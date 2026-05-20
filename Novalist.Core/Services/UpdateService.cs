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

    private static readonly HttpClient SharedHttp = CreateHttpClient();

    private readonly HttpClient _http;
    private readonly string _downloadDir;

    // OS-detection seams so the per-platform asset selection is unit-testable
    // from any host OS (defaults delegate to the real runtime).
    internal static Func<OSPlatform, bool> IsOsPlatform { get; set; } = RuntimeInformation.IsOSPlatform;
    internal static Func<Architecture> OsArchitecture { get; set; } = () => RuntimeInformation.OSArchitecture;

    /// <param name="http">HTTP client; defaults to a shared client with the update User-Agent.</param>
    /// <param name="downloadDir">Download target; defaults to %LocalAppData%/Novalist/Updates.</param>
    public UpdateService(HttpClient? http = null, string? downloadDir = null)
    {
        _http = http ?? SharedHttp;
        _downloadDir = downloadDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Novalist", "Updates");
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Novalist-UpdateCheck");
        client.Timeout = TimeSpan.FromSeconds(15);
        return client;
    }

    public async Task<UpdateInfo?> CheckForUpdateAsync(CancellationToken ct = default)
    {
        var release = await _http.GetFromJsonAsync<GitHubRelease>(ReleasesApiUrl, ct);
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
        Directory.CreateDirectory(_downloadDir);

        var filePath = Path.Combine(_downloadDir, update.AssetName);

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

        using var response = await _http.GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
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

    // Installer launch is OS-specific, side-effecting (spawns the installer),
    // and physically exercises a different branch per platform — excluded from
    // coverage by explicit decision rather than abstracting real process launch.
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public void LaunchInstaller(string installerPath)
    {
        if (!File.Exists(installerPath))
            return;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            LaunchLinuxAppImageUpdate(installerPath);
            return;
        }

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

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    private static void LaunchLinuxAppImageUpdate(string newAppImagePath)
    {
        // The new download is always made executable; if we end up launching it
        // from the downloads folder (read-only install location fallback) it
        // still needs the exec bit.
        TrySetExecutable(newAppImagePath);

        // The AppImage runtime sets APPIMAGE to the absolute path of the
        // running .AppImage file. If it's missing we're not running from one
        // (developer build, extracted bundle, etc.) — fall back to opening the
        // download folder so the user can move the file themselves.
        var currentAppImage = Environment.GetEnvironmentVariable("APPIMAGE");
        if (string.IsNullOrEmpty(currentAppImage) || !File.Exists(currentAppImage))
        {
            OpenFolder(Path.GetDirectoryName(newAppImagePath) ?? string.Empty);
            return;
        }

        // Write a detached bash script that waits for *this* process to exit,
        // replaces the running AppImage in place, and re-launches it. If the
        // in-place replacement fails (read-only mount, permission denied) it
        // launches the freshly downloaded copy instead so the user is never
        // left without a working app.
        var pid = Environment.ProcessId;
        var scriptPath = Path.Combine(Path.GetTempPath(),
            $"novalist-update-{Guid.NewGuid():N}.sh");

        var newQ = BashQuote(newAppImagePath);
        var curQ = BashQuote(currentAppImage);
        var script =
            "#!/bin/bash\n" +
            "set -u\n" +
            $"while kill -0 {pid} 2>/dev/null; do sleep 0.2; done\n" +
            "sleep 0.5\n" +
            $"if mv -f {newQ} {curQ} 2>/dev/null; then\n" +
            $"  chmod +x {curQ}\n" +
            $"  TARGET={curQ}\n" +
            "else\n" +
            $"  chmod +x {newQ}\n" +
            $"  TARGET={newQ}\n" +
            "fi\n" +
            "setsid nohup \"$TARGET\" </dev/null >/dev/null 2>&1 &\n" +
            "rm -f \"$0\"\n";

        File.WriteAllText(scriptPath, script);
        TrySetExecutable(scriptPath);

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "/bin/bash",
            Arguments = $"\"{scriptPath}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
        });
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    private static void TrySetExecutable(string path)
    {
        try
        {
            File.SetUnixFileMode(path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }
        catch { /* best effort */ }
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    private static void OpenFolder(string folder)
    {
        if (string.IsNullOrEmpty(folder)) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "xdg-open",
                Arguments = $"\"{folder}\"",
                UseShellExecute = false,
            });
        }
        catch { /* best effort */ }
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    private static string BashQuote(string s) =>
        "'" + s.Replace("'", "'\\''") + "'";

    private static GitHubReleaseAsset? FindPlatformAsset(GitHubRelease release)
    {
        if (release.Assets is null || release.Assets.Length == 0)
            return null;

        if (IsOsPlatform(OSPlatform.Windows))
            return release.Assets.FirstOrDefault(a =>
                a.Name != null && a.Name.Contains("windows", StringComparison.OrdinalIgnoreCase) &&
                a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));

        if (IsOsPlatform(OSPlatform.OSX))
        {
            var archSuffix = OsArchitecture() == Architecture.Arm64 ? "arm64" : "x64";
            var macAssets = release.Assets.Where(a =>
                a.Name != null && a.Name.Contains("macos", StringComparison.OrdinalIgnoreCase) &&
                a.Name.EndsWith(".dmg", StringComparison.OrdinalIgnoreCase)).ToArray();

            return macAssets.FirstOrDefault(a => a.Name!.Contains(archSuffix, StringComparison.OrdinalIgnoreCase))
                ?? macAssets.FirstOrDefault();
        }

        if (IsOsPlatform(OSPlatform.Linux))
            return release.Assets.FirstOrDefault(a =>
                a.Name != null && a.Name.EndsWith(".AppImage", StringComparison.OrdinalIgnoreCase));

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
