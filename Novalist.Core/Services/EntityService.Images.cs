using System.Security.Cryptography;
using System.Text.Json;
using Novalist.Core.Models;

namespace Novalist.Core.Services;

public partial class EntityService
{
    private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".gif", ".bmp", ".svg", ".webp"];

    public async Task<string> ImportImageAsync(string sourcePath)
    {
        var imageDir = Path.Combine(BookRoot, Book.ImageFolder);
        await _files.CreateDirectoryAsync(imageDir);

        var sourceHash = await ComputeFileHashAsync(sourcePath);
        var sourceFullPath = Path.GetFullPath(sourcePath);

        foreach (var existingPath in (await _files.GetFilesAsync(imageDir))
                     .Where(file => ImageExtensions.Contains(Path.GetExtension(file).ToLowerInvariant())))
        {
            if (string.Equals(Path.GetFullPath(existingPath), sourceFullPath, StringComparison.OrdinalIgnoreCase))
                return Path.Combine(Book.ImageFolder, Path.GetFileName(existingPath)).Replace('\\', '/');

            var existingHash = await ComputeFileHashAsync(existingPath);
            if (string.Equals(existingHash, sourceHash, StringComparison.OrdinalIgnoreCase))
                return Path.Combine(Book.ImageFolder, Path.GetFileName(existingPath)).Replace('\\', '/');
        }

        var fileName = Path.GetFileName(sourcePath);
        var destName = await GetUniqueImageFileNameAsync(imageDir, fileName);
        var destPath = Path.Combine(imageDir, destName);

        if (!string.Equals(Path.GetFullPath(destPath), sourceFullPath, StringComparison.OrdinalIgnoreCase))
            await _files.WriteBytesAsync(destPath, await _files.ReadBytesAsync(sourcePath));

        return Path.Combine(Book.ImageFolder, destName).Replace('\\', '/');
    }

    // The extension SDK returns this list synchronously. Start file-provider work off the caller's context so it can complete while that caller waits.
    // aislop-ignore-next-line ai-slop/csharp-sync-over-async -- Retains the published synchronous SDK contract; renderer RPC calls the asynchronous entry point.
    public List<string> GetProjectImages() => Task.Run(GetProjectImagesAsync).GetAwaiter().GetResult();

    public async Task<List<string>> GetProjectImagesAsync()
    {
        var results = new List<string>();

        // Book images (recursive to support subdirectories)
        var bookImageDir = Path.Combine(BookRoot, Book.ImageFolder);
        results.AddRange((await _files.GetFilesAsync(bookImageDir, recursive: true).ConfigureAwait(false))
            .Where(f => ImageExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .Select(f => Path.GetRelativePath(BookRoot, f).Replace('\\', '/')));

        // World Bible images
        if (WorldBibleRoot != null)
        {
            var wbImageDir = Path.Combine(WorldBibleRoot, Project.ImageFolder);
            results.AddRange((await _files.GetFilesAsync(wbImageDir, recursive: true).ConfigureAwait(false))
                .Where(f => ImageExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .Select(f => Path.Combine(Project.WorldBibleFolder,
                    Path.GetRelativePath(WorldBibleRoot, f).Replace('\\', '/'))));
        }

        return results.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Images available to shared entries, with stable project-relative
    /// paths so choosing a portrait never binds it to the currently open book.</summary>
    public async Task<List<string>> GetWorldBibleImagesAsync()
    {
        var projectRoot = _projectService.ProjectRoot
            ?? throw new InvalidOperationException("No project loaded.");
        var results = (await GetProjectImagesAsync().ConfigureAwait(false))
            .Select(path => "./" + ResolveProjectRelativeImage(path)).ToList();
        foreach (var book in Project.Books.Where(book => book.Id != Book.Id))
        {
            var imageDir = Path.Combine(projectRoot, book.FolderName, book.ImageFolder);
            results.AddRange((await _files.GetFilesAsync(imageDir, recursive: true).ConfigureAwait(false))
                .Where(path => ImageExtensions.Contains(Path.GetExtension(path).ToLowerInvariant()))
                .Select(path => "./" + Path.GetRelativePath(projectRoot, path).Replace('\\', '/')));
        }
        return results.Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public string GetImageFullPath(string relativePath)
        => ResolveImagePath(relativePath).FullPath;

    private (string ProjectRoot, string FullPath) ResolveImagePath(string relativePath)
    {
        var projectRoot = _projectService.ProjectRoot ?? throw new InvalidOperationException("No project loaded.");

        var normalized = relativePath.Replace('\\', '/');
        // New shared references explicitly mark the project root. Inferring it
        // from book names would confuse a book named Images with Images/a.png.
        if (normalized.StartsWith("./", StringComparison.Ordinal))
            return (projectRoot, Path.Combine(projectRoot, normalized[2..]));
        if (normalized.StartsWith(Project.WorldBibleFolder.Replace('\\', '/').TrimEnd('/') + "/",
                StringComparison.OrdinalIgnoreCase))
            return (projectRoot, Path.Combine(projectRoot, normalized));

        return (projectRoot, Path.Combine(BookRoot, normalized));
    }

    /// <summary>
    /// Maps a stored image path (legacy book-root-relative, World Bible-prefixed,
    /// or explicitly project-relative with ./) to a project-relative URL, so the
    /// renderer's project-rooted <c>novalist-project://</c> protocol resolves it.
    /// The stored path is unchanged on disk; this is a display-only projection.
    /// </summary>
    public string ResolveProjectRelativeImage(string relativePath)
    {
        var (projectRoot, full) = ResolveImagePath(relativePath);
        return Path.GetRelativePath(projectRoot, full).Replace('\\', '/');
    }

    private async Task<string> ComputeFileHashAsync(string filePath)
    {
        var bytes = await _files.ReadBytesAsync(filePath);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private async Task<string> GetUniqueImageFileNameAsync(string imageDir, string originalFileName)
    {
        var baseName = Path.GetFileNameWithoutExtension(originalFileName);
        var extension = Path.GetExtension(originalFileName);
        var candidate = originalFileName;
        var suffix = 2;

        while (await _files.ExistsAsync(Path.Combine(imageDir, candidate)))
        {
            candidate = $"{baseName} ({suffix}){extension}";
            suffix++;
        }

        return candidate;
    }
}
