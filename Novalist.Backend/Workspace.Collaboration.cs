#if NOVALIST_COLLABORATION
using Novalist.Sync.Client;
using Novalist.Core.Models;
using Novalist.Backend.Rpc;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.Text;

namespace Novalist.Backend;

public sealed partial class Workspace
{
    internal Func<Guid, DesktopSceneDocuments>? CreateSharedDocuments { get; set; }
    internal Func<Func<Task>, Task>? DispatchSharedUpdate { get; set; }
    internal HostedProjectWorkspace? HostedProjects { get; set; }
    internal PeerProjectWorkspace? PeerProjects { get; set; }
    private SharedSceneWorkspace? sharedScenes;
    private Guid sharedSession;
    private string? sharedRoot;
    private readonly Dictionary<string, Guid> sharedItems = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private readonly Dictionary<Guid, (string Relative, string SceneId)> sharedSceneFiles = [];
    private readonly Dictionary<Guid, string> sharedSceneProjected = [];
    private readonly HashSet<Guid> sharedSceneRecorded = [];
    private readonly Dictionary<Guid, byte[]> sharedSceneBaseline = [];

    internal async Task AttachSharedScenesAsync(Guid sessionId, CollaborationSession session,
        IReadOnlyList<SharedDocumentState> initial, CancellationToken ct)
    {
        if (sharedScenes is not null) throw new InvalidOperationException("Close the current collaboration session first.");
        var root = Projects.ProjectRoot ?? throw new InvalidOperationException("Open a project first.");
        var factory = CreateSharedDocuments ?? throw new InvalidOperationException("Desktop collaboration is unavailable.");
        if (sessionId == Guid.Empty) throw new ArgumentException("A session identity is required.", nameof(sessionId));
        var files = sharedSceneFiles;
        var projected = sharedSceneProjected;
        var recorded = sharedSceneRecorded;
        foreach (var item in initial)
        {
            if (item.TextField is not null) { sharedProse.Add(item.ItemId, item); continue; }
            var path = SharedProjectPath.Resolve(root, item.Path);
            var bytes = await FileService.ReadBytesAsync(path);
            if (item.SourceSha256 is { } expected && !string.Equals(expected,
                Convert.ToHexStringLower(SHA256.HashData(bytes)), StringComparison.OrdinalIgnoreCase))
                throw new IOException("A scene changed while sharing was starting. Your local writing has been kept; try sharing again.");
            var content = new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF');
            if (!FileFrontMatter.TryParse(content, out var marker)) throw new InvalidDataException("A shared scene is missing its project identity.");
            files.Add(item.ItemId, (item.Path, marker.Id));
            projected.Add(item.ItemId, FileFrontMatter.Strip(content));
            sharedSceneBaseline.Add(item.ItemId, item.State);
        }
        if (files.Values.Select(file => file.Relative).Distinct(sharedItems.Comparer).Count() != files.Count)
            throw new InvalidDataException("Shared scenes must have distinct project paths.");
        var documents = new SharedSceneWorkspace(session, factory(sessionId), async (item, snapshot, local, _) =>
        {
            using var projection = BeginSharedProjection();
            if (Projects.ProjectRoot != root) throw new InvalidOperationException("The shared project is no longer open.");
            if (sharedProse.TryGetValue(item, out var prose)) { await ProjectSharedProseAsync(root, prose, snapshot); return; }
            await PrepareSharedProjectionPathAsync(files[item].Relative);
            if (snapshot.Html is null)
            {
                await FileService.DeleteFileAsync(SharedProjectPath.Resolve(root, files[item].Relative));
                if (snapshot.Recovered is { } recovered)
                    await WriteSharedFileRecoveryAsync(root, item.ToString("N"), files[item].Relative,
                        Encoding.UTF8.GetBytes(FileFrontMatter.Stamp(recovered, files[item].SceneId)));
                return;
            }
            var changed = projected[item] != snapshot.Html;
            if (!changed && recorded.Contains(item)) return;
            var file = files[item];
            if (changed) await FileService.WriteTextAsync(SharedProjectPath.Resolve(root, file.Relative),
                FileFrontMatter.Stamp(FileFrontMatter.Strip(snapshot.Html), file.SceneId));
            await RecordSharedSceneAsync(root, file.Relative, snapshot.Html, local);
            projected[item] = snapshot.Html;
            recorded.Add(item);
        });
        if (DispatchSharedUpdate is { } dispatch) documents.DispatchRemote = dispatch;
        try { await documents.StartAsync(initial, ct); }
        catch { await documents.DisposeAsync(); throw; }
        documents.Notify = (itemId, update) => Coordinator?.Notify?.Invoke("collaboration/sceneUpdated", new { sessionId, itemId, update }) ?? Task.CompletedTask;
        sharedScenes = documents;
        sharedSession = sessionId;
        sharedRoot = root;
        foreach (var item in initial.Where(item => item.TextField is null)) sharedItems.Add(item.Path, item.ItemId);
    }

    internal async Task<SceneContentDto?> ReadSharedSceneAsync(string chapterGuid, string sceneId, CancellationToken ct)
    {
        var item = SharedItem(chapterGuid, sceneId);
        if (item is null || sharedScenes is null) return null;
        var snapshot = await sharedScenes.ReadAsync(item.Value, ct);
        var html = snapshot.Html ?? throw new InvalidOperationException("The shared scene was deleted.");
        return new(sceneId, html, Core.Services.ContentHasher.Hash(html), new(sharedSession, item.Value, snapshot.State, HostedProjects?.IsActive == true || PeerProjects?.IsActive == true));
    }

    internal SharedSceneWorkspace RequireSharedScenes(Guid sessionId)
    {
        if (sharedScenes is null || sessionId != sharedSession || Projects.ProjectRoot != sharedRoot)
            throw new InvalidOperationException("This collaboration session is no longer active.");
        return sharedScenes;
    }

    internal void RequireUnsharedScene(string chapterGuid, string sceneId)
    {
        if (SharedItem(chapterGuid, sceneId) is not null)
            throw new InvalidOperationException("A shared scene must be saved as collaborative edits.");
    }

    private Guid? SharedItem(string chapterGuid, string sceneId)
    {
        if (sharedScenes is null || sharedRoot is not { } root || Projects.ProjectRoot != root) return null;
        var (chapter, scene) = ResolveScene(chapterGuid, sceneId);
        var relative = Path.GetRelativePath(root, Projects.GetSceneFilePath(chapter, scene)).Replace('\\', '/');
        return sharedItems.TryGetValue(relative, out var item) ? item : null;
    }

    internal async Task DetachSharedScenesAsync()
    {
        await DetachSharedProjectFilesAsync();
        await DetachSharedFieldsAsync();
        if (sharedScenes is not null) await sharedScenes.CloseAsync(CancellationToken.None);
        sharedScenes = null;
        sharedItems.Clear();
        sharedSceneFiles.Clear();
        sharedSceneProjected.Clear();
        sharedSceneRecorded.Clear();
        sharedSceneBaseline.Clear();
        sharedProse.Clear();
        sharedRoot = null;
        sharedSession = Guid.Empty;
        if (HostedProjects is { } hosted) await hosted.ReleaseAsync();
        if (PeerProjects is { } peers) await peers.ReleaseAsync();
    }

    internal void RequireNoSharedSession()
    {
        if (sharedScenes is not null || sharedFields is not null || HostedProjects?.IsActive == true || PeerProjects?.IsActive == true) throw new InvalidOperationException("Close the collaboration session asynchronously before closing this workspace.");
    }

    private async Task RecordSharedSceneAsync(string root, string relative, string html, bool local)
    {
        if (Projects.ProjectRoot != root || Projects.ActiveBook is not { } book || Projects.ScenesManifest is not { } manifest) return;
        foreach (var chapter in book.Chapters)
        {
            foreach (var scene in manifest.Chapters.GetValueOrDefault(chapter.Guid) ?? [])
            {
                var path = Path.GetRelativePath(root, Projects.GetSceneFilePath(chapter, scene)).Replace('\\', '/');
                if (!sharedItems.Comparer.Equals(relative, path)) continue;
                var plainText = Core.Utilities.TextDiff.StripHtml(SharedBlockBoundary().Replace(html, "\n"));
                await AfterSceneWriteAsync(chapter, scene, html, plainText, recordHistory: local);
                return;
            }
        }
    }

    [GeneratedRegex(@"</?(?:p|div|h[1-6]|ul|ol|li|dl|dt|dd|blockquote|pre|table|thead|tbody|tfoot|tr|td|th|figure|figcaption|br|hr)\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SharedBlockBoundary();
}
#endif
