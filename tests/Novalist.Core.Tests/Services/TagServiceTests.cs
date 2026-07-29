using NSubstitute;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Xunit;

namespace Novalist.Core.Tests.Services;

/// <summary>
/// One tag vocabulary across scenes, Codex entries and research notes.
///
/// The failure this guards against is the one the three separate tag lists
/// already had: a rename that fixed the scenes and left the Codex spelling the
/// old way, which reads as two tags and is impossible to notice.
/// </summary>
public class TagServiceTests
{
    private readonly IProjectService _project = Substitute.For<IProjectService>();
    private readonly IEntityService _entities = Substitute.For<IEntityService>();
    private readonly ProjectMetadata _metadata = new();
    private readonly TagService _sut;

    private readonly SceneData _scene = new()
    {
        Id = "s1",
        AnalysisOverrides = new SceneAnalysisOverrides { Tags = ["flashback", "night"] }
    };

    private readonly CharacterData _character = new()
    {
        Id = "c1", Name = "Mira", Tags = ["flashback"]
    };

    public TagServiceTests()
    {
        var chapter = new ChapterData { Guid = "ch1" };
        _project.CurrentProject.Returns(_metadata);
        _project.GetChaptersOrdered().Returns([chapter]);
        _project.GetScenesForChapter("ch1").Returns([_scene]);

        _entities.LoadCharactersAsync().Returns(_ => Task.FromResult(new List<CharacterData> { _character }));
        _entities.LoadLocationsAsync().Returns([]);
        _entities.LoadItemsAsync().Returns([]);
        _entities.LoadLoreAsync().Returns([]);
        _entities.GetCustomEntityTypes().Returns([]);

        _metadata.ResearchItems.Add(new ResearchItem { Id = "r1", Tags = ["night"] });
        _sut = new TagService(_project, _entities);
    }

    [Fact]
    public async Task ListCountsEveryKindOfThingSeparately()
    {
        var tags = await _sut.ListAsync();

        Assert.Equal(["flashback", "night"], tags.Select(t => t.Name));
        var flashback = tags[0];
        Assert.Equal(1, flashback.Scenes);
        Assert.Equal(1, flashback.Entities);
        Assert.Equal(0, flashback.Research);
        Assert.Equal(2, flashback.Total);
        Assert.Equal(1, tags[1].Research);
    }

    [Fact]
    public async Task ATagThatOnlyHasAColourIsStillInTheList()
    {
        await _sut.SetColorAsync("planned", "#ff0000");

        var planned = (await _sut.ListAsync()).Single(t => t.Name == "planned");
        Assert.Equal("#ff0000", planned.Color);
        Assert.Equal(0, planned.Total);
    }

    [Fact]
    public async Task SettingAColourTwiceReplacesItRatherThanAddingATag()
    {
        await _sut.SetColorAsync("night", "#001133");
        await _sut.SetColorAsync("night", "#224466");

        Assert.Single(_metadata.Tags, t => t.Name == "night");
        Assert.Equal("#224466", (await _sut.ListAsync()).Single(t => t.Name == "night").Color);
    }

    [Fact]
    public async Task ABlankTagNameIsNotAColourToSet()
    {
        await _sut.SetColorAsync("   ", "#ff0000");
        Assert.Empty(_metadata.Tags);
    }

    [Fact]
    public async Task RenamingReachesEveryHolder()
    {
        var changed = await _sut.RenameAsync("flashback", "memory");

        Assert.Equal(2, changed);
        Assert.Contains("memory", _scene.AnalysisOverrides!.Tags!);
        Assert.DoesNotContain("flashback", _scene.AnalysisOverrides.Tags!);
        Assert.Equal(["memory"], _character.Tags);
        await _entities.Received().SaveCharacterAsync(_character);
    }

    [Fact]
    public async Task RenamingCarriesTheColourEntryAcross()
    {
        await _sut.SetColorAsync("flashback", "#123456");

        await _sut.RenameAsync("flashback", "memory");

        Assert.Equal("#123456", (await _sut.ListAsync()).Single(t => t.Name == "memory").Color);
    }

    [Fact]
    public async Task RenamingOntoAnExistingTagMergesThem()
    {
        await _sut.RenameAsync("flashback", "night");

        // The scene had both, and now carries one of them rather than two.
        Assert.Equal(["night"], _scene.AnalysisOverrides!.Tags);
        Assert.Equal(["night"], _character.Tags);
        Assert.Single(await _sut.ListAsync());
    }

    [Fact]
    public async Task MergingOntoAColouredTagKeepsTheColourItAlreadyHad()
    {
        await _sut.SetColorAsync("flashback", "#111111");
        await _sut.SetColorAsync("night", "#222222");

        await _sut.RenameAsync("flashback", "night");

        var tags = await _sut.ListAsync();
        Assert.Equal("#222222", tags.Single(t => t.Name == "night").Color);
        Assert.DoesNotContain(tags, t => t.Name == "flashback");
    }

    [Theory]
    [InlineData("", "memory")]
    [InlineData("flashback", "  ")]
    // Renaming to the same thing, in any casing, is not a change.
    [InlineData("flashback", "FLASHBACK")]
    public async Task ARenameThatChangesNothingDoesNothing(string from, string to)
    {
        Assert.Equal(0, await _sut.RenameAsync(from, to));
        Assert.Contains("flashback", _scene.AnalysisOverrides!.Tags!);
    }

    [Fact]
    public async Task DeletingTakesTheTagOffEverything()
    {
        await _sut.SetColorAsync("flashback", "#123456");

        var changed = await _sut.DeleteAsync("flashback");

        Assert.Equal(2, changed);
        Assert.Equal(["night"], _scene.AnalysisOverrides!.Tags);
        Assert.Empty(_character.Tags);
        Assert.DoesNotContain(await _sut.ListAsync(), t => t.Name == "flashback");
    }

    [Fact]
    public async Task DeletingNothingIsNotAChange()
        => Assert.Equal(0, await _sut.DeleteAsync("   "));

    [Fact]
    public async Task ASceneWithNoTagsAtAllIsLeftAlone()
    {
        _scene.AnalysisOverrides = null;

        Assert.Equal(1, await _sut.RenameAsync("flashback", "memory"));
        Assert.Null(_scene.AnalysisOverrides);
    }

    [Fact]
    public async Task CustomEntityTypesAreWalkedToo()
    {
        var custom = new CustomEntityData { Id = "x1", Name = "Guild", Tags = ["flashback"] };
        _entities.GetCustomEntityTypes()
            .Returns([new CustomEntityTypeDefinition { TypeKey = "faction" }]);
        _entities.LoadCustomEntitiesAsync("faction")
            .Returns(_ => Task.FromResult(new List<CustomEntityData> { custom }));

        // Counted as an entry before the rename, and rewritten by it.
        Assert.Equal(2, (await _sut.ListAsync()).Single(t => t.Name == "flashback").Entities);

        await _sut.RenameAsync("flashback", "memory");

        Assert.Equal(["memory"], custom.Tags);
        await _entities.Received().SaveCustomEntityAsync(custom);
    }

    [Fact]
    public async Task LocationsItemsAndLoreAreWalkedToo()
    {
        var location = new LocationData { Id = "l1", Name = "Ashport", Tags = ["flashback"] };
        var item = new ItemData { Id = "i1", Name = "Rope", Tags = ["flashback"] };
        var lore = new LoreData { Id = "o1", Name = "The Rite", Tags = ["flashback"] };
        _entities.LoadLocationsAsync().Returns(_ => Task.FromResult(new List<LocationData> { location }));
        _entities.LoadItemsAsync().Returns(_ => Task.FromResult(new List<ItemData> { item }));
        _entities.LoadLoreAsync().Returns(_ => Task.FromResult(new List<LoreData> { lore }));

        await _sut.RenameAsync("flashback", "memory");

        Assert.Equal(["memory"], location.Tags);
        Assert.Equal(["memory"], item.Tags);
        Assert.Equal(["memory"], lore.Tags);
    }

    [Fact]
    public async Task ResearchNotesAreRewrittenToo()
    {
        var changed = await _sut.RenameAsync("night", "dark");

        // The scene and the research note both carried it.
        Assert.Equal(2, changed);
        Assert.Equal(["dark"], _metadata.ResearchItems[0].Tags);
        Assert.Contains("dark", _scene.AnalysisOverrides!.Tags!);
    }

    [Fact]
    public async Task WithNoProjectOpenThereIsNothingToList()
    {
        _project.CurrentProject.Returns((ProjectMetadata?)null);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.ListAsync());
    }
}
