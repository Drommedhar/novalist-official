using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Novalist.Core.Models;

namespace Novalist.Core.Services;

public interface IExtensionGalleryService
{
    /// <summary>
    /// Optional GitHub personal access token to increase API rate limits.
    /// </summary>
    string? GitHubToken { get; set; }

    /// <summary>
    /// Fetches the gallery index from the gallery repository.
    /// </summary>
    Task<List<GalleryEntry>> FetchGalleryIndexAsync(CancellationToken ct = default);

    /// <summary>
    /// Fetches releases for an extension's GitHub repository, filtering out pre-releases
    /// and returning only releases that have a valid ZIP asset.
    /// </summary>
    Task<List<GalleryRelease>> FetchReleasesAsync(GalleryEntry entry, CancellationToken ct = default);

    /// <summary>
    /// Returns the latest release that is compatible with the current Novalist version,
    /// or null if no compatible release exists.
    /// </summary>
    Task<GalleryRelease?> GetLatestCompatibleReleaseAsync(GalleryEntry entry, CancellationToken ct = default);

    /// <summary>
    /// Fetches the README.md content from the extension's GitHub repository.
    /// </summary>
    Task<string> FetchReadmeAsync(string repo, CancellationToken ct = default);

    /// <summary>
    /// Downloads the extension ZIP to a temporary directory and returns the file path.
    /// </summary>
    Task<string> DownloadExtensionZipAsync(GalleryRelease release, IProgress<double>? progress = null, CancellationToken ct = default);

    /// <summary>
    /// Extracts the downloaded ZIP into the extensions directory and writes store-meta.json.
    /// </summary>
    Task InstallExtensionAsync(string zipPath, GalleryEntry entry, GalleryRelease release, CancellationToken ct = default);

    /// <summary>
    /// Deletes the extension folder from the extensions directory.
    /// </summary>
    Task UninstallExtensionAsync(string extensionId, CancellationToken ct = default);

    /// <summary>
    /// Checks all installed gallery extensions for available updates.
    /// </summary>
    Task<List<ExtensionUpdateInfo>> CheckForUpdatesAsync(CancellationToken ct = default);

    /// <summary>
    /// Reads the store-meta.json for an installed extension, or null if not present.
    /// </summary>
    ExtensionStoreMeta? ReadStoreMeta(string extensionId);
}

public sealed class ExtensionGalleryService : IExtensionGalleryService
{
    private const string GalleryIndexUrl =
        "https://raw.githubusercontent.com/Drommedhar/novalist-extension-gallery/main/gallery.json";

    private const string GitHubApiBase = "https://api.github.com";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http;

    // In-memory cache
    private List<GalleryEntry>? _cachedIndex;
    private readonly Dictionary<string, string> _readmeCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<GalleryRelease>> _releaseCache = new(StringComparer.OrdinalIgnoreCase);

    public string? GitHubToken { get; set; }

    public ExtensionGalleryService()
    {
        _http = CreateHttpClient();
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Novalist-ExtensionStore");
        client.Timeout = TimeSpan.FromSeconds(30);
        return client;
    }

    private void ApplyAuth(HttpRequestMessage request)
    {
        if (!string.IsNullOrWhiteSpace(GitHubToken))
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", GitHubToken);
    }

    /// <summary>
    /// Checks GitHub API responses for rate limiting (403/429) and throws a clear exception.
    /// </summary>
    private static void ThrowOnRateLimit(HttpResponseMessage response)
    {
        if (response.StatusCode is System.Net.HttpStatusCode.Forbidden or (System.Net.HttpStatusCode)429)
        {
            var remaining = response.Headers.TryGetValues("X-RateLimit-Remaining", out var vals)
                ? vals.FirstOrDefault() : null;

            if (remaining == "0" || response.StatusCode == (System.Net.HttpStatusCode)429)
            {
                var resetHeader = response.Headers.TryGetValues("X-RateLimit-Reset", out var rv)
                    ? rv.FirstOrDefault() : null;
                var resetMsg = "";
                if (long.TryParse(resetHeader, out var epoch))
                {
                    var resetTime = DateTimeOffset.FromUnixTimeSeconds(epoch).LocalDateTime;
                    resetMsg = $" Resets at {resetTime:HH:mm}.";
                }
                throw new GalleryRateLimitException(
                    $"GitHub API rate limit exceeded.{resetMsg} Add a Personal Access Token in Settings to increase the limit.");
            }
        }
    }

    // ── Gallery Index ─────────────────────────────────────────────────

    public async Task<List<GalleryEntry>> FetchGalleryIndexAsync(CancellationToken ct = default)
    {
        if (_cachedIndex is not null)
            return _cachedIndex;

        using var request = new HttpRequestMessage(HttpMethod.Get, GalleryIndexUrl);
        ApplyAuth(request);

        using var response = await _http.SendAsync(request, ct);
        ThrowOnRateLimit(response);
        response.EnsureSuccessStatusCode();

        var entries = await response.Content.ReadFromJsonAsync<List<GalleryEntry>>(JsonOptions, ct)
                      ?? [];

        _cachedIndex = entries;
        return entries;
    }

    // ── Releases ──────────────────────────────────────────────────────

    public async Task<List<GalleryRelease>> FetchReleasesAsync(GalleryEntry entry, CancellationToken ct = default)
    {
        if (_releaseCache.TryGetValue(entry.Id, out var cached))
            return cached;

        var url = $"{GitHubApiBase}/repos/{entry.Repo}/releases";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        ApplyAuth(request);

        using var response = await _http.SendAsync(request, ct);
        ThrowOnRateLimit(response);
        response.EnsureSuccessStatusCode();

        var ghReleases = await response.Content.ReadFromJsonAsync<GitHubRelease[]>(JsonOptions, ct)
                         ?? [];

        var results = new List<GalleryRelease>();
        var expectedAssetName = $"{entry.Id}.zip";

        foreach (var r in ghReleases)
        {
            if (r.Prerelease || r.Draft)
                continue;

            var asset = r.Assets?.FirstOrDefault(a =>
                string.Equals(a.Name, expectedAssetName, StringComparison.OrdinalIgnoreCase));

            if (asset is null || string.IsNullOrEmpty(asset.BrowserDownloadUrl))
                continue;

            results.Add(new GalleryRelease
            {
                TagName = r.TagName ?? string.Empty,
                Version = (r.TagName ?? string.Empty).TrimStart('v', 'V'),
                Body = r.Body ?? string.Empty,
                IsPrerelease = r.Prerelease,
                ZipDownloadUrl = asset.BrowserDownloadUrl,
                ZipSize = asset.Size,
                PublishedAt = r.PublishedAt
            });
        }

        // Newest first
        results.Sort((a, b) => b.PublishedAt.CompareTo(a.PublishedAt));

        _releaseCache[entry.Id] = results;
        return results;
    }

    public async Task<GalleryRelease?> GetLatestCompatibleReleaseAsync(GalleryEntry entry, CancellationToken ct = default)
    {
        var releases = await FetchReleasesAsync(entry, ct);

        foreach (var release in releases)
        {
            if (await IsReleaseCompatibleAsync(entry, release, ct))
                return release;
        }

        return null;
    }

    // ── README ────────────────────────────────────────────────────────

    public async Task<string> FetchReadmeAsync(string repo, CancellationToken ct = default)
    {
        if (_readmeCache.TryGetValue(repo, out var cached))
            return cached;

        var url = $"{GitHubApiBase}/repos/{repo}/readme";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.ParseAdd("application/vnd.github.raw+json");
        ApplyAuth(request);

        using var response = await _http.SendAsync(request, ct);
        ThrowOnRateLimit(response);
        if (!response.IsSuccessStatusCode)
            return string.Empty;

        var readme = await response.Content.ReadAsStringAsync(ct);
        _readmeCache[repo] = readme;
        return readme;
    }

    // ── Download ──────────────────────────────────────────────────────

    public async Task<string> DownloadExtensionZipAsync(GalleryRelease release, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var downloadDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Novalist", "ExtensionDownloads");
        Directory.CreateDirectory(downloadDir);

        var fileName = Path.GetFileName(new Uri(release.ZipDownloadUrl).AbsolutePath);
        var filePath = Path.Combine(downloadDir, fileName);

        // Skip if already downloaded with correct size
        if (File.Exists(filePath) && new FileInfo(filePath).Length == release.ZipSize)
        {
            progress?.Report(1.0);
            return filePath;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, release.ZipDownloadUrl);
        ApplyAuth(request);

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        ThrowOnRateLimit(response);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? release.ZipSize;
        await using var contentStream = await response.Content.ReadAsStreamAsync(ct);
        await using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

        var buffer = new byte[81920];
        long bytesRead = 0;
        int read;
        while ((read = await contentStream.ReadAsync(buffer, ct)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, read), ct);
            bytesRead += read;
            if (totalBytes > 0)
                progress?.Report((double)bytesRead / totalBytes);
        }

        progress?.Report(1.0);
        return filePath;
    }

    // ── Install ───────────────────────────────────────────────────────

    public async Task InstallExtensionAsync(string zipPath, GalleryEntry entry, GalleryRelease release, CancellationToken ct = default)
    {
        var extensionsDir = GetExtensionsDirectory();
        var targetDir = Path.Combine(extensionsDir, entry.Id);

        // Try to remove existing installation; if files are locked (loaded DLLs),
        // fall through and overwrite in place instead.
        if (Directory.Exists(targetDir))
        {
            try
            {
                Directory.Delete(targetDir, true);
            }
            catch (UnauthorizedAccessException)
            {
                // DLLs still loaded in memory — overwrite in place
            }
            catch (IOException)
            {
                // File locked — overwrite in place
            }
        }

        Directory.CreateDirectory(targetDir);

        // Extract ZIP with path traversal protection
        using var archive = ZipFile.OpenRead(zipPath);
        foreach (var zipEntry in archive.Entries)
        {
            // Skip directory entries
            if (string.IsNullOrEmpty(zipEntry.Name))
                continue;

            var destinationPath = Path.GetFullPath(Path.Combine(targetDir, zipEntry.FullName));

            // Prevent path traversal attacks
            if (!destinationPath.StartsWith(Path.GetFullPath(targetDir), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"ZIP entry attempts path traversal: {zipEntry.FullName}");

            // Ensure subdirectory exists
            var entryDir = Path.GetDirectoryName(destinationPath);
            if (entryDir is not null)
                Directory.CreateDirectory(entryDir);

            zipEntry.ExtractToFile(destinationPath, overwrite: true);
        }

        // Validate extension.json exists and id matches
        var manifestPath = Path.Combine(targetDir, "extension.json");
        if (!File.Exists(manifestPath))
        {
            Directory.Delete(targetDir, true);
            throw new InvalidOperationException("ZIP does not contain extension.json at the root level.");
        }

        var manifestJson = await File.ReadAllTextAsync(manifestPath, ct);
        var manifest = JsonSerializer.Deserialize<ManifestIdCheck>(manifestJson, JsonOptions);
        if (manifest is null || !string.Equals(manifest.Id, entry.Id, StringComparison.OrdinalIgnoreCase))
        {
            Directory.Delete(targetDir, true);
            throw new InvalidOperationException(
                $"extension.json id '{manifest?.Id}' does not match expected id '{entry.Id}'.");
        }

        // Write store-meta.json
        var meta = new ExtensionStoreMeta
        {
            InstalledFromGallery = true,
            Repo = entry.Repo,
            InstalledVersion = release.Version,
            InstalledAt = DateTime.UtcNow
        };
        var metaJson = JsonSerializer.Serialize(meta, JsonOptions);
        await File.WriteAllTextAsync(Path.Combine(targetDir, "store-meta.json"), metaJson, ct);

        // Clean up downloaded ZIP
        try { File.Delete(zipPath); } catch { /* best effort */ }
    }

    // ── Uninstall ─────────────────────────────────────────────────────

    public Task UninstallExtensionAsync(string extensionId, CancellationToken ct = default)
    {
        var targetDir = Path.Combine(GetExtensionsDirectory(), extensionId);
        if (Directory.Exists(targetDir))
            Directory.Delete(targetDir, true);

        return Task.CompletedTask;
    }

    // ── Update checks ─────────────────────────────────────────────────

    public async Task<List<ExtensionUpdateInfo>> CheckForUpdatesAsync(CancellationToken ct = default)
    {
        var updates = new List<ExtensionUpdateInfo>();
        var index = await FetchGalleryIndexAsync(ct);
        var extensionsDir = GetExtensionsDirectory();

        if (!Directory.Exists(extensionsDir))
            return updates;

        foreach (var entry in index)
        {
            ct.ThrowIfCancellationRequested();

            var meta = ReadStoreMeta(entry.Id);
            if (meta is null || !meta.InstalledFromGallery)
                continue;

            try
            {
                var latestRelease = await GetLatestCompatibleReleaseAsync(entry, ct);
                if (latestRelease is null)
                    continue;

                if (IsNewer(latestRelease.Version, meta.InstalledVersion))
                {
                    updates.Add(new ExtensionUpdateInfo
                    {
                        ExtensionId = entry.Id,
                        InstalledVersion = meta.InstalledVersion,
                        AvailableVersion = latestRelease.Version,
                        Release = latestRelease,
                        Entry = entry
                    });
                }
            }
            catch
            {
                // Skip extensions where we can't fetch releases (network error, repo gone, etc.)
            }
        }

        return updates;
    }

    // ── Store meta ────────────────────────────────────────────────────

    public ExtensionStoreMeta? ReadStoreMeta(string extensionId)
    {
        var metaPath = Path.Combine(GetExtensionsDirectory(), extensionId, "store-meta.json");
        if (!File.Exists(metaPath))
            return null;

        try
        {
            var json = File.ReadAllText(metaPath);
            return JsonSerializer.Deserialize<ExtensionStoreMeta>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Clears all in-memory caches. Called when the user wants to force-refresh.
    /// </summary>
    public void ClearCache()
    {
        _cachedIndex = null;
        _readmeCache.Clear();
        _releaseCache.Clear();
    }

    // ── Compatibility ─────────────────────────────────────────────────

    private async Task<bool> IsReleaseCompatibleAsync(GalleryEntry entry, GalleryRelease release, CancellationToken ct)
    {
        // Fetch the extension.json from the tagged commit to check compatibility
        // and extract the icon URL without downloading the full ZIP
        var tag = release.TagName;
        var url = $"https://raw.githubusercontent.com/{entry.Repo}/{tag}/extension.json";

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            ApplyAuth(request);

            using var response = await _http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
                return true; // If we can't fetch, assume compatible (will be validated on install)

            var json = await response.Content.ReadAsStringAsync(ct);
            var manifest = JsonSerializer.Deserialize<CompatibilityManifest>(json, JsonOptions);
            if (manifest is null)
                return true;

            // Populate icon from the extension manifest
            release.Icon = manifest.Icon;

            // Check minHostVersion
            if (!string.IsNullOrWhiteSpace(manifest.MinHostVersion))
            {
                if (!VersionInfo.IsCompatibleWith(manifest.MinHostVersion))
                    return false;
            }

            // Check maxHostVersion
            if (!string.IsNullOrWhiteSpace(manifest.MaxHostVersion))
            {
                if (!IsWithinMaxVersion(manifest.MaxHostVersion))
                    return false;
            }

            return true;
        }
        catch
        {
            return true; // Network error — assume compatible, validate on install
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────

    private static string GetExtensionsDirectory()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "Novalist", "Extensions");
    }

    private static bool IsNewer(string remote, string current)
    {
        var remoteParts = ParseVersionParts(remote);
        var currentParts = ParseVersionParts(current);

        for (var i = 0; i < 3; i++)
        {
            var r = i < remoteParts.Length ? remoteParts[i] : 0;
            var c = i < currentParts.Length ? currentParts[i] : 0;
            if (r > c) return true;
            if (r < c) return false;
        }

        return false;
    }

    private static bool IsWithinMaxVersion(string maxVersion)
    {
        var hostVersion = StripPreRelease(VersionInfo.Version);
        var max = StripPreRelease(maxVersion);

        if (Version.TryParse(hostVersion, out var hostVer) && Version.TryParse(max, out var maxVer))
            return hostVer <= maxVer;

        return true;
    }

    private static string StripPreRelease(string version)
    {
        var dash = version.IndexOf('-');
        return dash >= 0 ? version[..dash] : version;
    }

    private static int[] ParseVersionParts(string version)
    {
        version = StripPreRelease(version);
        var parts = version.Split('.');
        var result = new int[parts.Length];
        for (var i = 0; i < parts.Length; i++)
            int.TryParse(parts[i], out result[i]);
        return result;
    }

    // ── GitHub API DTOs (private) ─────────────────────────────────────

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")] public string? TagName { get; set; }
        [JsonPropertyName("body")] public string? Body { get; set; }
        [JsonPropertyName("prerelease")] public bool Prerelease { get; set; }
        [JsonPropertyName("draft")] public bool Draft { get; set; }
        [JsonPropertyName("published_at")] public DateTime PublishedAt { get; set; }
        [JsonPropertyName("assets")] public GitHubReleaseAsset[]? Assets { get; set; }
    }

    private sealed class GitHubReleaseAsset
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("browser_download_url")] public string? BrowserDownloadUrl { get; set; }
        [JsonPropertyName("size")] public long Size { get; set; }
    }

    private sealed class ManifestIdCheck
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
    }

    private sealed class CompatibilityManifest
    {
        [JsonPropertyName("minHostVersion")] public string? MinHostVersion { get; set; }
        [JsonPropertyName("maxHostVersion")] public string? MaxHostVersion { get; set; }
        [JsonPropertyName("icon")] public string? Icon { get; set; }
    }
}

/// <summary>
/// Thrown when the GitHub API rate limit has been exceeded.
/// </summary>
public sealed class GalleryRateLimitException : Exception
{
    public GalleryRateLimitException(string message) : base(message) { }
}
