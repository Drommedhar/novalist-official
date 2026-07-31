using Novalist.Backend;
using Novalist.Backend.Rpc;
using Novalist.Core.Models;
using Xunit;

namespace Novalist.Backend.Tests;

/// <summary>
/// Every open note in the book. Comments had no aggregate query at all, so one
/// was only findable by reopening the scene it was left in.
/// </summary>
public sealed class InboxRpcTests : IDisposable
{
    private readonly string _root;
    private readonly Workspace _workspace;
    private readonly InboxRpc _rpc;
    private readonly string _chapter;
    private readonly string _sceneA;
    private readonly string _sceneB;

    public InboxRpcTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "nl-inbox-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _workspace = new Workspace(Path.Combine(_root, "settings"));
        _workspace.Projects.CreateProjectAsync(_root, "InboxNovel", "Book").GetAwaiter().GetResult();
        _workspace.OpenProjectAsync(_workspace.Projects.ProjectRoot!).GetAwaiter().GetResult();
        _chapter = _workspace.Projects.CreateChapterAsync("One").GetAwaiter().GetResult().Guid;
        _sceneA = _workspace.Projects.CreateSceneAsync(_chapter, "First").GetAwaiter().GetResult().Id;
        _sceneB = _workspace.Projects.CreateSceneAsync(_chapter, "Second").GetAwaiter().GetResult().Id;
        _rpc = new InboxRpc(_workspace);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private SceneComment AddComment(string sceneId, string text, bool resolved = false)
    {
        var scene = _workspace.Projects.GetScenesForChapter(_chapter).First(s => s.Id == sceneId);
        var comment = new SceneComment
        {
            AnchorText = "the bells",
            Text = text,
            Resolved = resolved
        };
        (scene.Comments ??= []).Add(comment);
        _workspace.Projects.SaveScenesAsync().GetAwaiter().GetResult();
        return comment;
    }

    [Fact]
    public void List_GathersOpenNotesAcrossTheBookInReadingOrder()
    {
        AddComment(_sceneB, "second scene note");
        AddComment(_sceneA, "first scene note");
        AddComment(_sceneA, "already handled", resolved: true);

        var items = _rpc.List();

        // Reading order, not the order they were written in.
        Assert.Equal(["first scene note", "second scene note"], items.Select(i => i.Text));
        Assert.Equal("First", items[0].SceneTitle);
        Assert.Equal("the bells", items[0].AnchorText);
    }

    [Fact]
    public void List_CanIncludeWhatIsAlreadyDone()
    {
        AddComment(_sceneA, "open");
        AddComment(_sceneA, "handled", resolved: true);

        Assert.Single(_rpc.List());
        Assert.Equal(2, _rpc.List(includeResolved: true).Length);
    }

    [Fact]
    public async Task Resolve_AndReopen()
    {
        var comment = AddComment(_sceneA, "check the timetable");

        Assert.Empty(await _rpc.SetResolvedAsync(_sceneA, comment.Id, true));
        Assert.Single(await _rpc.SetResolvedAsync(_sceneA, comment.Id, false));
    }

    [Fact]
    public async Task ATodoIsFlaggedRatherThanBeingASeparateKindOfThing()
    {
        var comment = AddComment(_sceneA, "cut this paragraph");

        var items = await _rpc.SetTodoAsync(_sceneA, comment.Id, true);
        Assert.True(Assert.Single(items).IsTodo);

        Assert.False(Assert.Single(await _rpc.SetTodoAsync(_sceneA, comment.Id, false)).IsTodo);
    }

    [Fact]
    public async Task DecidingAgainstANoteIsNotTheSameAsDoingIt()
    {
        var comment = AddComment(_sceneA, "the middle drags");

        // Both finish with the note - the inbox has to empty either way, so the
        // decided one drops out of the open list exactly as resolving it does.
        Assert.Empty(await _rpc.SetVerdictAsync(_sceneA, comment.Id, "declined"));

        // The reason is kept, because a remark turned down is the one worth
        // recognising when a second reader says the same thing.
        var declined = Assert.Single(_rpc.List(includeResolved: true));
        Assert.Equal("declined", declined.Verdict);
        Assert.True(declined.Resolved);

        await _rpc.SetVerdictAsync(_sceneA, comment.Id, "accepted");
        var accepted = Assert.Single(_rpc.List(includeResolved: true));
        Assert.Equal("accepted", accepted.Verdict);
        Assert.True(accepted.Resolved);
    }

    [Fact]
    public async Task WeighingANoteLeavesItOpen()
    {
        var comment = AddComment(_sceneA, "is the sister necessary?");

        // The whole point of the value: undecided is a state to sit in, not a
        // way of closing the note quietly.
        var items = await _rpc.SetVerdictAsync(_sceneA, comment.Id, "considering");
        var only = Assert.Single(items);
        Assert.Equal("considering", only.Verdict);
        Assert.False(only.Resolved);
    }

    [Fact]
    public async Task ClearingAVerdictLeavesTheNoteAsItWas()
    {
        var comment = AddComment(_sceneA, "check the timetable");
        await _rpc.SetVerdictAsync(_sceneA, comment.Id, "considering");

        var cleared = Assert.Single(await _rpc.SetVerdictAsync(_sceneA, comment.Id, ""));
        Assert.Equal(string.Empty, cleared.Verdict);
        Assert.False(cleared.Resolved);
    }

    [Fact]
    public async Task AVerdictNothingCanFilterOnIsNotStored()
    {
        var comment = AddComment(_sceneA, "check the timetable");

        // A free-text verdict would make the filter meaningless and would not
        // survive a translation of the interface.
        var items = await _rpc.SetVerdictAsync(_sceneA, comment.Id, "maybe later");
        Assert.Equal(string.Empty, Assert.Single(items).Verdict);
        Assert.Equal(string.Empty, Assert.Single(await _rpc.SetVerdictAsync(_sceneA, comment.Id, null)).Verdict);
    }

    [Fact]
    public async Task AVerdictSurvivesBeingWrittenAndReadBack()
    {
        var comment = AddComment(_sceneA, "the middle drags");
        await _rpc.SetVerdictAsync(_sceneA, comment.Id, "declined");

        // Round-tripped through the project files rather than read out of the
        // object still in memory, which is where a missing JSON name hides.
        await _workspace.Projects.SaveScenesAsync();
        await _workspace.OpenProjectAsync(_workspace.Projects.ProjectRoot!);
        var reloaded = new InboxRpc(_workspace).List(includeResolved: true);

        Assert.Equal("declined", Assert.Single(reloaded, i => i.CommentId == comment.Id).Verdict);
    }

    [Fact]
    public async Task ReplyingAppendsToTheThreadAndCarriesTheAuthor()
    {
        _workspace.Projects.ProjectSettings.Author = "Jane Doe";
        var comment = AddComment(_sceneA, "is this the same bell?");

        await _rpc.ReplyAsync(_sceneA, comment.Id, "  Yes - chapter four.  ");

        var reply = Assert.Single(Assert.Single(_rpc.List()).Replies);
        Assert.Equal("Yes - chapter four.", reply.Text);
        Assert.Equal("Jane Doe", reply.Author);
    }

    [Fact]
    public async Task AnEmptyReplyIsRefused()
    {
        var comment = AddComment(_sceneA, "note");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _rpc.ReplyAsync(_sceneA, comment.Id, "   "));
    }

    [Fact]
    public async Task AnUnknownCommentThrowsRatherThanSilentlyDoingNothing()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _rpc.SetResolvedAsync(_sceneA, "no-such-comment", true));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _rpc.SetTodoAsync("no-such-scene", "x", true));
    }

    [Fact]
    public async Task EditingACommentInThePanelKeepsItsThread()
    {
        _workspace.Projects.ProjectSettings.Author = "Jane Doe";
        var comment = AddComment(_sceneA, "original");
        await _rpc.ReplyAsync(_sceneA, comment.Id, "an answer");
        await _rpc.SetTodoAsync(_sceneA, comment.Id, true);

        // The annotations panel only knows the text and the resolved flag;
        // rebuilding from that alone used to discard everything else.
        await new ScenesRpc(_workspace).SetAnnotationsAsync(_chapter, _sceneA,
            [new SceneCommentDto(comment.Id, "the bells", "edited", false)], []);

        var item = Assert.Single(_rpc.List());
        Assert.Equal("edited", item.Text);
        Assert.True(item.IsTodo);
        Assert.Equal("Jane Doe", item.Author);
        Assert.Single(item.Replies);
    }
}
