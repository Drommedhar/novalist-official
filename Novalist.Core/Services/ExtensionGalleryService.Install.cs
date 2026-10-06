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

public sealed partial class ExtensionGalleryService
{
    // ── Download ──────────────────────────────────────────────────────

    public async Task<string> DownloadExtensionZipAsync(GalleryRelease release, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        Directory.CreateDirectory(_downloadDir);

        var fileName = Path.GetFileName(new Uri(release.ZipDownloadUrl).AbsolutePath);
        var filePath = Path.Combine(_downloadDir, fileName);

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
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                // DLLs still loaded / files locked — fall through and overwrite in place.
            }
        }

        Directory.CreateDirectory(targetDir);

        // Extract ZIP with path traversal protection. Scope the archive so its
        // file handle is released before we delete the downloaded ZIP below —
        // otherwise the delete fails (file still open) on Windows and the ZIP leaks.
        using (var archive = ZipFile.OpenRead(zipPath))
        {
            foreach (var zipEntry in archive.Entries)
            {
                // Skip directory entries
                if (string.IsNullOrEmpty(zipEntry.Name))
                    continue;

                var destinationPath = Path.GetFullPath(Path.Combine(targetDir, zipEntry.FullName));

                // Prevent path traversal attacks
                if (!destinationPath.StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(targetDir)) + Path.DirectorySeparatorChar,
                        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                    throw new InvalidOperationException($"ZIP entry attempts path traversal: {zipEntry.FullName}");

                // Ensure subdirectory exists
                var entryDir = Path.GetDirectoryName(destinationPath);
                if (entryDir is not null)
                    Directory.CreateDirectory(entryDir);

                zipEntry.ExtractToFile(destinationPath, overwrite: true);
            }
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

    // ── Store meta ────────────────────────────────────────────────────

    /// <summary>
    /// The version stamped in the installed extension's own manifest
    /// (extension.json) — the ground truth of what is on disk. Update checks use
    /// this rather than store-meta's recorded install tag, which can disagree.
    /// Returns null when the extension is not installed or the manifest is unreadable.
    /// </summary>
    public string? ReadInstalledManifestVersion(string extensionId)
    {
        var manifestPath = Path.Combine(GetExtensionsDirectory(), extensionId, "extension.json");
        if (!File.Exists(manifestPath))
            return null;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
            return doc.RootElement.TryGetProperty("version", out var v) ? v.GetString() : null;
        }
        catch
        {
            return null;
        }
    }

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

    // ── Helpers ───────────────────────────────────────────────────────

    private string GetExtensionsDirectory() => _extensionsDir;

    private sealed class ManifestIdCheck
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
    }
}
