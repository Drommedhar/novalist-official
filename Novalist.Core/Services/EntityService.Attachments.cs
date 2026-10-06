using System.Security.Cryptography;
using System.Text.Json;
using Novalist.Core.Models;

namespace Novalist.Core.Services;

public partial class EntityService
{
    // ── Images ──────────────────────────────────────────────────────

    /// <summary>
    /// Copies a file into the project's attachment folder and returns its
    /// book-relative path.
    ///
    /// Copied rather than referenced in place: a path into somebody's Downloads
    /// folder is a file that will be gone by the time anyone follows it, and a
    /// project has to stay portable when it is zipped or moved to another
    /// machine.
    /// </summary>
    public async Task<string> ImportAttachmentAsync(string sourcePath)
    {
        var folder = Path.Combine(BookRoot, AttachmentFolder);
        await _files.CreateDirectoryAsync(folder);

        // The same file attached twice is one file. Matched on content rather
        // than name, because a browser saves the third copy as "deed (2).pdf".
        var sourceHash = await ComputeFileHashAsync(sourcePath);
        var sourceFullPath = Path.GetFullPath(sourcePath);
        foreach (var existing in await _files.GetFilesAsync(folder))
        {
            if (string.Equals(Path.GetFullPath(existing), sourceFullPath, StringComparison.OrdinalIgnoreCase)
                || string.Equals(await ComputeFileHashAsync(existing), sourceHash, StringComparison.OrdinalIgnoreCase))
                return Path.Combine(AttachmentFolder, Path.GetFileName(existing)).Replace('\\', '/');
        }

        var destName = await UniqueFileNameAsync(folder, Path.GetFileName(sourcePath));
        var destPath = Path.Combine(folder, destName);
        if (!string.Equals(Path.GetFullPath(destPath), sourceFullPath, StringComparison.OrdinalIgnoreCase))
            await _files.WriteBytesAsync(destPath, await _files.ReadBytesAsync(sourcePath));

        return Path.Combine(AttachmentFolder, destName).Replace('\\', '/');
    }

    /// <summary>Where attachments live inside a book.</summary>
    public const string AttachmentFolder = "Attachments";

    public string GetAttachmentFullPath(string relativePath)
    {
        if (_projectService.ProjectRoot == null)
            throw new InvalidOperationException("No project loaded.");
        return Path.Combine(BookRoot, relativePath);
    }

    /// <summary>
    /// A name nothing in the folder already has. Suffixes rather than
    /// overwrites: two different files called scan.pdf are two files.
    /// </summary>
    private async Task<string> UniqueFileNameAsync(string folder, string fileName)
    {
        if (!await _files.ExistsAsync(Path.Combine(folder, fileName))) return fileName;
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        for (var n = 2; ; n++)
        {
            var candidate = $"{stem}-{n}{extension}";
            if (!await _files.ExistsAsync(Path.Combine(folder, candidate))) return candidate;
        }
    }
}
