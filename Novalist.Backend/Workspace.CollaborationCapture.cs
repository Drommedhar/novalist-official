#if NOVALIST_COLLABORATION
using System.Security.Cryptography;
using System.Text;
using Novalist.Core.Models;
using Novalist.Sync.Client;

namespace Novalist.Backend;

public sealed partial class Workspace
{
    internal async Task<SharedProjectState> CaptureSharedProjectAsync(Stream archive, CancellationToken ct)
    {
        RequireNoSharedSession();
        await ReportCollaborationProgressAsync(new("scanning", 0, 0, 0, 0));
        var root = Projects.ProjectRoot ?? throw new InvalidOperationException("Open a project first.");
        var factory = CreateSharedDocuments ?? throw new InvalidOperationException("Desktop collaboration is unavailable.");
        var paths = ProjectContentSelection.Read(root);
        await using var documents = factory(Guid.NewGuid());
        await documents.OpenAsync(ct);
        var states = new List<SharedDocumentState>();
        var sceneFolders = SharedSceneFolders();
        var pathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var scenePaths = paths.Where(path => path.EndsWith(".novalist", StringComparison.OrdinalIgnoreCase) &&
            sceneFolders.Any(folder => path.StartsWith(folder, pathComparison))).ToArray();
        foreach (var path in scenePaths)
        {
            var bytes = await File.ReadAllBytesAsync(SharedProjectPath.Resolve(root, path), ct);
            var content = new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF');
            if (!FileFrontMatter.TryParse(content, out var marker) || marker.Version != FileFrontMatter.CurrentVersion)
                throw new InvalidDataException("A scene needs a supported project identity before sharing.");
            var item = Guid.NewGuid();
            await documents.SeedAsync(item, FileFrontMatter.Strip(content), ct);
            var snapshot = await documents.CaptureAsync(item, ct);
            if (snapshot.Html is null || snapshot.Deletions.Count != 0)
                throw new InvalidDataException("An initial shared scene cannot be deleted.");
            states.Add(new(item, path, snapshot.State) { SourceSha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)) });
            await ReportCollaborationProgressAsync(new("scanning", states.Count, scenePaths.Length, 0, 0));
        }
        states.AddRange(await CaptureSharedProseAsync(root, paths, documents, ct));
        var state = new SharedProjectState(states, await SharedCodexFields.CaptureAsync(root, paths, ct));
        await ReportCollaborationProgressAsync(new("comparing", 0, paths.Count, 0, 0));
        var progress = new CollaborationProgress(this);
        await ProjectCheckpoint.CreateAsync(root, archive, () =>
        {
            var current = ProjectContentSelection.Read(root);
            if (!paths.SequenceEqual(current)) throw new IOException("The project changed during capture. Try sharing again.");
            return current;
        }, state, ct, progress);
        await progress.FlushAsync();
        return state;
    }

    internal Task ReportCollaborationProgressAsync(CollaborationTransferProgress progress) =>
        Coordinator?.Notify?.Invoke("collaboration/progress", progress) ?? Task.CompletedTask;

    internal async Task CaptureActiveSharedProjectAsync(Stream archive, CancellationToken ct)
    {
        var root = sharedRoot ?? throw new InvalidOperationException("The shared project is closed.");
        var scenes = RequireSharedScenes(sharedSession);
        if (sharedProjectFiles is { } files) await files.RefreshAsync(ct);
        if (sharedFields is { } fields) await fields.RefreshAsync(ct);
        await scenes.RefreshAsync(ct);
        var currentFiles = await ReadSharedFilesAsync(root, ct);
        var hashes = currentFiles.ToDictionary(pair => pair.Key, pair => Convert.ToHexStringLower(SHA256.HashData(pair.Value)), StringComparer.Ordinal);
        var states = new List<SharedDocumentState>();
        foreach (var (id, file) in sharedSceneFiles)
        {
            if (!hashes.TryGetValue(file.Relative, out var hash)) continue;
            var snapshot = await scenes.ReadAsync(id, ct);
            if (snapshot.Html is not null) states.Add(new(id, file.Relative, snapshot.State) { SourceSha256 = hash });
        }
        foreach (var binding in ActiveSharedProse)
        {
            if (!hashes.TryGetValue(binding.Path, out var hash)) continue;
            var snapshot = await scenes.ReadAsync(binding.ItemId, ct);
            states.Add(binding with { State = snapshot.State, SourceSha256 = hash });
        }
        var canonical = await (sharedProjectFiles ?? throw new InvalidOperationException("The shared project is closed.")).SnapshotAsync(ct);
        var state = new SharedProjectState(states, ActiveFieldBindings.Where(binding => hashes.ContainsKey(binding.Path))
            .Select(binding => binding with { SourceSha256 = hashes[binding.Path] }).ToArray())
        {
            FileBaselines = canonical.Where(pair => currentFiles.TryGetValue(pair.Key, out var content) && !pair.Value.AsSpan().SequenceEqual(content))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
        };
        await ProjectCheckpoint.CreateAsync(root, archive, () => ProjectContentSelection.Read(root), state, ct);
    }

    private IReadOnlyList<string> SharedSceneFolders()
    {
        var project = Projects.CurrentProject ?? throw new InvalidOperationException("Open a project first.");
        return project.Books.SelectMany(book => book.Drafts.Count == 0
            ? new[] { book.FolderName + '/' + book.ChapterFolder + '/' }
            : book.Drafts.Select(draft => book.FolderName + "/Drafts/" + draft.FolderName + '/' + book.ChapterFolder + '/')).ToArray();
    }
}
#endif
