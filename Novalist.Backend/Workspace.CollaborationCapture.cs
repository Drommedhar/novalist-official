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
        var root = Projects.ProjectRoot ?? throw new InvalidOperationException("Open a project first.");
        var factory = CreateSharedDocuments ?? throw new InvalidOperationException("Desktop collaboration is unavailable.");
        await Projects.SaveScenesAsync();
        await Projects.SaveProjectAsync();
        var paths = ProjectContentSelection.Read(root);
        await using var documents = factory(Guid.NewGuid());
        await documents.OpenAsync(ct);
        var states = new List<SharedDocumentState>();
        var sceneFolders = SharedSceneFolders();
        var pathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        foreach (var path in paths.Where(path => path.EndsWith(".novalist", StringComparison.OrdinalIgnoreCase) &&
            sceneFolders.Any(folder => path.StartsWith(folder, pathComparison))))
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
        }
        var state = new SharedProjectState(states, await SharedCodexFields.CaptureAsync(root, paths, ct));
        await ProjectCheckpoint.CreateAsync(root, archive, () =>
        {
            var current = ProjectContentSelection.Read(root);
            if (!paths.SequenceEqual(current)) throw new IOException("The project changed during capture. Try sharing again.");
            return current;
        }, state, ct);
        return state;
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
