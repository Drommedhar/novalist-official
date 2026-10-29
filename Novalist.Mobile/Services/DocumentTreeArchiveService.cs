using Novalist.Core.Services;

namespace Novalist.Mobile.Services;

public sealed class DocumentTreeArchiveService : ICancellableArchiveService
{
    private readonly ArchiveService _local = new();
    private readonly IFileService files;
    private readonly Func<string, bool> isExternalPath;
    private readonly string cacheDirectory;

    public DocumentTreeArchiveService(IFileService files, Func<string, bool> isExternalPath, string cacheDirectory)
    {
        this.files = files;
        this.isExternalPath = isExternalPath;
        this.cacheDirectory = cacheDirectory;
    }

    public Task<int> CreateFromDirectoryAsync(string sourceDirectory, string destinationZipPath,
        IReadOnlyCollection<string> excludedDirectoryNames)
        => CreateFromDirectoryAsync(sourceDirectory, destinationZipPath, excludedDirectoryNames, CancellationToken.None);

    public async Task<int> CreateFromDirectoryAsync(string sourceDirectory, string destinationZipPath,
        IReadOnlyCollection<string> excludedDirectoryNames, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!isExternalPath(sourceDirectory) && !isExternalPath(destinationZipPath))
            return await _local.CreateFromDirectoryAsync(sourceDirectory, destinationZipPath, excludedDirectoryNames);
        using (DocumentTreeReadCancellation.Enter(cancellationToken))
        {
            if (await files.ExistsAsync(destinationZipPath) || await files.DirectoryExistsAsync(destinationZipPath))
                throw new IOException("The backup destination already exists.");
        }

        var staging = NewStagingDirectory();
        try
        {
            var source = sourceDirectory;
            if (isExternalPath(sourceDirectory))
            {
                source = Path.Combine(staging, "project");
                await CopyAsync(files, sourceDirectory, new FileService(), source, excludedDirectoryNames, cancellationToken);
            }
            var archive = Path.Combine(staging, "backup.zip");
            cancellationToken.ThrowIfCancellationRequested();
            var count = await _local.CreateFromDirectoryAsync(source, archive, excludedDirectoryNames);
            cancellationToken.ThrowIfCancellationRequested();
            await PublishArchiveAsync(archive, destinationZipPath);
            return count;
        }
        finally { Directory.Delete(staging, true); }
    }

    public async Task<int> ExtractToDirectoryAsync(string zipPath, string destinationDirectory)
    {
        if (!isExternalPath(zipPath) && !isExternalPath(destinationDirectory))
            return await _local.ExtractToDirectoryAsync(zipPath, destinationDirectory);

        var staging = NewStagingDirectory();
        try
        {
            var archive = await StageArchiveAsync(zipPath, staging);
            var extracted = Path.Combine(staging, "extracted");
            var count = await _local.ExtractToDirectoryAsync(archive, extracted);
            await CopyAsync(new FileService(), extracted, files, destinationDirectory, []);
            return count;
        }
        finally { Directory.Delete(staging, true); }
    }

    public async Task RestoreProjectAsync(string zipPath, string destinationDirectory, bool replaceExisting)
    {
        if (!isExternalPath(zipPath) && !isExternalPath(destinationDirectory))
        {
            await _local.RestoreProjectAsync(zipPath, destinationDirectory, replaceExisting);
            return;
        }

        if (!replaceExisting && (await files.DirectoryExistsAsync(destinationDirectory) || await files.ExistsAsync(destinationDirectory)))
            throw new IOException("Choose a new project folder. The destination already exists.");

        var staging = NewStagingDirectory();
        try
        {
            var archive = await StageArchiveAsync(zipPath, staging);
            if (!isExternalPath(destinationDirectory))
            {
                await _local.RestoreProjectAsync(archive, destinationDirectory, replaceExisting);
                return;
            }
            var validated = Path.Combine(staging, "validated");
            await _local.RestoreProjectAsync(archive, validated, false);
            if (replaceExisting)
            {
                await CopyAsync(new FileService(), validated, files, destinationDirectory, [".git"]);
                await RemoveObsoleteAsync(destinationDirectory, validated);
            }
            else await PublishNewAsync(validated, destinationDirectory);
        }
        finally { Directory.Delete(staging, true); }
    }

    private async Task PublishNewAsync(string source, string destination)
    {
        var temporary = Path.Combine(Path.GetDirectoryName(destination)
            ?? throw new IOException("A project needs a parent folder."), ".novalist-restore-" + Guid.NewGuid().ToString("N"));
        try
        {
            await CopyAsync(new FileService(), source, files, temporary, []);
            await files.MoveDirectoryAsync(temporary, destination);
        }
        finally { await files.DeleteDirectoryAsync(temporary); }
    }

    private async Task PublishArchiveAsync(string source, string destination)
    {
        if (!isExternalPath(destination))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination) ?? throw new IOException("The backup needs a parent folder."));
            File.Move(source, destination);
            return;
        }
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await files.WriteBytesAsync(temporary, await File.ReadAllBytesAsync(source));
            await files.MoveFileAsync(temporary, destination);
        }
        finally { await files.DeleteFileAsync(temporary); }
    }

    private async Task RemoveObsoleteAsync(string destination, string restored)
    {
        foreach (var file in await files.GetFilesAsync(destination))
        {
            if (Path.GetDirectoryName(file) != destination) continue;
            if (!File.Exists(Path.Combine(restored, Path.GetFileName(file)))) await files.DeleteFileAsync(file);
        }
        foreach (var directory in await files.GetDirectoriesAsync(destination))
        {
            if (Path.GetFileName(directory).Equals(".git", StringComparison.OrdinalIgnoreCase)) continue;
            await RemoveObsoleteAsync(directory, Path.Combine(restored, Path.GetFileName(directory)));
            if ((await files.GetFilesAsync(directory)).Count == 0 && (await files.GetDirectoriesAsync(directory)).Count == 0)
                await files.DeleteDirectoryAsync(directory, false);
        }
    }

    private async Task<string> StageArchiveAsync(string path, string staging)
    {
        var archive = Path.Combine(staging, "input.zip");
        await File.WriteAllBytesAsync(archive, await files.ReadBytesAsync(path));
        return archive;
    }

    private string NewStagingDirectory()
    {
        var path = Path.Combine(cacheDirectory, "archive-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static async Task CopyAsync(IFileService sourceFiles, string source, IFileService targetFiles,
        string destination, IReadOnlyCollection<string> excluded, CancellationToken cancellationToken = default)
    {
        using var reads = DocumentTreeReadCancellation.Enter(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (!await sourceFiles.DirectoryExistsAsync(source)) throw new DirectoryNotFoundException("The source folder is unavailable.");
        await targetFiles.CreateDirectoryAsync(destination);
        foreach (var file in await sourceFiles.GetFilesAsync(source))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Path.GetDirectoryName(file) != source) continue;
            var bytes = await sourceFiles.ReadBytesAsync(file);
            cancellationToken.ThrowIfCancellationRequested();
            await targetFiles.WriteBytesAsync(Path.Combine(destination, Path.GetFileName(file)), bytes);
        }
        foreach (var directory in await sourceFiles.GetDirectoriesAsync(source))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = Path.GetFileName(directory);
            if (excluded.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
            await CopyAsync(sourceFiles, directory, targetFiles, Path.Combine(destination, name), excluded, cancellationToken);
        }
    }
}
