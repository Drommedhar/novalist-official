using Novalist.Backend;
using Novalist.Backend.Rpc;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Xunit;

namespace Novalist.Backend.Tests;

/// <summary>
/// Codex names used as plain text, from the RPC surface the Codex panel calls.
/// </summary>
public sealed class UnlinkedMentionRpcTests : IDisposable
{
    private readonly string _root;
    private readonly Workspace _workspace;
    private readonly UnlinkedMentionRpc _rpc;

    public UnlinkedMentionRpcTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "nl-unlinked-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _workspace = new Workspace(Path.Combine(_root, "settings"));
        _workspace.Projects.CreateProjectAsync(_root, "MentionNovel", "Book")
            .GetAwaiter().GetResult();
        _workspace.OpenProjectAsync(_workspace.Projects.ProjectRoot!).GetAwaiter().GetResult();
        _rpc = new UnlinkedMentionRpc(_workspace);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    [Fact]
    public async Task FindThenLink_TheOccurrenceStopsBeingUnlinked()
    {
        var mira = new CharacterData { Name = "Mira" };
        await new EntityService(_workspace.Projects).SaveCharacterAsync(mira);
        var chapter = await _workspace.Projects.CreateChapterAsync("One");
        var scene = await _workspace.Projects.CreateSceneAsync(chapter.Guid, "Opening");
        await _workspace.WriteSceneAsync(chapter.Guid, scene.Id,
            "<p>Mira crossed the yard.</p>", "Mira crossed the yard.");

        var found = Assert.Single(await _rpc.FindAsync());
        Assert.Equal("Mira", found.EntityName);
        Assert.Equal(scene.Id, found.SceneId);
        Assert.Equal(1, found.Count);

        Assert.Empty(await _rpc.LinkAsync(chapter.Guid, scene.Id, mira.Id));
    }

    [Fact]
    public async Task ABookWithNoCodexEntriesFindsNothing()
    {
        var chapter = await _workspace.Projects.CreateChapterAsync("One");
        var scene = await _workspace.Projects.CreateSceneAsync(chapter.Guid, "Opening");
        await _workspace.WriteSceneAsync(chapter.Guid, scene.Id, "<p>Nobody.</p>", "Nobody.");

        Assert.Empty(await _rpc.FindAsync());
    }
}
