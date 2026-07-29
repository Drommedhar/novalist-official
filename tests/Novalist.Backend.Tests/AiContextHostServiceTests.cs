using NSubstitute;
using Novalist.Backend.Extensions;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Sdk.Services;
using Xunit;

namespace Novalist.Backend.Tests;

/// <summary>
/// The seam an extension is expected to assemble its model context from.
///
/// This exists so honouring the writer's setting is the easy path. An extension
/// that reads the raw entity lists still sees everything — the point of putting
/// the filter in the host is that the obvious call already does the right thing.
/// </summary>
[Collection("BackendStatics")]
public class AiContextHostServiceTests
{
    private readonly IProjectService _proj = Substitute.For<IProjectService>();
    private readonly IEntityService _ent = Substitute.For<IEntityService>();
    private readonly ChapterData _chapter = new() { Title = "One", Order = 1 };
    private readonly SceneData _scene;

    public AiContextHostServiceTests()
    {
        _scene = new SceneData { Title = "S", Order = 1, ChapterGuid = _chapter.Guid };
        _proj.GetChaptersOrdered().Returns([_chapter]);
        _proj.GetScenesForChapter(_chapter.Guid).Returns([_scene]);
        _ent.LoadCharactersAsync().Returns([]);
        _ent.LoadLocationsAsync().Returns([]);
        _ent.LoadItemsAsync().Returns([]);
        _ent.LoadLoreAsync().Returns([]);
        _ent.GetCustomEntityTypes().Returns([]);
    }

    private IExtensionEntityService Host()
    {
        var settings = Substitute.For<ISettingsService>();
        settings.Settings.Returns(new AppSettings());
        return new HostServices(Substitute.For<IFileService>(), _proj, _ent, settings);
    }

    private void SceneSays(string prose)
        => _proj.ReadSceneContentAsync(_chapter, _scene).Returns($"<p>{prose}</p>");

    private void CharactersAre(params CharacterData[] characters)
        => _ent.LoadCharactersAsync().Returns(characters.ToList());

    private Task<IReadOnlyList<AiContextEntryInfo>> ContextAsync()
        => Host().GetAiContextAsync(_chapter.Guid, _scene.Id);

    [Fact]
    public async Task ACharacterTheSceneNamesComesThrough()
    {
        SceneSays("Rose opened the door.");
        CharactersAre(new CharacterData { Id = "c1", Name = "Rose" });

        Assert.Equal(["Rose"], (await ContextAsync()).Select(e => e.Name));
    }

    [Fact]
    public async Task ACharacterTheSceneNeverNamesStaysOut()
    {
        SceneSays("Nobody in particular.");
        CharactersAre(new CharacterData { Id = "c1", Name = "Rose" });

        Assert.Empty(await ContextAsync());
    }

    [Fact]
    public async Task AnAlwaysEntryComesThroughUnmentioned()
    {
        SceneSays("Nobody in particular.");
        CharactersAre(new CharacterData { Id = "c1", Name = "Rose", Ai = AiInclusion.Always });

        Assert.Equal(["Rose"], (await ContextAsync()).Select(e => e.Name));
    }

    [Fact]
    public async Task ANeverEntryStaysOutOfASceneAboutIt()
    {
        // The promise the setting makes, at the seam that has to keep it.
        SceneSays("Rose opened the door.");
        CharactersAre(new CharacterData { Id = "c1", Name = "Rose", Ai = AiInclusion.Never });

        Assert.Empty(await ContextAsync());
    }

    [Fact]
    public async Task AWithheldSectionNeverLeavesTheHost()
    {
        SceneSays("Rose opened the door.");
        CharactersAre(new CharacterData
        {
            Id = "c1",
            Name = "Rose",
            Sections =
            [
                new EntitySection { Title = "Public", Content = "Known things." },
                new EntitySection { Title = "Twist", Content = "She is the killer.", AiHidden = true }
            ]
        });

        var entry = (await ContextAsync()).Single();

        Assert.Equal(["Public"], entry.Sections.Select(s => s.Title));
        Assert.DoesNotContain(entry.Sections, s => s.Content.Contains("killer"));
    }

    [Fact]
    public async Task TheEntryCarriesItsTypeAndWhyItIsHere()
    {
        SceneSays("Nothing.");
        CharactersAre(new CharacterData { Id = "c1", Name = "Rose", Ai = AiInclusion.Always });

        var entry = (await ContextAsync()).Single();

        Assert.Equal("character", entry.TypeKey);
        Assert.Equal("Always", entry.Inclusion);
        Assert.Equal("c1", entry.Id);
    }

    [Fact]
    public async Task ACharacterIsFoundByTheirFullName()
    {
        SceneSays("Liam Calder said nothing.");
        CharactersAre(new CharacterData { Id = "c1", Name = "Liam", Surname = "Calder" });

        Assert.Equal(["Liam Calder"], (await ContextAsync()).Select(e => e.Name));
    }

    [Fact]
    public async Task MatchingIgnoresCapitalisation()
    {
        SceneSays("the ravens gathered");
        _ent.LoadLoreAsync().Returns([new LoreData { Id = "l1", Name = "Ravens" }]);
        SceneSays("the ravens gathered");

        Assert.Equal(["Ravens"], (await ContextAsync()).Select(e => e.Name));
    }

    [Fact]
    public async Task LocationsItemsAndLoreAreAllConsidered()
    {
        SceneSays("At the Inn, holding the Blade, thinking of the Pact.");
        _ent.LoadLocationsAsync().Returns([new LocationData { Id = "lo1", Name = "Inn" }]);
        _ent.LoadItemsAsync().Returns([new ItemData { Id = "i1", Name = "Blade" }]);
        _ent.LoadLoreAsync().Returns([new LoreData { Id = "l1", Name = "Pact" }]);

        var types = (await ContextAsync()).Select(e => e.TypeKey).OrderBy(t => t);

        Assert.Equal(["item", "location", "lore"], types);
    }

    [Fact]
    public async Task CustomEntitiesAreConsideredUnderTheirOwnTypeKey()
    {
        SceneSays("The Ravens rode out.");
        _ent.GetCustomEntityTypes().Returns([new CustomEntityTypeDefinition { TypeKey = "faction", DisplayName = "Faction" }]);
        _ent.LoadCustomEntitiesAsync("faction")
            .Returns([new CustomEntityData { Id = "f1", Name = "Ravens" }]);

        var entry = (await ContextAsync()).Single();

        Assert.Equal("faction", entry.TypeKey);
    }

    [Fact]
    public async Task AnUnknownSceneYieldsNothingRatherThanThrowing()
    {
        Assert.Empty(await Host().GetAiContextAsync(_chapter.Guid, "no-such-scene"));
        Assert.Empty(await Host().GetAiContextAsync("no-such-chapter", _scene.Id));
    }

    [Fact]
    public async Task AnEntryWithNoNameIsNeverMatched()
    {
        // An empty name would substring-match every scene, dragging a blank
        // entry into every context.
        SceneSays("Anything at all.");
        CharactersAre(new CharacterData { Id = "c1", Name = "" });

        Assert.Empty(await ContextAsync());
    }
}
