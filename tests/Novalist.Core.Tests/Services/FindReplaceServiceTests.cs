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

    private static (FindReplaceService Sut, IProjectService Project, IEntityService Entities) BuildWithCodex()
    {
        var project = Substitute.For<IProjectService>();
        var entities = Substitute.For<IEntityService>();
        entities.LoadCharactersAsync().Returns([]);
        entities.LoadLocationsAsync().Returns([]);
        entities.LoadItemsAsync().Returns([]);
        entities.LoadLoreAsync().Returns([]);
        return (new FindReplaceService(project, entities), project, entities);
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

    // Every scene one run touches gets the same label, so the run can be
    // cleared as a unit. It used to be a constant string, which made every run
    // indistinguishable from every other.
    [Fact]
    public async Task ReplaceAllAsync_LabelsOneRunsSnapshotsAsOneBatch()
    {
        var (sut, project) = Build();
        var ch = Chapter("c1");
        var first = Scene("s1");
        var second = Scene("s2");
        project.GetChaptersOrdered().Returns(new List<ChapterData> { ch });
        project.GetScenesForChapter("c1").Returns(new List<SceneData> { first, second });
        project.ReadSceneContentAsync(ch, first).Returns("<p>cat</p>");
        project.ReadSceneContentAsync(ch, second).Returns("<p>cat</p>");
        var snapshots = Substitute.For<ISnapshotService>();
        var labels = new List<string>();
        snapshots
            .TakeAsync(Arg.Any<ChapterData>(), Arg.Any<SceneData>(), Arg.Do<string>(labels.Add))
            .Returns(new SceneSnapshot());

        await sut.ReplaceAllAsync(new FindOptions { Pattern = "cat", Replacement = "dog" }, snapshots);

        Assert.Equal(2, labels.Count);
        Assert.Single(labels.Distinct());
        Assert.StartsWith(FindReplaceService.SnapshotBatchPrefix, labels[0]);
    }

    [Fact]
    public void SnapshotBatchLabel_CarriesTheRunsOwnTime()
    {
        var label = FindReplaceService.SnapshotBatchLabel(new DateTime(2026, 7, 31, 1, 2, 3));

        Assert.Equal("Before find/replace 2026-07-31 01:02:03", label);
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

    // ── Whole-project scope ──
    //
    // The manual has always advertised this as "every scene in every book".
    // The service treated it as the active book and said so in a comment.

    /// <summary>
    /// A project of two books where switching books swaps what the chapter and
    /// scene accessors return, which is what the real service does.
    /// </summary>
    private static (FindReplaceService Sut, IProjectService Project) TwoBooks()
    {
        var (sut, project) = Build();
        var one = new BookData { Id = "b1", Name = "Book One" };
        var two = new BookData { Id = "b2", Name = "Book Two" };
        var meta = new ProjectMetadata { ActiveBookId = "b1", Books = { one, two } };
        project.CurrentProject.Returns(meta);

        var first = Chapter("c1", "First");
        var second = Chapter("c2", "Second");
        var sceneOne = Scene("s1", "Opening");
        var sceneTwo = Scene("s2", "Later");

        void Apply()
        {
            var inBookOne = meta.ActiveBookId == "b1";
            project.ActiveBook.Returns(inBookOne ? one : two);
            project.GetChaptersOrdered().Returns([inBookOne ? first : second]);
            project.GetScenesForChapter(inBookOne ? "c1" : "c2")
                .Returns([inBookOne ? sceneOne : sceneTwo]);
        }

        project.SwitchBookAsync(Arg.Any<string>()).Returns(call =>
        {
            meta.ActiveBookId = call.Arg<string>();
            Apply();
            return Task.CompletedTask;
        });
        project.ReadSceneContentAsync(first, sceneOne).Returns("<p>the bell rings</p>");
        project.ReadSceneContentAsync(second, sceneTwo).Returns("<p>the bell again</p>");
        Apply();
        return (sut, project);
    }

    [Fact]
    public async Task FindAsync_ProjectScope_SpansEveryBook()
    {
        var (sut, project) = TwoBooks();

        var matches = await sut.FindAsync(
            new FindOptions { Pattern = "bell", Scope = FindScope.Project });

        Assert.Equal(2, matches.Count);
        Assert.Equal(["Book One", "Book Two"], matches.Select(m => m.BookTitle));
        // And the writer is left in the book they started in.
        Assert.Equal("b1", project.CurrentProject!.ActiveBookId);
    }

    [Fact]
    public async Task FindAsync_ActiveBookScope_StaysInOneBook()
    {
        var (sut, _) = TwoBooks();

        var matches = await sut.FindAsync(
            new FindOptions { Pattern = "bell", Scope = FindScope.ActiveBook });

        Assert.Equal("Book One", Assert.Single(matches).BookTitle);
    }

    [Fact]
    public async Task ReplaceAllAsync_ProjectScope_ReachesEveryBook()
    {
        var (sut, project) = TwoBooks();

        var replaced = await sut.ReplaceAllAsync(new FindOptions
        {
            Pattern = "bell",
            Replacement = "chime",
            Scope = FindScope.Project
        });

        Assert.Equal(2, replaced);
        // Saved once per book: the manifest belongs to whichever book is open,
        // so one save at the end would write it into the wrong one.
        await project.Received(2).SaveScenesAsync();
        Assert.Equal("b1", project.CurrentProject!.ActiveBookId);
    }

    [Fact]
    public async Task FindAsync_ProjectScope_WithOneBook_DoesNotSwitchAtAll()
    {
        var (sut, project) = Build();
        var meta = new ProjectMetadata
        {
            ActiveBookId = "b1",
            Books = { new BookData { Id = "b1", Name = "Only" } }
        };
        project.CurrentProject.Returns(meta);
        var ch = Chapter("c1");
        var sc = Scene("s1");
        project.GetChaptersOrdered().Returns([ch]);
        project.GetScenesForChapter("c1").Returns([sc]);
        project.ReadSceneContentAsync(ch, sc).Returns("<p>the bell</p>");

        Assert.Single(await sut.FindAsync(
            new FindOptions { Pattern = "bell", Scope = FindScope.Project }));

        // Reopening the only book would be a pointless round trip through disk.
        await project.DidNotReceive().SwitchBookAsync(Arg.Any<string>());
    }

    [Fact]
    public async Task FindAsync_IncludeSceneNotes_SearchesSynopsisNotesAndComments()
    {
        var (sut, project) = Build();
        var ch = Chapter("c1");
        var sc = Scene("s1");
        sc.Synopsis = "The bell tolls";
        sc.Notes = "check the bell";
        sc.Comments = [new SceneComment { Text = "which bell?" }];
        project.GetChaptersOrdered().Returns([ch]);
        project.GetScenesForChapter("c1").Returns([sc]);
        project.ReadSceneContentAsync(ch, sc).Returns("<p>a bell</p>");

        var matches = await sut.FindAsync(
            new FindOptions { Pattern = "bell", IncludeSceneNotes = true });

        Assert.Equal(
            new[] { "prose", "synopsis", "notes", "comment" },
            matches.Select(m => m.Field).ToArray());
    }

    [Fact]
    public async Task FindAsync_WithoutIncludeSceneNotes_SearchesProseOnly()
    {
        var (sut, project) = Build();
        var ch = Chapter("c1");
        var sc = Scene("s1");
        sc.Synopsis = "The bell tolls";
        project.GetChaptersOrdered().Returns([ch]);
        project.GetScenesForChapter("c1").Returns([sc]);
        project.ReadSceneContentAsync(ch, sc).Returns("<p>nothing here</p>");

        Assert.Empty(await sut.FindAsync(new FindOptions { Pattern = "bell" }));
    }

    [Fact]
    public async Task FindAsync_IncludeSceneNotes_SkipsEmptyFields()
    {
        var (sut, project) = Build();
        var ch = Chapter("c1");
        var sc = Scene("s1");
        project.GetChaptersOrdered().Returns([ch]);
        project.GetScenesForChapter("c1").Returns([sc]);
        project.ReadSceneContentAsync(ch, sc).Returns("<p>a bell</p>");

        var matches = await sut.FindAsync(
            new FindOptions { Pattern = "bell", IncludeSceneNotes = true });

        Assert.Single(matches);
    }

    [Fact]
    public async Task ReplaceAllAsync_IncludeSceneNotes_RewritesSynopsisAndNotes()
    {
        var (sut, project) = Build();
        var ch = Chapter("c1");
        var sc = Scene("s1");
        sc.Synopsis = "The bell tolls";
        sc.Notes = "check the bell";
        sc.Comments = [new SceneComment { Text = "which bell?" }];
        project.GetChaptersOrdered().Returns([ch]);
        project.GetScenesForChapter("c1").Returns([sc]);
        project.ReadSceneContentAsync(ch, sc).Returns("<p>a bell</p>");

        var count = await sut.ReplaceAllAsync(
            new FindOptions { Pattern = "bell", Replacement = "horn", IncludeSceneNotes = true },
            null);

        Assert.Equal(3, count);
        Assert.Equal("The horn tolls", sc.Synopsis);
        Assert.Equal("check the horn", sc.Notes);
        // A comment is a conversation - a blind rewrite would put words in a mouth.
        Assert.Equal("which bell?", sc.Comments[0].Text);
    }

    [Fact]
    public async Task ReplaceAllAsync_IncludeSceneNotes_NotesOnlyStillSaves()
    {
        var (sut, project) = Build();
        var ch = Chapter("c1");
        var sc = Scene("s1");
        sc.Synopsis = "The bell tolls";
        project.GetChaptersOrdered().Returns([ch]);
        project.GetScenesForChapter("c1").Returns([sc]);
        project.ReadSceneContentAsync(ch, sc).Returns("<p>nothing here</p>");

        var count = await sut.ReplaceAllAsync(
            new FindOptions { Pattern = "bell", Replacement = "horn", IncludeSceneNotes = true },
            null);

        Assert.Equal(1, count);
        Assert.Equal("The horn tolls", sc.Synopsis);
        await project.Received().SaveScenesAsync();
    }

    [Fact]
    public async Task FindAsync_IncludeCodex_SearchesNamesAndSections()
    {
        var (sut, project, entities) = BuildWithCodex();
        project.GetChaptersOrdered().Returns([]);
        project.ActiveBook.Returns(new BookData { Id = "b1", Name = "Only" });
        entities.LoadCharactersAsync().Returns([
            new CharacterData
            {
                Id = "e1",
                Name = "Bell",
                Surname = "Harrow",
                Sections = { new EntitySection { Content = "<p>rings the bell</p>" } }
            }
        ]);
        entities.LoadLocationsAsync().Returns([
            new LocationData { Id = "l1", Name = "Bell Tower" }
        ]);
        entities.LoadItemsAsync().Returns([
            new ItemData
            {
                Id = "i1",
                Name = "Rope",
                Sections = { new EntitySection { Content = "for the bell" } }
            }
        ]);
        entities.LoadLoreAsync().Returns([
            new LoreData { Id = "o1", Name = "The Bell Rite" }
        ]);

        var matches = await sut.FindAsync(
            new FindOptions { Pattern = "bell", IncludeCodex = true });

        Assert.Equal(5, matches.Count);
        Assert.All(matches, m => Assert.Equal("codex", m.Field));
        Assert.Equal("Only", matches[0].BookTitle);
        Assert.Equal("Bell Harrow", matches[0].SceneTitle);
    }

    [Fact]
    public async Task FindAsync_IncludeCodex_WithoutEntityService_IsIgnored()
    {
        var (sut, project) = Build();
        project.GetChaptersOrdered().Returns([]);

        Assert.Empty(await sut.FindAsync(new FindOptions { Pattern = "bell", IncludeCodex = true }));
    }
}
