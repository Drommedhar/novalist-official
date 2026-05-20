using NSubstitute;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Xunit;

namespace Novalist.Core.Tests.Services;

public class FindReplaceServiceTests
{
    private static (FindReplaceService Sut, IProjectService Project) Build()
    {
        var project = Substitute.For<IProjectService>();
        return (new FindReplaceService(project), project);
    }

    private static ChapterData Chapter(string guid, string title = "Ch") => new() { Guid = guid, Title = title };
    private static SceneData Scene(string id, string title = "Sc") => new() { Id = id, Title = title };

    [Fact]
    public async Task FindAsync_EmptyPattern_ReturnsEmpty()
    {
        var (sut, _) = Build();
        var result = await sut.FindAsync(new FindOptions { Pattern = "" });
        Assert.Empty(result);
    }

    [Fact]
    public async Task FindAsync_ReturnsMatchesWithSnippets()
    {
        var (sut, project) = Build();
        var ch = Chapter("c1");
        var sc = Scene("s1");
        project.GetChaptersOrdered().Returns(new List<ChapterData> { ch });
        project.GetScenesForChapter("c1").Returns(new List<SceneData> { sc });
        project.ReadSceneContentAsync(ch, sc)
            .Returns("<p>The quick brown fox jumps over the lazy brown dog brown.</p>");

        var matches = await sut.FindAsync(new FindOptions { Pattern = "brown", Scope = FindScope.ActiveBook });

        Assert.Equal(3, matches.Count);
        Assert.All(matches, m => Assert.Equal("brown", m.MatchedText));
        Assert.Equal("c1", matches[0].ChapterGuid);
        Assert.Equal("s1", matches[0].SceneId);
        // First match has text before it; later matches have text after.
        Assert.NotEqual(string.Empty, matches[0].Before);
        Assert.NotEqual(string.Empty, matches[0].After);
    }

    [Fact]
    public async Task FindAsync_MatchAtStart_HasEmptyBefore()
    {
        var (sut, project) = Build();
        var ch = Chapter("c1");
        var sc = Scene("s1");
        project.GetChaptersOrdered().Returns(new List<ChapterData> { ch });
        project.GetScenesForChapter("c1").Returns(new List<SceneData> { sc });
        project.ReadSceneContentAsync(ch, sc).Returns("brown");

        var matches = await sut.FindAsync(new FindOptions { Pattern = "brown" });
        Assert.Equal(string.Empty, matches[0].Before);
        Assert.Equal(string.Empty, matches[0].After);
    }

    [Fact]
    public async Task FindAsync_WholeWord_DoesNotMatchSubstring()
    {
        var (sut, project) = Build();
        var ch = Chapter("c1");
        var sc = Scene("s1");
        project.GetChaptersOrdered().Returns(new List<ChapterData> { ch });
        project.GetScenesForChapter("c1").Returns(new List<SceneData> { sc });
        project.ReadSceneContentAsync(ch, sc).Returns("category cat cat");

        var matches = await sut.FindAsync(new FindOptions { Pattern = "cat", WholeWord = true });
        Assert.Equal(2, matches.Count);
    }

    [Fact]
    public async Task FindAsync_MatchCase_Respected()
    {
        var (sut, project) = Build();
        var ch = Chapter("c1");
        var sc = Scene("s1");
        project.GetChaptersOrdered().Returns(new List<ChapterData> { ch });
        project.GetScenesForChapter("c1").Returns(new List<SceneData> { sc });
        project.ReadSceneContentAsync(ch, sc).Returns("Cat cat CAT");

        var sensitive = await sut.FindAsync(new FindOptions { Pattern = "cat", MatchCase = true });
        Assert.Single(sensitive);
    }

    [Fact]
    public async Task FindAsync_UseRegex_AppliesPattern()
    {
        var (sut, project) = Build();
        var ch = Chapter("c1");
        var sc = Scene("s1");
        project.GetChaptersOrdered().Returns(new List<ChapterData> { ch });
        project.GetScenesForChapter("c1").Returns(new List<SceneData> { sc });
        project.ReadSceneContentAsync(ch, sc).Returns("a1 b2 c3");

        var matches = await sut.FindAsync(new FindOptions { Pattern = @"\d", UseRegex = true });
        Assert.Equal(3, matches.Count);
    }

    [Fact]
    public async Task FindAsync_CurrentScene_RequiresAnchors()
    {
        var (sut, project) = Build();
        project.GetChaptersOrdered().Returns(new List<ChapterData>());
        var result = await sut.FindAsync(new FindOptions { Pattern = "x", Scope = FindScope.CurrentScene });
        Assert.Empty(result);
    }

    [Fact]
    public async Task FindAsync_CurrentScene_ResolvesAnchoredScene()
    {
        var (sut, project) = Build();
        var ch = Chapter("c1");
        var sc = Scene("s1");
        project.GetChaptersOrdered().Returns(new List<ChapterData> { ch });
        project.GetScenesForChapter("c1").Returns(new List<SceneData> { sc });
        project.ReadSceneContentAsync(ch, sc).Returns("hello");

        var result = await sut.FindAsync(new FindOptions
        {
            Pattern = "hello", Scope = FindScope.CurrentScene,
            AnchorChapterGuid = "c1", AnchorSceneId = "s1"
        });
        Assert.Single(result);
    }

    [Fact]
    public async Task FindAsync_CurrentScene_MissingChapter_ReturnsEmpty()
    {
        var (sut, project) = Build();
        project.GetChaptersOrdered().Returns(new List<ChapterData>());
        var result = await sut.FindAsync(new FindOptions
        {
            Pattern = "x", Scope = FindScope.CurrentScene,
            AnchorChapterGuid = "nope", AnchorSceneId = "s1"
        });
        Assert.Empty(result);
    }

    [Fact]
    public async Task FindAsync_CurrentChapter_RequiresAnchor()
    {
        var (sut, project) = Build();
        project.GetChaptersOrdered().Returns(new List<ChapterData>());
        var result = await sut.FindAsync(new FindOptions { Pattern = "x", Scope = FindScope.CurrentChapter });
        Assert.Empty(result);
    }

    [Fact]
    public async Task FindAsync_CurrentChapter_MissingChapter_ReturnsEmpty()
    {
        var (sut, project) = Build();
        project.GetChaptersOrdered().Returns(new List<ChapterData>());
        var result = await sut.FindAsync(new FindOptions
        {
            Pattern = "x", Scope = FindScope.CurrentChapter, AnchorChapterGuid = "nope"
        });
        Assert.Empty(result);
    }

    [Fact]
    public async Task FindAsync_CurrentChapter_EnumeratesScenes()
    {
        var (sut, project) = Build();
        var ch = Chapter("c1");
        var sc = Scene("s1");
        project.GetChaptersOrdered().Returns(new List<ChapterData> { ch });
        project.GetScenesForChapter("c1").Returns(new List<SceneData> { sc });
        project.ReadSceneContentAsync(ch, sc).Returns("word word");

        var result = await sut.FindAsync(new FindOptions
        {
            Pattern = "word", Scope = FindScope.CurrentChapter, AnchorChapterGuid = "c1"
        });
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task ReplaceAllAsync_EmptyPattern_ReturnsZero()
    {
        var (sut, _) = Build();
        Assert.Equal(0, await sut.ReplaceAllAsync(new FindOptions { Pattern = "" }));
    }

    [Fact]
    public async Task ReplaceAllAsync_ReplacesAndSaves()
    {
        var (sut, project) = Build();
        var ch = Chapter("c1");
        var sc = Scene("s1");
        project.GetChaptersOrdered().Returns(new List<ChapterData> { ch });
        project.GetScenesForChapter("c1").Returns(new List<SceneData> { sc });
        project.ReadSceneContentAsync(ch, sc).Returns("<p>cat cat</p>");

        var count = await sut.ReplaceAllAsync(new FindOptions { Pattern = "cat", Replacement = "dog" });

        Assert.Equal(2, count);
        await project.Received(1).WriteSceneContentAsync(ch, sc, "<p>dog dog</p>");
        await project.Received(1).SaveScenesAsync();
        Assert.Equal(2, sc.WordCount);
    }

    [Fact]
    public async Task ReplaceAllAsync_NoMatches_DoesNotWriteOrSave()
    {
        var (sut, project) = Build();
        var ch = Chapter("c1");
        var sc = Scene("s1");
        project.GetChaptersOrdered().Returns(new List<ChapterData> { ch });
        project.GetScenesForChapter("c1").Returns(new List<SceneData> { sc });
        project.ReadSceneContentAsync(ch, sc).Returns("<p>nothing here</p>");

        var count = await sut.ReplaceAllAsync(new FindOptions { Pattern = "zzz", Replacement = "x" });

        Assert.Equal(0, count);
        await project.DidNotReceive().WriteSceneContentAsync(Arg.Any<ChapterData>(), Arg.Any<SceneData>(), Arg.Any<string>());
        await project.DidNotReceive().SaveScenesAsync();
    }

    [Fact]
    public async Task ReplaceAllAsync_TakesSnapshot_WhenServiceProvided()
    {
        var (sut, project) = Build();
        var ch = Chapter("c1");
        var sc = Scene("s1");
        project.GetChaptersOrdered().Returns(new List<ChapterData> { ch });
        project.GetScenesForChapter("c1").Returns(new List<SceneData> { sc });
        project.ReadSceneContentAsync(ch, sc).Returns("<p>cat</p>");
        var snapshots = Substitute.For<ISnapshotService>();

        await sut.ReplaceAllAsync(new FindOptions { Pattern = "cat", Replacement = "dog" }, snapshots);

        await snapshots.Received(1).TakeAsync(ch, sc, Arg.Any<string>());
    }

    [Fact]
    public async Task FindAsync_HonorsCancellation()
    {
        var (sut, project) = Build();
        var ch = Chapter("c1");
        project.GetChaptersOrdered().Returns(new List<ChapterData> { ch });
        project.GetScenesForChapter("c1").Returns(new List<SceneData> { Scene("s1") });
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            sut.FindAsync(new FindOptions { Pattern = "x" }, cts.Token));
    }
}
