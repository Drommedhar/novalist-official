using System.Net.Http;
using Novalist.Backend.Extensions;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Sdk.Services;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

/// <summary>
/// Extension-store surface over <see cref="ExtensionGalleryService"/>: browse the
/// remote gallery, read a per-extension README and release notes, and download +
/// install (or update) an extension with progress driven through the existing
/// <c>ui/progress/*</c> bridge (which also carries the Cancel round-trip). After a
/// successful install the loaded extension host is refreshed so the new extension
/// appears without an app restart.
/// </summary>
public sealed class ExtensionStoreRpc
{
    private readonly Workspace _workspace;
    private readonly HttpClient? _http;
    private readonly string? _downloadDir;
    private IExtensionGalleryService? _gallery;

    /// <param name="workspace">The owning workspace (settings, extension host).</param>
    /// <param name="http">Test seam: HTTP client for the gallery. Null in production
    /// so the service builds its own client with the store User-Agent.</param>
    /// <param name="downloadDir">Test seam: ZIP download directory. Null in production
    /// so the service defaults to %LocalAppData%/Novalist/ExtensionDownloads.</param>
    public ExtensionStoreRpc(Workspace workspace, HttpClient? http = null, string? downloadDir = null)
    {
        _workspace = workspace;
        _http = http;
        _downloadDir = downloadDir;
    }

    /// <summary>Lazily builds the gallery service pointed at the same extensions
    /// directory the host discovers from, and applies the current GitHub token.</summary>
    private async Task<IExtensionGalleryService> GalleryAsync()
    {
        await _workspace.Settings.LoadAsync();
        if (_gallery == null)
        {
            var extDir = _workspace.ExtensionsLoaderOverride?.ExtensionsDirectory
                ?? ExtensionLoader.GetExtensionsDirectory();
            _gallery = new ExtensionGalleryService(_http, extDir, _downloadDir);
        }
        _gallery.GitHubToken = _workspace.Settings.Settings.GitHubToken;
        return _gallery;
    }

    // ── Browse ──────────────────────────────────────────────────────

    /// <summary>Fetches the gallery index and, per entry, its latest compatible
    /// release plus installed/update state.</summary>
    [JsonRpcMethod("store/index")]
    public async Task<StoreEntryDto[]> IndexAsync()
    {
        // No gallery request at all from a build that could not install what it
        // came back with. See ExtensionLoader.ExtensionsDisabled.
        if (ExtensionLoader.DisabledByEnvironment)
            return [];

        var gallery = await GalleryAsync();
        var entries = await gallery.FetchGalleryIndexAsync();
        var result = new List<StoreEntryDto>(entries.Count);

        foreach (var entry in entries)
        {
            GalleryRelease? release = null;
            try
            {
                release = await gallery.GetLatestCompatibleReleaseAsync(entry);
            }
            catch
            {
                // Best-effort per entry: a repo whose releases can't be fetched
                // (network error, repo gone) still lists, just without a version.
            }

            var meta = gallery.ReadStoreMeta(entry.Id);
            var isInstalled = meta is { InstalledFromGallery: true };
            // Use the version actually on disk (extension.json), not the tag
            // store-meta recorded at install, so a stale-manifest release still
            // shows an available update instead of masking it.
            var installedVersion = isInstalled
                ? gallery.ReadInstalledManifestVersion(entry.Id) ?? meta!.InstalledVersion
                : null;
            var hasUpdate = isInstalled && release != null
                && !string.IsNullOrEmpty(installedVersion)
                && IsNewer(release.Version, installedVersion);

            result.Add(new StoreEntryDto(
                entry.Id, entry.Name, entry.Description, entry.Author, entry.Repo,
                entry.Tags.ToArray(), release?.Icon, release?.Version, release?.TagName,
                release != null, isInstalled, hasUpdate, installedVersion));
        }

        Log.Info($"Store index built: count={result.Count}.");
        return result.ToArray();
    }

    /// <summary>
    /// The README to show for one extension. With an id it prefers the README
    /// that extension's own release published, so several extensions sharing a
    /// repository do not all show the repository's front page.
    /// </summary>
    [JsonRpcMethod("store/readme")]
    public async Task<string> ReadmeAsync(string repo, string? id = null)
    {
        var gallery = await GalleryAsync();
        return await gallery.FetchReadmeAsync(repo, id);
    }

    /// <summary>Fetches the published releases (newest first) for an extension.</summary>
    [JsonRpcMethod("store/releases")]
    public async Task<StoreReleaseDto[]> ReleasesAsync(string id, string repo)
    {
        var gallery = await GalleryAsync();
        var releases = await gallery.FetchReleasesAsync(new GalleryEntry { Id = id, Repo = repo });
        return releases
            .Select(r => new StoreReleaseDto(r.TagName, r.Version, r.Body, r.PublishedAt))
            .ToArray();
    }

    /// <summary>Checks all gallery-installed extensions for available updates.</summary>
    [JsonRpcMethod("store/checkUpdates")]
    public async Task<StoreUpdateDto[]> CheckUpdatesAsync()
    {
        var gallery = await GalleryAsync();
        var updates = await gallery.CheckForUpdatesAsync();
        Log.Info($"Store update check: count={updates.Count}.");
        return updates
            .Select(u => new StoreUpdateDto(
                u.ExtensionId, u.Entry?.Name ?? u.ExtensionId, u.Entry?.Repo ?? string.Empty,
                u.InstalledVersion, u.AvailableVersion))
            .ToArray();
    }

    // ── Install / Update ────────────────────────────────────────────

    /// <summary>Downloads and installs the latest compatible release of an
    /// extension, reporting download progress (and honoring Cancel) through the
    /// <c>ui/progress/*</c> bridge, then reloads the host so it appears live.</summary>
    [JsonRpcMethod("store/install")]
    public Task<StoreInstallResultDto> InstallAsync(string id, string repo) => InstallCoreAsync(id, repo);

    /// <summary>Updates an installed extension to its latest compatible release.
    /// Identical mechanics to <see cref="InstallAsync"/> (download → install →
    /// reload); the loaded assembly is hot-swapped so no restart is required.</summary>
    [JsonRpcMethod("store/update")]
    public Task<StoreInstallResultDto> UpdateAsync(string id, string repo) => InstallCoreAsync(id, repo);

    private async Task<StoreInstallResultDto> InstallCoreAsync(string id, string repo)
    {
        if (ExtensionLoader.DisabledByEnvironment)
        {
            Log.Warn("Store install refused: extensions are disabled in this build.");
            return new StoreInstallResultDto(id, false, "disabled");
        }

        var gallery = await GalleryAsync();
        var entry = await ResolveEntryAsync(gallery, id, repo);
        var release = await gallery.GetLatestCompatibleReleaseAsync(entry);
        if (release == null)
        {
            Log.Warn($"Store install skipped, no compatible release: id={id}.");
            return new StoreInstallResultDto(id, false, "incompatible");
        }

        var progress = _workspace.UiBridge.CreateProgress(new BusyProgressOptions
        {
            Title = entry.Name,
            IsIndeterminate = false,
            ShowProgressBar = true,
            AllowCancel = true,
            IsModal = false
        });
        try
        {
            var ct = progress.CancellationToken;
            var zip = await gallery.DownloadExtensionZipAsync(
                release, new Progress<double>(p => progress.SetProgress(p)), ct);
            progress.SetIndeterminate(true);
            await gallery.InstallExtensionAsync(zip, entry, release, ct);
            await _workspace.ExtensionsHost.ReloadExtensionAsync(id);
            Log.Info($"Store install complete: id={id} ver={release.Version}.");
            return new StoreInstallResultDto(id, true, null);
        }
        catch (OperationCanceledException)
        {
            Log.Info($"Store install cancelled: id={id}.");
            return new StoreInstallResultDto(id, false, "cancelled");
        }
        catch (Exception ex)
        {
            Log.Warn($"Store install failed: id={id} err={ex.GetType().Name}.");
            return new StoreInstallResultDto(id, false, ex.Message);
        }
        finally
        {
            progress.Dispose();
        }
    }

    /// <summary>Resolves the full gallery entry (with name/tags) from the cached
    /// index, falling back to a minimal entry when the id is not in the gallery.</summary>
    private static async Task<GalleryEntry> ResolveEntryAsync(IExtensionGalleryService gallery, string id, string repo)
    {
        var index = await gallery.FetchGalleryIndexAsync();
        return index.FirstOrDefault(e => string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase))
            ?? new GalleryEntry { Id = id, Repo = repo };
    }

    // ── Version comparison (mirrors the gallery service's own rule) ──

    private static bool IsNewer(string remote, string current)
    {
        var r = ParseVersionParts(remote);
        var c = ParseVersionParts(current);
        for (var i = 0; i < 3; i++)
        {
            var rp = i < r.Length ? r[i] : 0;
            var cp = i < c.Length ? c[i] : 0;
            if (rp > cp) return true;
            if (rp < cp) return false;
        }
        return false;
    }

    private static int[] ParseVersionParts(string version)
    {
        var dash = version.IndexOf('-');
        if (dash >= 0) version = version[..dash];
        var parts = version.Split('.');
        var result = new int[parts.Length];
        for (var i = 0; i < parts.Length; i++)
            int.TryParse(parts[i], out result[i]);
        return result;
    }
}

/// <summary>A gallery extension plus its installed/update state, for the store list.</summary>
public sealed record StoreEntryDto(
    string Id,
    string Name,
    string Description,
    string Author,
    string Repo,
    string[] Tags,
    string? Icon,
    string? LatestVersion,
    string? ReleaseTag,
    bool IsCompatible,
    bool IsInstalled,
    bool HasUpdate,
    string? InstalledVersion);

/// <summary>A single published release, for the detail panel's release notes.</summary>
public sealed record StoreReleaseDto(string TagName, string Version, string Body, DateTime PublishedAt);

/// <summary>Result of an install/update attempt. <paramref name="Error"/> is null on
/// success, "cancelled" when the user cancelled, "incompatible" when no compatible
/// release exists, else the failure message.</summary>
public sealed record StoreInstallResultDto(string Id, bool Success, string? Error);

/// <summary>An available update for an installed extension.</summary>
public sealed record StoreUpdateDto(
    string ExtensionId, string Name, string Repo, string InstalledVersion, string AvailableVersion);
