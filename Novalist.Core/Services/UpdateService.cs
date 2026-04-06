using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Novalist.Core.Services;

public sealed class UpdateInfo
{
    public string Version { get; init; } = string.Empty;
    public string TagName { get; init; } = string.Empty;
    public string HtmlUrl { get; init; } = string.Empty;
    public string Body { get; init; } = string.Empty;
    public string DownloadUrl { get; init; } = string.Empty;
    public string AssetName { get; init; } = string.Empty;
    public long AssetSize { get; init; }
}

public interface IUpdateService
{
    Task<UpdateInfo?> CheckForUpdateAsync(CancellationToken ct = default);
    Task<string> DownloadUpdateAsync(UpdateInfo update, IProgress<double>? progress = null, CancellationToken ct = default);
    void LaunchInstaller(string installerPath);
}

public sealed class UpdateService : IUpdateService
{
    private const string ReleasesApiUrl = "https://api.github.com/repos/Drommedhar/novalist-official/releases/latest";

    private static readonly HttpClient Http = CreateHttpClient();

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Novalist-UpdateCheck");
        client.Timeout = TimeSpan.FromSeconds(15);
        return client;
    }

    public async Task<UpdateInfo?> CheckForUpdateAsync(CancellationToken ct = default)
    {
        var release = await Http.GetFromJsonAsync<GitHubRelease>(ReleasesApiUrl, ct);
        if (release is null || string.IsNullOrEmpty(release.TagName))
            return null;

        var remoteVersion = release.TagName.TrimStart('v', 'V');
        var currentVersion = StripPreRelease(VersionInfo.Version);

        if (!IsNewer(remoteVersion, currentVersion))
            return null;

        var asset = FindPlatformAsset(release);
        if (asset is null)
            return null;

        return new UpdateInfo
        {
            Version = remoteVersion,
            TagName = release.TagName,
            HtmlUrl = release.HtmlUrl ?? string.Empty,
            Body = release.Body ?? string.Empty,
            DownloadUrl = asset.BrowserDownloadUrl ?? string.Empty,
            AssetName = asset.Name ?? string.Empty,
            AssetSize = asset.Size
        };
    }

    public async Task<string> DownloadUpdateAsync(UpdateInfo update, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var downloadDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Novalist", "Updates");
        Directory.CreateDirectory(downloadDir);

        var filePath = Path.Combine(downloadDir, update.AssetName);

        // If already downloaded with correct size, skip
        if (File.Exists(filePath))
        {
            var existing = new FileInfo(filePath);
            if (existing.Length == update.AssetSize)
            {
                progress?.Report(1.0);
                return filePath;
            }

            File.Delete(filePath);
        }

        using var response = await Http.GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? update.AssetSize;
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

    public void LaunchInstaller(string installerPath)
    {
        if (!File.Exists(installerPath))
            return;

        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = installerPath,
            UseShellExecute = true
        };

        // On macOS, open the DMG via the system handler
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            psi.FileName = "open";
            psi.Arguments = $"\"{installerPath}\"";
        }

        System.Diagnostics.Process.Start(psi);
    }

    private static GitHubReleaseAsset? FindPlatformAsset(GitHubRelease release)
    {
        if (release.Assets is null || release.Assets.Length == 0)
            return null;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return release.Assets.FirstOrDefault(a =>
                a.Name != null && a.Name.Contains("windows", StringComparison.OrdinalIgnoreCase) &&
                a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return release.Assets.FirstOrDefault(a =>
                a.Name != null && a.Name.Contains("macos", StringComparison.OrdinalIgnoreCase) &&
                a.Name.EndsWith(".dmg", StringComparison.OrdinalIgnoreCase));

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return release.Assets.FirstOrDefault(a =>
                a.Name != null && a.Name.Contains("linux", StringComparison.OrdinalIgnoreCase));

        return null;
    }

    private static string StripPreRelease(string version)
    {
        var dash = version.IndexOf('-');
        return dash >= 0 ? version[..dash] : version;
    }

    private static bool IsNewer(string remote, string current)
    {
        var remoteParts = ParseParts(remote);
        var currentParts = ParseParts(current);

        for (var i = 0; i < 3; i++)
        {
            var r = i < remoteParts.Length ? remoteParts[i] : 0;
            var c = i < currentParts.Length ? currentParts[i] : 0;
            if (r > c) return true;
            if (r < c) return false;
        }

        return false;
    }

    private static int[] ParseParts(string version)
    {
        var parts = version.Split('.');
        var result = new int[parts.Length];
        for (var i = 0; i < parts.Length; i++)
            int.TryParse(parts[i], out result[i]);
        return result;
    }

    // GitHub API DTOs
    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")] public string? TagName { get; set; }
        [JsonPropertyName("html_url")] public string? HtmlUrl { get; set; }
        [JsonPropertyName("body")] public string? Body { get; set; }
        [JsonPropertyName("prerelease")] public bool Prerelease { get; set; }
        [JsonPropertyName("assets")] public GitHubReleaseAsset[]? Assets { get; set; }
    }

    private sealed class GitHubReleaseAsset
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("browser_download_url")] public string? BrowserDownloadUrl { get; set; }
        [JsonPropertyName("size")] public long Size { get; set; }
    }
}
