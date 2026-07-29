using NSubstitute;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Xunit;

namespace Novalist.Core.Tests.Services;

public class GlobalSearchServiceTests
{
    private static (GlobalSearchService Sut, IProjectService Project, IEntityService Entity,
        IResearchService Research) Build()
    {
        var project = Substitute.For<IProjectService>();
        var entity = Substitute.For<IEntityService>();
        var research = Substitute.For<IResearchService>();

        project.GetChaptersOrdered().Returns(new List<ChapterData>());
        project.ProjectSettings.Returns(new ProjectSettings());
        entity.LoadCharactersAsync().Returns(new List<CharacterData>());
        entity.LoadLocationsAsync().Returns(new List<LocationData>());
        entity.LoadItemsAsync().Returns(new List<ItemData>());
        entity.LoadLoreAsync().Returns(new List<LoreData>());
        entity.GetCustomEntityTypes().Returns(new List<CustomEntityTypeDefinition>());
        research.GetAll().Returns(new List<ResearchItem>());

        return (new GlobalSearchService(project, entity, research), project, entity, research);
    }

    /// <summary>Wires one chapter with one scene, whose prose is the given html.</summary>
    private static void WithScene(
        IProjectService project, SceneData scene, string html = "<p></p>", string chapterTitle = "Chapter One")
    {
        var chapter = new ChapterData { Guid = "c1", Title = chapterTitle };
        project.GetChaptersOrdered().Returns(new List<ChapterData> { chapter });
        project.GetScenesForChapter("c1").Returns(new List<SceneData> { scene });
        project.ReadSceneContentAsync(chapter, scene).Returns(html);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Search_BlankQuery_ReturnsNothing(string query)
    {
        var (sut, _, _, _) = Build();
        Assert.Empty(await sut.SearchAsync(query));
    }

    [Fact]
    public async Task Search_MatchesSceneTitle()
    {
        var (sut, project, _, _) = Build();
        WithScene(project, new SceneData { Id = "s1", Title = "The Frost Oath" });

        var hit = Assert.Single(await sut.SearchAsync("frost"), h => h.Kind == GlobalSearchKinds.Scene);
        Assert.Equal("The Frost Oath", hit.Title);
        Assert.Equal("Chapter One", hit.Subtitle);
        Assert.Equal("c1", hit.ChapterGuid);
        Assert.Equal("s1", hit.SceneId);
    }

    [Fact]
    public async Task Search_MatchesSceneProse_WithSnippet()
    {
        var (sut, project, _, _) = Build();
        WithScene(project, new SceneData { Id = "s1", Title = "Arrival" },
            "<p>They spoke of the frost oath in whispers.</p>");

        var hit = Assert.Single(await sut.SearchAsync("frost oath"));
        Assert.Equal(GlobalSearchKinds.SceneText, hit.Kind);
        Assert.Contains("frost oath", hit.Snippet);
        Assert.Equal("s1", hit.SceneId);
    }

    [Fact]
    public async Task Search_MatchesSynopsisAndNotes()
    {
        var (sut, project, _, _) = Build();
        WithScene(project, new SceneData
        {
            Id = "s1", Title = "Arrival",
            Synopsis = "Mira swears the frost oath.",
            Notes = "Check the frost imagery here."
        });

        var notes = (await sut.SearchAsync("frost"))
            .Where(h => h.Kind == GlobalSearchKinds.SceneNote)
            .ToList();
        Assert.Equal(2, notes.Count);   // synopsis and notes are separate hits
    }

    [Fact]
    public async Task Search_MatchesCommentsAndFootnotes()
    {
        var (sut, project, _, _) = Build();
        WithScene(project, new SceneData
        {
            Id = "s1", Title = "Arrival",
            Comments = [new SceneComment { Id = "k1", AnchorText = "oath", Text = "Tighten this vow." }],
            Footnotes = [new SceneFootnote { Id = "f1", Number = 1, Text = "The vow is binding." }]
        });

        var hits = (await sut.SearchAsync("vow"))
            .Where(h => h.Kind == GlobalSearchKinds.Annotation)
            .ToList();
        Assert.Equal(2, hits.Count);
        Assert.All(hits, h => Assert.Equal("s1", h.SceneId));
    }

    [Fact]
    public async Task Search_MatchesCommentAnchorText()
    {
        var (sut, project, _, _) = Build();
        WithScene(project, new SceneData
        {
            Id = "s1", Title = "Arrival",
            Comments = [new SceneComment { Id = "k1", AnchorText = "frostfall", Text = "note" }]
        });

        Assert.Single(await sut.SearchAsync("frostfall"), h => h.Kind == GlobalSearchKinds.Annotation);
    }

    [Fact]
    public async Task Search_MatchesEntityName_AliasAndFields()
    {
        var (sut, _, entity, _) = Build();
        entity.LoadCharactersAsync().Returns(new List<CharacterData>
        {
            new() { Id = "c1", Name = "Mira", Surname = "Vane" },
            new() { Id = "c2", Name = "Corin", Aliases = { "The Grey" } },
            new() { Id = "c3", Name = "Bren", Role = "Harbourmaster" }
        });

        var byName = Assert.Single(await sut.SearchAsync("Vane"));
        Assert.Equal("Mira Vane", byName.Title);
        Assert.Equal("character", byName.EntityTypeKey);
        Assert.Equal("c1", byName.EntityId);

        Assert.Equal("c2", Assert.Single(await sut.SearchAsync("the grey")).EntityId);
        Assert.Equal("c3", Assert.Single(await sut.SearchAsync("harbourmaster")).EntityId);
    }

    [Fact]
    public async Task Search_MatchesEntitySectionsAndCustomProperties()
    {
        var (sut, _, entity, _) = Build();
        entity.LoadLoreAsync().Returns(new List<LoreData>
        {
            new()
            {
                Id = "l1", Name = "Frostbinding",
                Sections = { new EntitySection { Title = "Origins", Content = "<p>Sworn at the glacier.</p>" } }
            }
        });
        entity.LoadItemsAsync().Returns(new List<ItemData>
        {
            new() { Id = "i1", Name = "Ring", CustomProperties = { ["Maker"] = "Dwarven smiths" } }
        });

        var section = Assert.Single(await sut.SearchAsync("glacier"));
        Assert.Equal("l1", section.EntityId);
        Assert.Contains("glacier", section.Snippet);

        Assert.Equal("i1", Assert.Single(await sut.SearchAsync("dwarven")).EntityId);
    }

    [Fact]
    public async Task Search_MatchesLocationsAndCustomEntities()
    {
        var (sut, _, entity, _) = Build();
        entity.LoadLocationsAsync().Returns(new List<LocationData>
        {
            new() { Id = "loc1", Name = "Harbour", Description = "A bustling port." }
        });
        entity.GetCustomEntityTypes().Returns(new List<CustomEntityTypeDefinition>
        {
            new() { TypeKey = "faction", DisplayName = "Faction" }
        });
        entity.LoadCustomEntitiesAsync("faction").Returns(new List<CustomEntityData>
        {
            new() { Id = "f1", EntityTypeKey = "faction", Name = "Grey Order",
                Fields = { ["Creed"] = "Hold the line" } }
        });

        Assert.Equal("loc1", Assert.Single(await sut.SearchAsync("bustling")).EntityId);
        var custom = Assert.Single(await sut.SearchAsync("hold the line"));
        Assert.Equal("f1", custom.EntityId);
        Assert.Equal("faction", custom.EntityTypeKey);
    }

    [Fact]
    public async Task Search_MatchesResearchTitleContentAndTags()
    {
        var (sut, _, _, research) = Build();
        research.GetAll().Returns(new List<ResearchItem>
        {
            new() { Id = "r1", Title = "Sailing notes", Content = "Rigging and knots.", Type = ResearchItemType.Note },
            new() { Id = "r2", Title = "Other", Tags = { "medieval" } }
        });

        var byContent = Assert.Single(await sut.SearchAsync("rigging"));
        Assert.Equal("r1", byContent.ResearchId);
        Assert.Equal(GlobalSearchKinds.Research, byContent.Kind);
        Assert.Contains("Rigging", byContent.Snippet);

        var byTag = Assert.Single(await sut.SearchAsync("medieval"));
        Assert.Equal("r2", byTag.ResearchId);
        Assert.Null(byTag.Snippet);   // matched the tag, not the body
    }

    [Fact]
    public async Task Search_MatchesTimelineEvents()
    {
        var (sut, project, _, _) = Build();
        var settings = new ProjectSettings();
        settings.Timeline.ManualEvents.Add(new TimelineManualEvent
        {
            Id = "e1", Title = "The Comet", Date = "1043-03-01", Description = "An omen over the harbour."
        });
        project.ProjectSettings.Returns(settings);

        var byTitle = Assert.Single(await sut.SearchAsync("comet"));
        Assert.Equal(GlobalSearchKinds.Timeline, byTitle.Kind);
        Assert.Equal("1043-03-01", byTitle.Subtitle);
        Assert.Null(byTitle.Snippet);

        var byDescription = Assert.Single(await sut.SearchAsync("omen"));
        Assert.Contains("omen", byDescription.Snippet);
    }

    [Fact]
    public async Task Search_NoTimelineSettings_IsSkipped()
    {
        var (sut, project, _, _) = Build();
        project.ProjectSettings.Returns((ProjectSettings?)null!);
        Assert.Empty(await sut.SearchAsync("anything"));
    }

    [Fact]
    public async Task Search_HonoursPerKindLimit()
    {
        var (sut, _, entity, _) = Build();
        entity.LoadCharactersAsync().Returns(Enumerable.Range(0, 10)
            .Select(i => new CharacterData { Id = $"c{i}", Name = $"Frost {i}" })
            .ToList());

        Assert.Equal(3, (await sut.SearchAsync("frost", limit: 3)).Count);
    }

    [Fact]
    public async Task Search_LongProse_SnippetIsEllipsisedOnBothSides()
    {
        var (sut, project, _, _) = Build();
        var filler = string.Join(' ', Enumerable.Repeat("word", 60));
        WithScene(project, new SceneData { Id = "s1", Title = "Long" },
            $"<p>{filler} needle {filler}</p>");

        var hit = Assert.Single(await sut.SearchAsync("needle"));
        Assert.StartsWith("...", hit.Snippet);
        Assert.EndsWith("...", hit.Snippet);
    }

    [Fact]
    public async Task Search_Cancellation_Throws()
    {
        var (sut, project, _, _) = Build();
        WithScene(project, new SceneData { Id = "s1", Title = "Arrival" });
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => sut.SearchAsync("anything", 20, cts.Token));
    }

    // -- Structured queries --

    [Fact]
    public async Task Search_ScopesATermToTheTitle()
    {
        var (sut, project, _, _) = Build();
        WithScene(project, new SceneData { Id = "s1", Title = "Arrival" }, "<p>the bell tolled</p>");

        Assert.Empty(await sut.SearchAsync("title:bell"));
        Assert.NotEmpty(await sut.SearchAsync("text:bell"));
    }

    [Fact]
    public async Task Search_ANegatedTermExcludesTheScene()
    {
        var (sut, project, _, _) = Build();
        WithScene(project, new SceneData { Id = "s1", Title = "Arrival" }, "<p>still a draft</p>");

        Assert.Empty(await sut.SearchAsync("draft -draft"));
    }

    [Fact]
    public async Task Search_AQueryOfNothingButANegationStillFindsWhatIsLeft()
    {
        var (sut, project, _, _) = Build();
        WithScene(project, new SceneData { Id = "s1", Title = "Arrival" }, "<p>finished prose</p>");

        // Nothing positive to snippet, so the scene is reported by its title.
        var hit = Assert.Single(await sut.SearchAsync("-draft"));
        Assert.Equal(GlobalSearchKinds.Scene, hit.Kind);
        Assert.Equal("Arrival", hit.Title);
    }

    [Fact]
    public async Task Search_AKindTermKeepsTheOtherKindsOut()
    {
        var (sut, project, entity, _) = Build();
        WithScene(project, new SceneData { Id = "s1", Title = "Bell" });
        entity.LoadCharactersAsync().Returns(new List<CharacterData>
        {
            new() { Id = "e1", Name = "Bell" }
        });

        var scenesOnly = await sut.SearchAsync("kind:scene Bell");

        Assert.All(scenesOnly, h => Assert.NotEqual(GlobalSearchKinds.Entity, h.Kind));
        Assert.Contains(await sut.SearchAsync("Bell"), h => h.Kind == GlobalSearchKinds.Entity);
    }

    [Fact]
    public async Task Search_ATitleMatchIsRankedAboveABodyMatch()
    {
        var (sut, project, _, _) = Build();
        var chapter = new ChapterData { Guid = "c1", Title = "One" };
        var titled = new SceneData { Id = "s1", Title = "The bell" };
        var mentioned = new SceneData { Id = "s2", Title = "Arrival" };
        project.GetChaptersOrdered().Returns(new List<ChapterData> { chapter });
        project.GetScenesForChapter("c1").Returns(new List<SceneData> { mentioned, titled });
        project.ReadSceneContentAsync(chapter, titled).Returns("<p>nothing</p>");
        project.ReadSceneContentAsync(chapter, mentioned).Returns("<p>the bell tolled</p>");

        var hits = await sut.SearchAsync("bell");

        Assert.Equal("The bell", hits[0].Title);
    }
}
