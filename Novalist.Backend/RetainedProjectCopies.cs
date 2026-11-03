#if NOVALIST_COLLABORATION
using System.Text.Json;
using System.Text.Json.Nodes;
using Novalist.Core.Services;
using Novalist.Sync.Client;

namespace Novalist.Backend;

internal static class RetainedProjectCopies
{
    internal static async Task CreateIndependentAsync(string root, string destination, CancellationToken ct)
    {
        destination = Path.GetFullPath(destination);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (destination.StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar, comparison))
            throw new IOException("Choose a folder outside the original project.");
        if (Directory.Exists(destination) || File.Exists(destination)) throw new IOException("Choose a new folder for the independent project.");
        var parent = Path.GetDirectoryName(destination) ?? throw new IOException("Choose a project folder.");
        Directory.CreateDirectory(parent);
        var staging = Path.Combine(parent, ".novalist-independent-" + Guid.NewGuid().ToString("N"));
        var archivePath = Path.Combine(parent, ".novalist-copy-" + Guid.NewGuid().ToString("N") + ".zip");
        try
        {
            await using (var archive = new FileStream(archivePath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            {
                await ProjectCheckpoint.CreateAsync(root, archive, () => ProjectContentSelection.Read(root), ct);
                archive.Position = 0;
                await ProjectCheckpoint.InstallAsync(archive, staging, FreeSpace(destination), ct);
            }
            var path = SharedProjectPath.Resolve(staging, ".novalist/project.json");
            var metadata = JsonNode.Parse(await File.ReadAllTextAsync(path, ct)) as JsonObject
                ?? throw new InvalidDataException("Invalid project metadata.");
            metadata["id"] = Guid.NewGuid().ToString("D");
            metadata["createdAt"] = DateTime.UtcNow;
            ct.ThrowIfCancellationRequested();
            await new FileService().WriteTextAsync(path, metadata.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            ct.ThrowIfCancellationRequested();
            Directory.Move(staging, destination);
        }
        finally
        {
            if (File.Exists(archivePath)) File.Delete(archivePath);
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }
    }

    internal static long FreeSpace(string path) => new DriveInfo(Path.GetPathRoot(Path.GetFullPath(path))
        ?? throw new IOException("Choose a local project folder.")).AvailableFreeSpace;
}
#endif
