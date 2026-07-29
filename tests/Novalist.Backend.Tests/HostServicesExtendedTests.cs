using NSubstitute;
using Novalist.Backend.Extensions;
using Novalist.Backend.Tests.TestHelpers;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Sdk.Hooks;
using Novalist.Sdk.Models;
using Novalist.Sdk.Services;
using Xunit;

namespace Novalist.Backend.Tests;

/// <summary>
/// The SDK surface added so that work placed outside core could be written
/// outside core: research, review, story structure, structural editing, a
/// command bus and post-export checks.
///
/// Run against a real project rather than mocks, because most of these are
/// exactly the calls where "it compiled" and "it persisted" differ.
/// </summary>
[Collection("BackendStatics")]
public class HostServicesExtendedTests
{
    private static (HostServices Host, ProjectService Proj, TempDir Dir) Build()
    {
        var dir = new TempDir();
        var file = new FileService();
        var proj = new ProjectService(file);
        proj.CreateProjectAsync(dir.Path, "P", "Book").GetAwaiter().GetResult();
        var settings = Substitute.For<ISettingsService>();
        settings.Settings.Returns(new AppSettings());
        settings.SaveAsync().Returns(Task.CompletedTask);
        return (new HostServices(file, proj, new EntityService(proj), settings), proj, dir);
    }

    private static async Task<(string Chapter, string Scene)> SceneAsync(
        HostServices host, string title = "Arrival", string html = "<p>The bell rang once.</p>")
    {
        var chapter = await host.ProjectService.CreateChapterAsync("One");
        var scene = await host.ProjectService.CreateSceneAsync(chapter, title);
        await host.ProjectService.WriteSceneContentAsync(chapter, scene, html);
        return (chapter, scene);
    }

    [Fact]
    public void EveryNewFacadeIsReachable()
    {
        var (host, _, dir) = Build();
        using var _d = dir;

        Assert.Same(host, host.ResearchService);
        Assert.Same(host, host.ReviewService);
        Assert.Same(host, host.StoryService);
    }

    // ── Structural editing ──

    [Fact]
    public async Task ChaptersAndScenesCanBeRenamedMovedAndLabelled()
    {
        var (host, proj, dir) = Build();
        using var _d = dir;
        var (chapter, scene) = await SceneAsync(host);
        var second = await host.ProjectService.CreateChapterAsync("Two");

        Assert.True(await host.ProjectService.RenameChapterAsync(chapter, "Opening"));
        Assert.True(await host.ProjectService.RenameSceneAsync(chapter, scene, "The bell"));
        Assert.True(await host.ProjectService.SetChapterActAsync(chapter, "Act I"));
        Assert.True(await host.ProjectService.MoveChapterAsync(second, 1));
        Assert.True(await host.ProjectService.MoveSceneAsync(scene, second, 0));

        Assert.Equal("Opening", proj.GetChaptersOrdered().Single(c => c.Guid == chapter).Title);
        // Moved across chapters, which is the case the SDK could not express.
        Assert.Equal("The bell", proj.GetScenesForChapter(second).Single().Title);
        Assert.Empty(proj.GetScenesForChapter(chapter));
        Assert.Equal("Act I", proj.GetChaptersOrdered().Single(c => c.Guid == chapter).Act);
        Assert.Equal(1, proj.GetChaptersOrdered().Single(c => c.Guid == second).Order);
    }

    [Fact]
    public async Task StructuralEditsOnSomethingThatIsNotThereReturnFalse()
    {
        var (host, _, dir) = Build();
        using var _d = dir;
        var (chapter, _) = await SceneAsync(host);

        Assert.False(await host.ProjectService.RenameChapterAsync("nope", "x"));
        Assert.False(await host.ProjectService.RenameSceneAsync(chapter, "nope", "x"));
        Assert.False(await host.ProjectService.RenameSceneAsync("nope", "nope", "x"));
        Assert.False(await host.ProjectService.MoveChapterAsync("nope", 1));
        Assert.False(await host.ProjectService.MoveSceneAsync("nope", chapter, 0));
        Assert.False(await host.ProjectService.MoveSceneAsync("nope", "nope", 0));
        Assert.False(await host.ProjectService.SetChapterActAsync("nope", "Act I"));
        Assert.False(await host.ProjectService.TrashChapterAsync("nope"));
        Assert.False(await host.ProjectService.ArchiveSceneAsync(chapter, "nope"));
    }

    [Fact]
    public async Task TheDestructiveVerbsAreRecoverableOnes()
    {
        // An extension can put a chapter aside and cannot erase one. Trash and
        // archive are both things the writer can undo from the binder.
        var (host, proj, dir) = Build();
        using var _d = dir;
        var (chapter, scene) = await SceneAsync(host);

        Assert.True(await host.ProjectService.ArchiveSceneAsync(chapter, scene));
        Assert.Single(proj.GetArchivedScenes());

        Assert.True(await host.ProjectService.TrashChapterAsync(chapter));
        Assert.Single(proj.GetTrashedChapters());
    }

    [Fact]
    public async Task SettingABlankActTakesTheChapterOutOfOne()
    {
        var (host, proj, dir) = Build();
        using var _d = dir;
        var (chapter, _) = await SceneAsync(host);
        await host.ProjectService.SetChapterActAsync(chapter, "Act I");

        await host.ProjectService.SetChapterActAsync(chapter, "   ");

        Assert.Equal(string.Empty, proj.GetChaptersOrdered().Single().Act);
        Assert.Empty(host.StoryService.GetActs());
    }

    // ── Entity writing ──

    [Fact]
    public async Task SectionsCanBeWrittenOntoEveryKindOfEntry()
    {
        var (host, _, dir) = Build();
        using var _d = dir;

        foreach (var typeKey in new[] { "character", "location", "item", "lore" })
        {
            var id = await host.EntityService.CreateEntityAsync(typeKey, "Mira Vance");
            Assert.NotNull(id);

            Assert.True(await host.EntityService.SaveEntityAsync(
                typeKey, id!, sections: [new CustomEntitySectionInfo
                {
                    Title = "Childhood",
                    Content = "Grew up at the docks."
                }]));
        }

        var character = Assert.Single(await host.EntityService.LoadCharactersAsync());
        var detailed = await host.EntityService.GetCharacterDetailedAsync(character.Id, null, null);
        Assert.Contains(detailed!.Sections, s => s.Title == "Childhood");
    }

    [Fact]
    public async Task WritingOneSectionLeavesTheOthersAlone()
    {
        // A questionnaire about childhood must not erase what is recorded about
        // appearance.
        var (host, _, dir) = Build();
        using var _d = dir;
        var id = (await host.EntityService.CreateEntityAsync("location", "Ashport"))!;
        await host.EntityService.SaveEntityAsync("location", id,
            sections: [new CustomEntitySectionInfo { Title = "Look", Content = "Grey." }]);

        await host.EntityService.SaveEntityAsync("location", id,
            sections: [new CustomEntitySectionInfo { Title = "Sound", Content = "Gulls." }]);

        var sections = (await new EntityService((ProjectService)GetProject(host)).LoadLocationsAsync())
            .Single().Sections;
        Assert.Equal(2, sections.Count);
        Assert.Contains(sections, s => s.Content == "Grey.");
    }

    [Fact]
    public async Task WritingTheSameSectionTwiceReplacesIt()
    {
        var (host, proj, dir) = Build();
        using var _d = dir;
        var id = (await host.EntityService.CreateEntityAsync("item", "The lantern"))!;

        await host.EntityService.SaveEntityAsync("item", id,
            sections: [new CustomEntitySectionInfo { Title = "Use", Content = "First." }]);
        await host.EntityService.SaveEntityAsync("item", id,
            sections: [new CustomEntitySectionInfo { Title = "Use", Content = "Second." }]);

        var section = Assert.Single((await new EntityService(proj).LoadItemsAsync()).Single().Sections);
        Assert.Equal("Second.", section.Content);
    }

    [Fact]
    public async Task ACharactersNameSplitsIntoBothFields()
    {
        var (host, proj, dir) = Build();
        using var _d = dir;
        var id = (await host.EntityService.CreateEntityAsync("character", "Someone"))!;

        await host.EntityService.SaveEntityAsync("character", id, name: "Mira Vance");

        var character = (await new EntityService(proj).LoadCharactersAsync()).Single();
        Assert.Equal("Mira", character.Name);
        Assert.Equal("Vance", character.Surname);
    }

    [Fact]
    public async Task AOneWordNameLeavesNoSurnameBehind()
    {
        var (host, proj, dir) = Build();
        using var _d = dir;
        var id = (await host.EntityService.CreateEntityAsync("character", "Mira Vance"))!;

        await host.EntityService.SaveEntityAsync("character", id, name: "Mira");

        var character = (await new EntityService(proj).LoadCharactersAsync()).Single();
        Assert.Equal("Mira", character.Name);
        Assert.Equal(string.Empty, character.Surname);
    }

    [Fact]
    public async Task ADescriptionOnAKindWithNoSuchFieldBecomesANote()
    {
        var (host, proj, dir) = Build();
        using var _d = dir;
        var id = (await host.EntityService.CreateEntityAsync("character", "Mira"))!;

        await host.EntityService.SaveEntityAsync("character", id, description: "Ship's mate.");

        var sections = (await new EntityService(proj).LoadCharactersAsync()).Single().Sections;
        Assert.Contains(sections, s => s.Title == "Notes" && s.Content == "Ship's mate.");
    }

    [Fact]
    public async Task ADescriptionOnAKindThatHasOneIsStoredThere()
    {
        var (host, proj, dir) = Build();
        using var _d = dir;
        var id = (await host.EntityService.CreateEntityAsync("lore", "The Compact"))!;

        await host.EntityService.SaveEntityAsync("lore", id, description: "Signed at sea.");

        Assert.Equal("Signed at sea.", (await new EntityService(proj).LoadLoreAsync()).Single().Description);
    }

    [Fact]
    public async Task WritingToAnEntryThatIsNotThereReturnsFalse()
    {
        var (host, _, dir) = Build();
        using var _d = dir;

        Assert.False(await host.EntityService.SaveEntityAsync("character", "nope", name: "x"));
        Assert.False(await host.EntityService.SaveEntityAsync("location", "nope", name: "x"));
        Assert.False(await host.EntityService.SaveEntityAsync("item", "nope", name: "x"));
        Assert.False(await host.EntityService.SaveEntityAsync("lore", "nope", name: "x"));
        Assert.False(await host.EntityService.SaveEntityAsync("madeup", "nope", name: "x"));
        Assert.False(await host.EntityService.SaveEntityAsync("character", "  ", name: "x"));
    }

    [Fact]
    public async Task ACustomEntryTakesSectionsLikeAnyOther()
    {
        var (host, proj, dir) = Build();
        using var _d = dir;
        var entities = new EntityService(proj);
        await entities.SaveCustomEntityTypeAsync(new CustomEntityTypeDefinition
        {
            TypeKey = "faction",
            DisplayName = "Faction",
            DisplayNamePlural = "Factions"
        });
        var id = (await host.EntityService.CreateEntityAsync("faction", "The Compact"))!;

        Assert.True(await host.EntityService.SaveEntityAsync(
            "faction", id,
            name: "The Harbour Compact",
            description: "Signed at sea.",
            sections: [new CustomEntitySectionInfo { Title = "Aims", Content = "Control the docks." }]));

        var stored = (await entities.LoadCustomEntitiesAsync("faction")).Single();
        Assert.Equal("The Harbour Compact", stored.Name);
        Assert.Contains(stored.Sections!, s => s.Title == "Aims");
        // Custom entries have no description field, so it lands as a note.
        Assert.Contains(stored.Sections!, s => s.Title == "Notes" && s.Content == "Signed at sea.");
    }

    // ── Research ──

    [Fact]
    public async Task ResearchItemsRoundTrip()
    {
        var (host, _, dir) = Build();
        using var _d = dir;

        var id = await host.ResearchService.SaveAsync(new ResearchItemInfo
        {
            Title = "Ship logs",
            Type = "Link",
            Content = "https://example.test/logs",
            Tags = ["sea"],
            EntityRefs = ["e1"]
        });

        var stored = Assert.Single(host.ResearchService.GetAll());
        Assert.Equal(id, stored.Id);
        Assert.Equal("Ship logs", stored.Title);
        Assert.Equal("Link", stored.Type);
        Assert.Equal("sea", Assert.Single(stored.Tags));
        Assert.Equal("e1", Assert.Single(stored.EntityRefs));
    }

    [Fact]
    public async Task SavingAnItemAgainUpdatesItRatherThanAddingOne()
    {
        var (host, _, dir) = Build();
        using var _d = dir;
        var id = await host.ResearchService.SaveAsync(new ResearchItemInfo { Title = "First" });

        await host.ResearchService.SaveAsync(new ResearchItemInfo { Id = id, Title = "Second" });

        Assert.Equal("Second", Assert.Single(host.ResearchService.GetAll()).Title);
    }

    [Fact]
    public async Task AnUnknownTypeReadsAsANote()
    {
        var (host, _, dir) = Build();
        using var _d = dir;

        await host.ResearchService.SaveAsync(new ResearchItemInfo { Title = "x", Type = "Nonsense" });

        Assert.Equal("Note", Assert.Single(host.ResearchService.GetAll()).Type);
    }

    [Fact]
    public async Task DeletingResearchNeedsAnIdThatExists()
    {
        var (host, _, dir) = Build();
        using var _d = dir;
        var id = await host.ResearchService.SaveAsync(new ResearchItemInfo { Title = "x" });

        Assert.False(await host.ResearchService.DeleteAsync("nope"));
        Assert.True(await host.ResearchService.DeleteAsync(id));
        Assert.Empty(host.ResearchService.GetAll());
    }

    [Fact]
    public async Task ImportingAFileCopiesItIntoTheProject()
    {
        var (host, proj, dir) = Build();
        using var _d = dir;
        var source = Path.Combine(dir.Path, "outside.txt");
        await File.WriteAllTextAsync(source, "notes");

        var relative = await host.ResearchService.ImportFileAsync(source);

        Assert.False(string.IsNullOrEmpty(relative));
        Assert.True(File.Exists(Path.Combine(proj.ProjectRoot!, relative)));
    }

    // ── Review ──

    [Fact]
    public async Task CommentsCarryTheirAuthorAndCanBeResolvedAndRemoved()
    {
        var (host, _, dir) = Build();
        using var _d = dir;
        var (chapter, scene) = await SceneAsync(host);

        var id = await host.ReviewService.AddCommentAsync(
            chapter, scene, "the bell", "Is this the same bell?", "Mira");

        var comment = Assert.Single(await host.ReviewService.GetCommentsAsync(chapter, scene));
        Assert.Equal("Mira", comment.Author);
        Assert.Equal("the bell", comment.AnchorText);
        Assert.False(comment.Resolved);

        Assert.True(await host.ReviewService.SetCommentResolvedAsync(chapter, scene, id, true));
        Assert.True((await host.ReviewService.GetCommentsAsync(chapter, scene)).Single().Resolved);

        Assert.True(await host.ReviewService.DeleteCommentAsync(chapter, scene, id));
        Assert.Empty(await host.ReviewService.GetCommentsAsync(chapter, scene));
    }

    [Fact]
    public async Task CommentCallsOnSomethingThatIsNotThereAreSafe()
    {
        var (host, _, dir) = Build();
        using var _d = dir;
        var (chapter, scene) = await SceneAsync(host);

        Assert.Empty(await host.ReviewService.GetCommentsAsync(chapter, "nope"));
        Assert.Empty(await host.ReviewService.GetCommentsAsync("nope", "nope"));
        Assert.Equal(string.Empty,
            await host.ReviewService.AddCommentAsync("nope", "nope", "a", "b", "c"));
        Assert.False(await host.ReviewService.SetCommentResolvedAsync(chapter, scene, "nope", true));
        Assert.False(await host.ReviewService.DeleteCommentAsync(chapter, scene, "nope"));
        Assert.False(await host.ReviewService.DeleteCommentAsync("nope", "nope", "nope"));
    }

    [Fact]
    public async Task AnUnattributedCommentStaysUnattributed()
    {
        var (host, _, dir) = Build();
        using var _d = dir;
        var (chapter, scene) = await SceneAsync(host);

        await host.ReviewService.AddCommentAsync(chapter, scene, "bell", "note", "   ");

        Assert.Equal(string.Empty,
            (await host.ReviewService.GetCommentsAsync(chapter, scene)).Single().Author);
    }

    [Fact]
    public async Task AnExtensionProposesAnEditRatherThanMakingOne()
    {
        // The whole point: a machine's opinion arrives as a suggestion the
        // writer answers, not as a silent rewrite of their manuscript.
        var (host, proj, dir) = Build();
        using var _d = dir;
        var (chapter, scene) = await SceneAsync(host);

        Assert.True(await host.ReviewService.SuggestEditAsync(
            chapter, scene, "once", "twice", "Mira"));

        Assert.Equal(2, await host.ReviewService.PendingSuggestionCountAsync(chapter, scene));

        // The original words are still there, marked rather than gone.
        var chapterData = proj.GetChaptersOrdered().Single(c => c.Guid == chapter);
        var sceneData = proj.GetScenesForChapter(chapter).Single();
        var html = await proj.ReadSceneContentAsync(chapterData, sceneData);
        Assert.Contains("once", html);
        Assert.Contains("twice", html);
        Assert.Contains("data-nl-change", html);
    }

    [Fact]
    public async Task AnEmptyReplacementProposesACut()
    {
        var (host, _, dir) = Build();
        using var _d = dir;
        var (chapter, scene) = await SceneAsync(host);

        Assert.True(await host.ReviewService.SuggestEditAsync(chapter, scene, "once", "", "Mira"));

        Assert.Equal(1, await host.ReviewService.PendingSuggestionCountAsync(chapter, scene));
    }

    [Fact]
    public async Task ProposingAnEditToWordsThatAreNotThereIsRefused()
    {
        // No honest place to attach it, and guessing at the nearest phrase would
        // put the proposal on prose the extension never read.
        var (host, _, dir) = Build();
        using var _d = dir;
        var (chapter, scene) = await SceneAsync(host);

        Assert.False(await host.ReviewService.SuggestEditAsync(
            chapter, scene, "a sentence nobody wrote", "x", "Mira"));
        Assert.False(await host.ReviewService.SuggestEditAsync(chapter, scene, "", "x", "Mira"));
        Assert.False(await host.ReviewService.SuggestEditAsync("nope", "nope", "once", "x", "Mira"));
        Assert.Equal(0, await host.ReviewService.PendingSuggestionCountAsync("nope", "nope"));
    }

    [Fact]
    public async Task ASuggestionUpdatesTheWordCountToWhatWouldBeAccepted()
    {
        var (host, proj, dir) = Build();
        using var _d = dir;
        var (chapter, scene) = await SceneAsync(host, html: "<p>The bell rang.</p>");

        await host.ReviewService.SuggestEditAsync(
            chapter, scene, "rang", "rang twice and stopped", "Mira");

        Assert.Equal(6, proj.GetScenesForChapter(chapter).Single().WordCount);
    }

    // ── Story structure ──

    [Fact]
    public async Task ASceneReportsWhatItIsAndNotJustWhatItSays()
    {
        var (host, proj, dir) = Build();
        using var _d = dir;
        var (chapter, scene) = await SceneAsync(host);
        await host.ProjectService.SetChapterActAsync(chapter, "Act I");
        await host.ProjectService.SetSceneSynopsisAsync(chapter, scene, "She arrives.");

        var data = proj.GetScenesForChapter(chapter).Single();
        data.AnalysisOverrides = new SceneAnalysisOverrides
        {
            Pov = "Mira",
            Emotion = "dread",
            Intensity = 7,
            Conflict = "the harbourmaster",
            Tags = ["night"]
        };
        data.Stage = "Inciting incident";
        data.NarrativeMode = "Flashback";
        data.DateRange = new StoryDateRange { Start = "1893-04-02", End = "1893-04-03" };
        await proj.SaveScenesAsync();

        var detail = host.StoryService.GetSceneDetail(chapter, scene);

        Assert.NotNull(detail);
        Assert.Equal("Mira", detail!.Pov);
        Assert.Equal("dread", detail.Emotion);
        Assert.Equal(7, detail.Intensity);
        Assert.Equal("the harbourmaster", detail.Conflict);
        Assert.Equal("Inciting incident", detail.Stage);
        Assert.Equal("Flashback", detail.NarrativeMode);
        Assert.Equal("1893-04-02", detail.DateStart);
        Assert.Equal("1893-04-03", detail.DateEnd);
        Assert.Equal("night", Assert.Single(detail.Tags));
        Assert.Equal("Act I", detail.Act);
        Assert.Equal("She arrives.", detail.Synopsis);
    }

    [Fact]
    public async Task ASceneWithNothingRecordedReportsEmptyRatherThanFailing()
    {
        var (host, _, dir) = Build();
        using var _d = dir;
        var (chapter, scene) = await SceneAsync(host);

        var detail = host.StoryService.GetSceneDetail(chapter, scene);

        Assert.NotNull(detail);
        Assert.Equal(string.Empty, detail!.Pov);
        Assert.Null(detail.Intensity);
        Assert.Empty(detail.Tags);
        Assert.Empty(detail.PlotlineIds);
        Assert.Empty(detail.Properties);
    }

    [Fact]
    public async Task ASceneThatIsNotThereHasNoDetail()
    {
        var (host, _, dir) = Build();
        using var _d = dir;
        var (chapter, _) = await SceneAsync(host);

        Assert.Null(host.StoryService.GetSceneDetail(chapter, "nope"));
        Assert.Null(host.StoryService.GetSceneDetail("nope", "nope"));
    }

    [Fact]
    public async Task ActsAreReadFromTheChaptersThatCarryThem()
    {
        var (host, _, dir) = Build();
        using var _d = dir;
        var first = await host.ProjectService.CreateChapterAsync("One");
        var second = await host.ProjectService.CreateChapterAsync("Two");
        var third = await host.ProjectService.CreateChapterAsync("Three");
        await host.ProjectService.SetChapterActAsync(first, "Act I");
        await host.ProjectService.SetChapterActAsync(second, "Act I");
        await host.ProjectService.SetChapterActAsync(third, "Act II");

        var acts = host.StoryService.GetActs();

        Assert.Equal(2, acts.Count);
        Assert.Equal(2, acts.Single(a => a.Name == "Act I").ChapterGuids.Count);
        Assert.Equal(third, acts.Single(a => a.Name == "Act II").ChapterGuids.Single());
    }

    [Fact]
    public async Task PlotThreadsCanBeCreatedAndPutOnAScene()
    {
        var (host, _, dir) = Build();
        using var _d = dir;
        var (chapter, scene) = await SceneAsync(host);

        var id = await host.StoryService.CreatePlotlineAsync("The betrayal", "#ff0000", "Slow burn");

        var plotline = Assert.Single(host.StoryService.GetPlotlines());
        Assert.Equal(id, plotline.Id);
        Assert.Equal("The betrayal", plotline.Name);
        Assert.Equal("#ff0000", plotline.Color);
        Assert.Equal("Slow burn", plotline.Description);

        Assert.True(await host.StoryService.SetScenePlotlinesAsync(chapter, scene, [id]));
        Assert.Equal(id, host.StoryService.GetSceneDetail(chapter, scene)!.PlotlineIds.Single());
    }

    [Fact]
    public async Task APlotThreadWithNoColourTakesTheHostsDefault()
    {
        var (host, _, dir) = Build();
        using var _d = dir;

        await host.StoryService.CreatePlotlineAsync("Unnamed colour");

        Assert.False(string.IsNullOrEmpty(Assert.Single(host.StoryService.GetPlotlines()).Color));
    }

    [Fact]
    public async Task SettingThreadsOnASceneThatIsNotThereReturnsFalse()
    {
        var (host, _, dir) = Build();
        using var _d = dir;
        var (chapter, _) = await SceneAsync(host);

        Assert.False(await host.StoryService.SetScenePlotlinesAsync(chapter, "nope", ["x"]));
    }

    [Fact]
    public async Task TimelineEventsRoundTripAndCanBeRemoved()
    {
        var (host, _, dir) = Build();
        using var _d = dir;
        var (chapter, _) = await SceneAsync(host);

        var id = await host.StoryService.SaveTimelineEventAsync(new TimelineEventInfo
        {
            Title = "The fire",
            Date = "1893-04-02",
            Description = "The warehouse goes up.",
            CategoryId = "world",
            LinkedChapterGuid = chapter
        });

        var stored = Assert.Single(host.StoryService.GetTimelineEvents());
        Assert.Equal(id, stored.Id);
        Assert.Equal("The fire", stored.Title);
        Assert.Equal("world", stored.CategoryId);
        Assert.Equal(chapter, stored.LinkedChapterGuid);

        await host.StoryService.SaveTimelineEventAsync(new TimelineEventInfo
        {
            Id = id,
            Title = "The fire, later"
        });
        Assert.Equal("The fire, later", Assert.Single(host.StoryService.GetTimelineEvents()).Title);

        Assert.False(await host.StoryService.DeleteTimelineEventAsync("nope"));
        Assert.True(await host.StoryService.DeleteTimelineEventAsync(id));
        Assert.Empty(host.StoryService.GetTimelineEvents());
    }

    [Fact]
    public async Task AnEventWithNoCategoryIsAPlotEvent()
    {
        var (host, _, dir) = Build();
        using var _d = dir;

        await host.StoryService.SaveTimelineEventAsync(new TimelineEventInfo { Title = "x" });

        Assert.Equal("plot", Assert.Single(host.StoryService.GetTimelineEvents()).CategoryId);
    }

    [Fact]
    public void StoryCallsWithNoBookOpenAreEmptyRatherThanThrowing()
    {
        var file = Substitute.For<IFileService>();
        var proj = Substitute.For<IProjectService>();
        proj.GetChaptersOrdered().Returns([]);
        var settings = Substitute.For<ISettingsService>();
        settings.Settings.Returns(new AppSettings());
        using var host = new HostServices(file, proj, Substitute.For<IEntityService>(), settings);

        Assert.Empty(host.StoryService.GetActs());
        Assert.Empty(host.StoryService.GetPlotlines());
        Assert.Empty(host.StoryService.GetTimelineEvents());
    }

    [Fact]
    public async Task WritingStoryDataWithNoBookOpenIsRefusedRatherThanCrashing()
    {
        var file = Substitute.For<IFileService>();
        var proj = Substitute.For<IProjectService>();
        proj.GetChaptersOrdered().Returns([]);
        var settings = Substitute.For<ISettingsService>();
        settings.Settings.Returns(new AppSettings());
        using var host = new HostServices(file, proj, Substitute.For<IEntityService>(), settings);

        Assert.Equal(string.Empty, await host.StoryService.CreatePlotlineAsync("x"));
        Assert.Equal(string.Empty,
            await host.StoryService.SaveTimelineEventAsync(new TimelineEventInfo { Title = "x" }));
        Assert.False(await host.StoryService.DeleteTimelineEventAsync("x"));
    }

    // ── Commands ──

    [Fact]
    public async Task ACommandCanBeRegisteredFoundAndRun()
    {
        var (host, _, dir) = Build();
        using var _d = dir;
        var ran = string.Empty;

        host.RegisterCommand(
            new HostCommandInfo { Id = "com.example.run", Title = "Run", Mutates = true },
            args => { ran = args ?? "none"; return Task.CompletedTask; });

        var listed = Assert.Single(host.GetCommands());
        Assert.Equal("com.example.run", listed.Id);
        Assert.True(listed.Mutates);

        Assert.True(await host.InvokeCommandAsync("com.example.run", "{\"n\":1}"));
        Assert.Equal("{\"n\":1}", ran);
    }

    [Fact]
    public async Task InvokingACommandThatDoesNotExistSaysSo()
    {
        var (host, _, dir) = Build();
        using var _d = dir;

        Assert.False(await host.InvokeCommandAsync("nothing.here"));
    }

    [Fact]
    public async Task RegisteringTheSameIdTwiceReplacesTheHandler()
    {
        var (host, _, dir) = Build();
        using var _d = dir;
        var which = 0;
        var info = new HostCommandInfo { Id = "com.example.run", Title = "Run" };

        host.RegisterCommand(info, _ => { which = 1; return Task.CompletedTask; });
        host.RegisterCommand(info, _ => { which = 2; return Task.CompletedTask; });
        await host.InvokeCommandAsync("com.example.run");

        Assert.Equal(2, which);
        Assert.Single(host.GetCommands());
    }

    [Fact]
    public async Task ACommandWithNoIdOrNoHandlerIsNotRegistered()
    {
        var (host, _, dir) = Build();
        using var _d = dir;

        host.RegisterCommand(new HostCommandInfo { Id = "  " }, _ => Task.CompletedTask);
        host.RegisterCommand(new HostCommandInfo { Id = "x" }, null!);
        host.RegisterCommand(null!, _ => Task.CompletedTask);

        Assert.Empty(host.GetCommands());
        Assert.False(await host.InvokeCommandAsync("x"));
    }

    [Fact]
    public async Task UnregisteringACommandRemovesIt()
    {
        var (host, _, dir) = Build();
        using var _d = dir;
        host.RegisterCommand(new HostCommandInfo { Id = "x" }, _ => Task.CompletedTask);

        host.UnregisterCommand("x");
        host.UnregisterCommand("never-registered");

        Assert.Empty(host.GetCommands());
        Assert.False(await host.InvokeCommandAsync("x"));
    }

    // ── Post-export checks ──

    private sealed class StubProcessor : IExportPostProcessor
    {
        public IReadOnlyList<string> Formats { get; init; } = [];
        public string DisplayName { get; init; } = "Stub";
        public ExportCheckResult Result { get; init; } = new();
        public Exception? Throws { get; init; }
        public int Calls { get; private set; }

        public Task<ExportCheckResult> CheckAsync(
            string outputPath, string formatKey, CancellationToken cancellationToken = default)
        {
            Calls++;
            if (Throws != null) throw Throws;
            return Task.FromResult(Result);
        }
    }

    [Fact]
    public async Task APostExportCheckRunsForItsFormatAndNotForOthers()
    {
        var (host, _, dir) = Build();
        using var _d = dir;
        var epubOnly = new StubProcessor { Formats = ["Epub"], DisplayName = "EPUB check" };
        var everything = new StubProcessor { DisplayName = "Any format" };
        host.RegisterExportPostProcessor(epubOnly);
        host.RegisterExportPostProcessor(everything);

        var results = await host.RunExportChecksAsync("out.pdf", "Pdf");

        Assert.Equal("Any format", Assert.Single(results).Name);
        Assert.Equal(0, epubOnly.Calls);
        Assert.Equal(1, everything.Calls);
    }

    [Fact]
    public async Task APostExportCheckReportsWhatItFound()
    {
        var (host, _, dir) = Build();
        using var _d = dir;
        host.RegisterExportPostProcessor(new StubProcessor
        {
            Formats = ["Epub"],
            DisplayName = "EPUB check",
            Result = new ExportCheckResult
            {
                Ok = false,
                Problems = ["Missing cover"],
                Notes = ["312 pages"]
            }
        });

        var (name, result) = Assert.Single(await host.RunExportChecksAsync("out.epub", "epub"));

        Assert.Equal("EPUB check", name);
        Assert.False(result.Ok);
        Assert.Equal("Missing cover", Assert.Single(result.Problems));
        Assert.Equal("312 pages", Assert.Single(result.Notes));
    }

    [Fact]
    public async Task ACheckThatThrowsIsAFailedCheckAndNotAFailedExport()
    {
        // The file is already written and is probably fine; a broken validator
        // should not look like a broken export.
        var (host, _, dir) = Build();
        using var _d = dir;
        host.RegisterExportPostProcessor(new StubProcessor
        {
            DisplayName = "Broken",
            Throws = new InvalidOperationException("boom")
        });

        var (name, result) = Assert.Single(await host.RunExportChecksAsync("out.epub", "Epub"));

        Assert.Equal("Broken", name);
        Assert.False(result.Ok);
        Assert.Equal(nameof(InvalidOperationException), Assert.Single(result.Problems));
    }

    [Fact]
    public async Task PostExportProcessorsCanBeRemovedAndAreNotAddedTwice()
    {
        var (host, _, dir) = Build();
        using var _d = dir;
        var processor = new StubProcessor();

        host.RegisterExportPostProcessor(processor);
        host.RegisterExportPostProcessor(processor);
        host.RegisterExportPostProcessor(null!);
        Assert.Single(await host.RunExportChecksAsync("out.epub", "Epub"));

        host.UnregisterExportPostProcessor(processor);
        Assert.Empty(await host.RunExportChecksAsync("out.epub", "Epub"));
    }

    /// <summary>The project behind a host, for tests that need to read it back
    /// through a second service instance.</summary>
    private static IProjectService GetProject(HostServices host)
        => (IProjectService)typeof(HostServices)
            .GetField("_projectService", System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Instance)!
            .GetValue(host)!;
}
