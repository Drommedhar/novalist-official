using NSubstitute;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Xunit;

namespace Novalist.Core.Tests.Services;

/// <summary>
/// Where things sit across the whole book.
///
/// Novalist computed POV and mentions per scene and only ever showed them for
/// the scene in view, so the two questions these answer - which character is
/// this book about, and what have I quietly dropped - had no answer anywhere.
/// </summary>
public class BookAnalyticsServiceTests
{
    private readonly IProjectService _projects = Substitute.For<IProjectService>();
    private readonly IEntityService _entities = Substitute.For<IEntityService>();
    private readonly List<ChapterData> _chapters = [];

    public BookAnalyticsServiceTests()
    {
        _projects.GetChaptersOrdered().Returns(_ => _chapters);
        _entities.LoadCharactersAsync().Returns([]);
        _entities.LoadLocationsAsync().Returns([]);
    }

    private BookAnalyticsService Sut => new(_projects, _entities);

    /// <summary>A chapter whose scenes carry the given prose.</summary>
    private ChapterData Chapter(string title, string act, params (string Title, string Html, string Pov, int Words)[] scenes)
    {
        var chapter = new ChapterData { Title = title, Act = act, Order = _chapters.Count + 1 };
        var built = new List<SceneData>();
        for (var i = 0; i < scenes.Length; i++)
        {
            var scene = new SceneData
            {
                Title = scenes[i].Title,
                Order = i + 1,
                ChapterGuid = chapter.Guid,
                WordCount = scenes[i].Words,
                AnalysisOverrides = scenes[i].Pov.Length > 0
                    ? new SceneAnalysisOverrides { Pov = scenes[i].Pov }
                    : null
            };
            _projects.ReadSceneContentAsync(chapter, scene).Returns(scenes[i].Html);
            built.Add(scene);
        }
        _projects.GetScenesForChapter(chapter.Guid).Returns(built);
        _chapters.Add(chapter);
        return chapter;
    }

    /// <summary>The markup a confirmed @-mention leaves in the prose.</summary>
    private static string Mention(string entityId, string text = "Rose")
        => $"<p><span class=\"nv-entity-mention\" data-entity-id=\"{entityId}\">{text}</span> went in.</p>";

    [Fact]
    public async Task ABookWithNoChaptersReportsNothing()
    {
        var result = await Sut.ComputeAsync();

        Assert.Empty(result.ChapterTitles);
        Assert.Empty(result.Pov);
    }

    // ── POV ──

    [Fact]
    public async Task PovIsRankedCommonestFirstWithItsShare()
    {
        Chapter("One", "Act One",
            ("A", "<p>x</p>", "Rose", 100),
            ("B", "<p>x</p>", "Rose", 100),
            ("C", "<p>x</p>", "Liam", 100));

        var pov = (await Sut.ComputeAsync()).Pov;

        Assert.Equal("Rose", pov[0].Key);
        Assert.Equal(2, pov[0].SceneCount);
        Assert.Equal(67, pov[0].Percent);
        Assert.Equal("Liam", pov[1].Key);
    }

    [Fact]
    public async Task ScenesWithNoPovAreReportedRatherThanDropped()
    {
        // "How much of this book has no POV set" is worth being told.
        Chapter("One", "", ("A", "<p>x</p>", "", 100));

        Assert.Contains((await Sut.ComputeAsync()).Pov, p => p.Key.Length == 0);
    }

    [Fact]
    public async Task ShareIsByScenesRatherThanWords()
    {
        // A single long scene should not read as dominance: "how much of the
        // book is in her POV" is a question about how often.
        Chapter("One", "",
            ("A", "<p>x</p>", "Rose", 10_000),
            ("B", "<p>x</p>", "Liam", 100),
            ("C", "<p>x</p>", "Liam", 100));

        var pov = (await Sut.ComputeAsync()).Pov;

        Assert.Equal("Liam", pov[0].Key);
        Assert.Equal(67, pov[0].Percent);
    }

    // ── Acts ──

    [Fact]
    public async Task ScenesAreCountedPerAct()
    {
        Chapter("One", "Act One", ("A", "<p>x</p>", "", 100));
        Chapter("Two", "Act Two", ("B", "<p>x</p>", "", 100), ("C", "<p>x</p>", "", 100));

        var acts = (await Sut.ComputeAsync()).Acts;

        Assert.Equal("Act Two", acts[0].Key);
        Assert.Equal(2, acts[0].SceneCount);
    }

    // ── Presence ──

    [Fact]
    public async Task ACharacterIsCountedInEveryChapterTheyAppearIn()
    {
        _entities.LoadCharactersAsync().Returns([new CharacterData { Id = "c1", Name = "Rose" }]);
        Chapter("One", "", ("A", Mention("c1"), "", 100));
        Chapter("Two", "", ("B", "<p>nobody</p>", "", 100));
        Chapter("Three", "", ("C", Mention("c1"), "", 100), ("D", Mention("c1"), "", 100));

        var row = (await Sut.ComputeAsync()).Characters.Single();

        Assert.Equal("Rose", row.Label);
        Assert.Equal([1, 0, 2], row.ScenesPerChapter);
        Assert.Equal(3, row.TotalScenes);
    }

    [Fact]
    public async Task PresenceIsByIdRatherThanByName()
    {
        // Two characters sharing a first name would confuse a name search;
        // mention spans carry the id, which cannot be confused.
        _entities.LoadCharactersAsync().Returns([
            new CharacterData { Id = "c1", Name = "Rose", Surname = "Ward" },
            new CharacterData { Id = "c2", Name = "Rose", Surname = "Hale" }
        ]);
        Chapter("One", "", ("A", Mention("c2"), "", 100));

        var rows = (await Sut.ComputeAsync()).Characters;

        Assert.Equal("Rose Hale", rows.Single().Label);
    }

    [Fact]
    public async Task LocationsAreCountedSeparatelyFromCharacters()
    {
        _entities.LoadCharactersAsync().Returns([new CharacterData { Id = "c1", Name = "Rose" }]);
        _entities.LoadLocationsAsync().Returns([new LocationData { Id = "l1", Name = "The Inn" }]);
        Chapter("One", "", ("A", Mention("c1") + Mention("l1", "The Inn"), "", 100));

        var result = await Sut.ComputeAsync();

        Assert.Single(result.Characters);
        Assert.Single(result.Locations);
        Assert.Equal("The Inn", result.Locations.Single().Label);
    }

    [Fact]
    public async Task PresenceIsBusiestFirst()
    {
        _entities.LoadCharactersAsync().Returns([
            new CharacterData { Id = "c1", Name = "Rose" },
            new CharacterData { Id = "c2", Name = "Liam" }
        ]);
        Chapter("One", "", ("A", Mention("c1") + Mention("c2"), "", 100));
        Chapter("Two", "", ("B", Mention("c2"), "", 100));

        Assert.Equal("Liam", (await Sut.ComputeAsync()).Characters[0].Label);
    }

    [Fact]
    public async Task AMentionOfSomethingNotInTheCodexIsIgnored()
    {
        // A stale id left by a deleted entry would otherwise become a nameless
        // row.
        Chapter("One", "", ("A", Mention("deleted-long-ago"), "", 100));

        Assert.Empty((await Sut.ComputeAsync()).Characters);
    }

    // ── Unused ──

    [Fact]
    public async Task AnEntryTheManuscriptNeverMentionsIsNamed()
    {
        _entities.LoadCharactersAsync().Returns([
            new CharacterData { Id = "c1", Name = "Rose" },
            new CharacterData { Id = "c2", Name = "Forgotten" }
        ]);
        Chapter("One", "", ("A", Mention("c1"), "", 100));

        Assert.Equal(["Forgotten"], (await Sut.ComputeAsync()).Unused);
    }

    [Fact]
    public async Task UnusedCoversLocationsToo()
    {
        _entities.LoadLocationsAsync().Returns([new LocationData { Id = "l1", Name = "Never Visited" }]);
        Chapter("One", "", ("A", "<p>x</p>", "", 100));

        Assert.Contains("Never Visited", (await Sut.ComputeAsync()).Unused);
    }

    [Fact]
    public async Task NothingIsUnusedWhenEverythingIsMentioned()
    {
        _entities.LoadCharactersAsync().Returns([new CharacterData { Id = "c1", Name = "Rose" }]);
        Chapter("One", "", ("A", Mention("c1"), "", 100));

        Assert.Empty((await Sut.ComputeAsync()).Unused);
    }

    [Fact]
    public async Task AnUnnamedEntryIsNotListedAsUnused()
    {
        // A blank row in "you forgot these" is noise, not information.
        _entities.LoadCharactersAsync().Returns([new CharacterData { Id = "c1", Name = "" }]);
        Chapter("One", "", ("A", "<p>x</p>", "", 100));

        Assert.Empty((await Sut.ComputeAsync()).Unused);
    }

    [Fact]
    public async Task TheChapterTitlesLineUpWithThePresenceCounts()
    {
        _entities.LoadCharactersAsync().Returns([new CharacterData { Id = "c1", Name = "Rose" }]);
        Chapter("One", "", ("A", Mention("c1"), "", 100));
        Chapter("Two", "", ("B", "<p>x</p>", "", 100));

        var result = await Sut.ComputeAsync();

        Assert.Equal(["One", "Two"], result.ChapterTitles);
        Assert.Equal(result.ChapterTitles.Count, result.Characters.Single().ScenesPerChapter.Count);
    }
}
