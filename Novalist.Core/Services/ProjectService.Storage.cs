using System.Globalization;

namespace Novalist.Core.Services;

public partial class ProjectService
{
    private static string RequireStorageName(string name, string parameter)
    {
        var folder = SanitizeFileName(name);
        if (string.IsNullOrWhiteSpace(folder))
            throw new ArgumentException("Enter a name that can be used for a folder.", parameter);
        return folder;
    }

    private async Task ReserveDirectoryAsync(string destination)
    {
        if (await _fileService.DirectoryExistsAsync(destination) || await _fileService.ExistsAsync(destination))
            throw new IOException("Choose a new folder. The destination already exists.");

        var temporary = _fileService.CombinePath(_fileService.GetDirectoryName(destination),
            ".novalist-create-" + Guid.NewGuid().ToString("N"));
        try
        {
            await _fileService.CreateDirectoryAsync(temporary);
            // Publishing an empty reservation by no-overwrite rename closes the
            // gap between the existence check and the first project write.
            await _fileService.MoveDirectoryAsync(temporary, destination);
        }
        finally { await _fileService.DeleteDirectoryAsync(temporary); }
    }

    private async Task<string> ReserveBookFolderAsync(string bookName)
    {
        var stem = RequireStorageName(bookName, nameof(bookName));
        var project = CurrentProject ?? throw new InvalidOperationException("No project loaded.");
        var root = ProjectRoot ?? throw new InvalidOperationException("No project loaded.");
        var reserved = project.Books.Select(book => book.FolderName)
            .Append(project.WorldBibleFolder).Append(".novalist")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var suffix = 1; ; suffix++)
        {
            var folder = suffix == 1 ? stem : stem + "-" + suffix.ToString(CultureInfo.InvariantCulture);
            var destination = _fileService.CombinePath(root, folder);
            if (reserved.Contains(folder) || await _fileService.DirectoryExistsAsync(destination) ||
                await _fileService.ExistsAsync(destination)) continue;
            try { await ReserveDirectoryAsync(destination); }
            catch (IOException)
            {
                if (!await _fileService.DirectoryExistsAsync(destination) && !await _fileService.ExistsAsync(destination)) throw;
                continue;
            }
            return folder;
        }
    }
}
