using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
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
    /// The README to show for an extension: the one its latest release
    /// published, and the repository's own when it published none.
    /// </summary>
    Task<string> FetchReadmeAsync(string repo, string? extensionId = null, CancellationToken ct = default);

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

    /// <summary>The version in the installed extension's own manifest, or null.</summary>
    string? ReadInstalledManifestVersion(string extensionId);
}

public sealed partial class ExtensionGalleryService : IExtensionGalleryService
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
    private readonly string _extensionsDir;
    private readonly string _downloadDir;

    // In-memory cache
    private List<GalleryEntry>? _cachedIndex;
    private readonly Dictionary<string, string> _readmeCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<GalleryRelease>> _releaseCache = new(StringComparer.OrdinalIgnoreCase);

    public string? GitHubToken { get; set; }

    /// <param name="http">HTTP client; defaults to a shared client with the store User-Agent.</param>
    /// <param name="extensionsDir">Installed-extensions directory; defaults to %APPDATA%/Novalist/Extensions.</param>
    /// <param name="downloadDir">ZIP download directory; defaults to %LocalAppData%/Novalist/ExtensionDownloads.</param>
    public ExtensionGalleryService(HttpClient? http = null, string? extensionsDir = null, string? downloadDir = null)
    {
        _http = http ?? CreateHttpClient();
        _extensionsDir = extensionsDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Novalist", "Extensions");
        _downloadDir = downloadDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Novalist", "ExtensionDownloads");
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

            string? Named(string suffix) => r.Assets?
                .FirstOrDefault(a => string.Equals(
                    a.Name, $"{entry.Id}{suffix}", StringComparison.OrdinalIgnoreCase))
                ?.BrowserDownloadUrl;

            var asset = r.Assets?.FirstOrDefault(a =>
                string.Equals(a.Name, expectedAssetName, StringComparison.OrdinalIgnoreCase));

            if (asset is null || string.IsNullOrEmpty(asset.BrowserDownloadUrl))
                continue;

            results.Add(new GalleryRelease
            {
                TagName = r.TagName ?? string.Empty,
                Version = VersionFromTag(r.TagName),
                Body = r.Body ?? string.Empty,
                IsPrerelease = r.Prerelease,
                ZipDownloadUrl = asset.BrowserDownloadUrl,
                ZipSize = asset.Size,
                ManifestUrl = Named(".extension.json"),
                ReadmeUrl = Named(".README.md"),
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

    public async Task<string> FetchReadmeAsync(
        string repo, string? extensionId = null, CancellationToken ct = default)
    {
        var key = string.IsNullOrWhiteSpace(extensionId) ? repo : $"{repo}#{extensionId}";
        if (_readmeCache.TryGetValue(key, out var cached))
            return cached;

        // A release that published its own README describes one extension; the
        // repository's README describes the repository, which is the wrong page
        // when four of them live in it.
        var url = $"{GitHubApiBase}/repos/{repo}/readme";
        var raw = false;
        if (!string.IsNullOrWhiteSpace(extensionId))
        {
            var entry = new GalleryEntry { Id = extensionId, Repo = repo };
            var releases = await FetchReleasesAsync(entry, ct);
            var publishedUrl = releases.Select(release => release.ReadmeUrl)
                .FirstOrDefault(url => !string.IsNullOrWhiteSpace(url));
            if (!string.IsNullOrWhiteSpace(publishedUrl))
            {
                url = publishedUrl;
                raw = true;
            }
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (!raw) request.Headers.Accept.ParseAdd("application/vnd.github.raw+json");
        ApplyAuth(request);

        using var response = await _http.SendAsync(request, ct);
        ThrowOnRateLimit(response);
        if (!response.IsSuccessStatusCode)
            return string.Empty;

        var readme = await response.Content.ReadAsStringAsync(ct);
        _readmeCache[key] = readme;
        return readme;
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
}

/// <summary>
/// Thrown when the GitHub API rate limit has been exceeded.
/// </summary>
public sealed class GalleryRateLimitException : Exception
{
    public GalleryRateLimitException(string message) : base(message) { }
}
