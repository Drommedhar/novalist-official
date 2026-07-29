using Novalist.Backend.Rpc;
using Novalist.Core.Utilities;
using Xunit;

namespace Novalist.Backend.Tests;

/// <summary>
/// Suggested edits over the RPC surface, and what answering one does to the
/// scene it was in.
/// </summary>
public sealed class SuggestionsRpcTests : IDisposable
{
    private readonly string _root;
    private readonly Workspace _workspace;
    private readonly SuggestionsRpc _rpc;
    private string _chapter = string.Empty;
    private string _scene = string.Empty;

    public SuggestionsRpcTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "nl-suggest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _workspace = new Workspace(Path.Combine(_root, "settings"));
        _workspace.Projects.CreateProjectAsync(_root, "SuggestNovel", "Book").GetAwaiter().GetResult();
        _workspace.OpenProjectAsync(_workspace.Projects.ProjectRoot!).GetAwaiter().GetResult();
        _rpc = new SuggestionsRpc(_workspace);
    }

    public void Dispose()
    {
        _workspace.Dispose();
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    /// <summary>A scene reading "The bell rang once" with "twice" suggested over it.</summary>
    private async Task WriteSuggestedSceneAsync(string title = "Arrival")
    {
        var chapter = _workspace.Projects.GetChaptersOrdered().FirstOrDefault()
            ?? await _workspace.Projects.CreateChapterAsync("One");
        var scene = await _workspace.Projects.CreateSceneAsync(chapter.Guid, title);
        _chapter = chapter.Guid;
        _scene = scene.Id;

        var html = "<p>The bell rang " +
            TrackedChanges.Deletion("d1", "once", "Mira", "2026-03-14") +
            TrackedChanges.Insertion("i1", "twice", "Mira", "2026-03-14") +
            ".</p>";
        await _workspace.WriteSceneAsync(_chapter, _scene, html, TextDiff.StripHtml(html));
    }

    [Fact]
    public async Task ASceneWithNoSuggestionsHasNone()
    {
        var chapter = await _workspace.Projects.CreateChapterAsync("One");
        var scene = await _workspace.Projects.CreateSceneAsync(chapter.Guid, "Plain");
        await _workspace.WriteSceneAsync(chapter.Guid, scene.Id, "<p>Plain prose.</p>", "Plain prose.");

        Assert.Empty(await _rpc.ForSceneAsync(chapter.Guid, scene.Id));
    }

    [Fact]
    public async Task BothSidesOfAReplacementAreListed()
    {
        await WriteSuggestedSceneAsync();

        var pending = await _rpc.ForSceneAsync(_chapter, _scene);

        Assert.Equal(2, pending.Length);
        Assert.Equal("deletion", pending[0].Kind);
        Assert.Equal("once", pending[0].Text);
        Assert.Equal("Mira", pending[0].Author);
        Assert.Equal("insertion", pending[1].Kind);
        Assert.Equal("twice", pending[1].Text);
    }

    [Fact]
    public async Task TakingOneEditLeavesTheOtherPending()
    {
        await WriteSuggestedSceneAsync();

        var remaining = await _rpc.AcceptAsync(_chapter, _scene, "i1");

        Assert.Equal("deletion", Assert.Single(remaining).Kind);
    }

    [Fact]
    public async Task AnsweringAnEditRewritesTheSceneOnDisk()
    {
        await WriteSuggestedSceneAsync();

        await _rpc.AcceptAllAsync(_chapter, _scene);

        var (chapter, scene) = _workspace.ResolveScene(_chapter, _scene);
        var html = await _workspace.Projects.ReadSceneContentAsync(chapter, scene);
        Assert.Contains("twice", html);
        Assert.DoesNotContain("once", html);
        // The markup itself is gone, not just resolved in a view of it.
        Assert.DoesNotContain("<ins", html);
        Assert.DoesNotContain("<del", html);
    }

    [Fact]
    public async Task TurningEveryEditDownPutsTheProseBack()
    {
        await WriteSuggestedSceneAsync();

        await _rpc.RejectAllAsync(_chapter, _scene);

        var (chapter, scene) = _workspace.ResolveScene(_chapter, _scene);
        var html = await _workspace.Projects.ReadSceneContentAsync(chapter, scene);
        Assert.Contains("once", html);
        Assert.DoesNotContain("twice", html);
    }

    [Fact]
    public async Task TurningOneEditDownLeavesTheOther()
    {
        await WriteSuggestedSceneAsync();

        var remaining = await _rpc.RejectAsync(_chapter, _scene, "d1");

        Assert.Equal("insertion", Assert.Single(remaining).Kind);
    }

    [Fact]
    public async Task AnsweringAnEditUpdatesTheWordCount()
    {
        // Turning down a suggested sentence makes the scene shorter. A stale
        // count would show on the dashboard before it showed anywhere else.
        var chapter = await _workspace.Projects.CreateChapterAsync("One");
        var scene = await _workspace.Projects.CreateSceneAsync(chapter.Guid, "Longer");
        var html = "<p>The bell rang." +
            TrackedChanges.Insertion("i9", " Nobody came to see why.", "Mira", "2026-03-14") +
            "</p>";
        await _workspace.WriteSceneAsync(chapter.Guid, scene.Id, html, TextDiff.StripHtml(html));

        // A pending insertion counts, the way a word processor counts one.
        var (_, before) = _workspace.ResolveScene(chapter.Guid, scene.Id);
        Assert.Equal(8, before.WordCount);

        await _rpc.RejectAllAsync(chapter.Guid, scene.Id);

        var (_, after) = _workspace.ResolveScene(chapter.Guid, scene.Id);
        Assert.Equal(3, after.WordCount);
    }

    [Fact]
    public async Task TheInboxFindsEverySceneWithSomethingWaiting()
    {
        await WriteSuggestedSceneAsync("First");
        var firstScene = _scene;
        await WriteSuggestedSceneAsync("Second");

        var inbox = await _rpc.InboxAsync();

        Assert.Equal(2, inbox.Length);
        Assert.All(inbox, row => Assert.Equal(2, row.Count));
        Assert.Contains(inbox, row => row.SceneId == firstScene);
        Assert.Contains(inbox, row => row.SceneTitle == "Second");
    }

    [Fact]
    public async Task TheInboxLeavesOutScenesWithNothingWaiting()
    {
        var chapter = await _workspace.Projects.CreateChapterAsync("One");
        await _workspace.Projects.CreateSceneAsync(chapter.Guid, "Plain");

        Assert.Empty(await _rpc.InboxAsync());
    }

    [Fact]
    public async Task AnsweringEverythingEmptiesTheInbox()
    {
        await WriteSuggestedSceneAsync();

        await _rpc.AcceptAllAsync(_chapter, _scene);

        Assert.Empty(await _rpc.InboxAsync());
    }
}
