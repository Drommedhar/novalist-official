using NSubstitute;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Xunit;

namespace Novalist.Core.Tests.Services;

/// <summary>
/// Renaming an entity has to reach everything that stored its display name
/// rather than its id, or the rename silently orphans those references.
/// </summary>
public class EntityRenameServiceTests
{
    private readonly IProjectService _project = Substitute.For<IProjectService>();
    private readonly IEntityService _entities = Substitute.For<IEntityService>();

    private EntityRenameService Build()
    {
        _entities.LoadCharactersAsync().Returns(new List<CharacterData>());
        _entities.LoadLocationsAsync().Returns(new List<LocationData>());
        _entities.LoadItemsAsync().Returns(new List<ItemData>());
        _entities.LoadLoreAsync().Returns(new List<LoreData>());
        _entities.GetCustomEntityTypes().Returns(new List<CustomEntityTypeDefinition>());
        _project.GetChaptersOrdered().Returns(new List<ChapterData>());
        return new EntityRenameService(_project, _entities);
    }

    [Theory]
    [InlineData("", "New")]
    [InlineData("Old", "")]
    [InlineData("   ", "New")]
    [InlineData("Old", "   ")]
    [InlineData("Same", "Same")]
    public async Task Cascade_NoOpForBlankOrUnchangedNames(string oldName, string newName)
    {
        var sut = Build();

        var report = await sut.CascadeAsync("e1", oldName, newName);

        Assert.True(report.IsEmpty);
        await _project.DidNotReceive().SyncMentionDisplayTextAsync(Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task Cascade_RewritesProseMentionsById()
    {
        var sut = Build();
        _project.SyncMentionDisplayTextAsync("e1", "Robert").Returns(3);

        var report = await sut.CascadeAsync("e1", "Bob", "Robert");

        Assert.Equal(3, report.ScenesUpdated);
        Assert.False(report.IsEmpty);
    }

    [Fact]
    public async Task Cascade_BlankEntityId_SkipsProseButStillFixesReferences()
    {
        var other = new CharacterData
        {
            Id = "c2",
            Name = "Alice",
            Relationships = [new EntityRelationship { Role = "friend", Target = "Bob" }]
        };
        _entities.LoadCharactersAsync().Returns(new List<CharacterData> { other });
        var sut = Build();
        _entities.LoadCharactersAsync().Returns(new List<CharacterData> { other });

        var report = await sut.CascadeAsync("", "Bob", "Robert");

        Assert.Equal(0, report.ScenesUpdated);
        Assert.Equal(1, report.RelationshipsUpdated);
        Assert.Equal("Robert", other.Relationships[0].Target);
    }

    [Fact]
    public async Task Cascade_RetargetsRelationshipsOnOtherCharacters()
    {
        var alice = new CharacterData
        {
            Id = "c2",
            Name = "Alice",
            Relationships =
            [
                new EntityRelationship { Role = "brother", Target = "Bob" },
                new EntityRelationship { Role = "rival", Target = "Carol" }
            ]
        };
        var sut = Build();
        _entities.LoadCharactersAsync().Returns(new List<CharacterData> { alice });

        var report = await sut.CascadeAsync("c1", "Bob", "Robert");

        Assert.Equal(1, report.RelationshipsUpdated);
        Assert.Equal("Robert", alice.Relationships[0].Target);
        Assert.Equal("Carol", alice.Relationships[1].Target);
        await _entities.Received(1).SaveCharacterAsync(alice);
    }

    [Fact]
    public async Task Cascade_LeavesUnrelatedCharactersUnsaved()
    {
        var bystander = new CharacterData { Id = "c3", Name = "Dave" };
        var sut = Build();
        _entities.LoadCharactersAsync().Returns(new List<CharacterData> { bystander });

        await sut.CascadeAsync("c1", "Bob", "Robert");

        await _entities.DidNotReceive().SaveCharacterAsync(Arg.Any<CharacterData>());
    }

    [Fact]
    public async Task Cascade_UpdatesLocationParent()
    {
        var child = new LocationData { Id = "l2", Name = "Harbour", Parent = "Old Town" };
        var sut = Build();
        _entities.LoadLocationsAsync().Returns(new List<LocationData> { child });

        var report = await sut.CascadeAsync("l1", "Old Town", "Altstadt");

        Assert.Equal(1, report.ParentsUpdated);
        Assert.Equal("Altstadt", child.Parent);
        await _entities.Received(1).SaveLocationAsync(child);
    }

    [Fact]
    public async Task Cascade_RewritesWikiLinksInSectionsAcrossEveryType()
    {
        var character = new CharacterData
        {
            Id = "c2",
            Name = "Alice",
            Sections = [new EntitySection { Title = "Notes", Content = "Met [[Bob]] at dawn." }]
        };
        var location = new LocationData
        {
            Id = "l1",
            Name = "Inn",
            Sections = [new EntitySection { Title = "Notes", Content = "Owned by [[Bob|the innkeeper]]." }]
        };
        var item = new ItemData
        {
            Id = "i1",
            Name = "Sword",
            Sections = [new EntitySection { Title = "Notes", Content = "Forged for [[Bob]]." }]
        };
        var lore = new LoreData
        {
            Id = "lo1",
            Name = "Prophecy",
            Sections = [new EntitySection { Title = "Notes", Content = "Speaks of [[Bob]]." }]
        };

        var sut = Build();
        _entities.LoadCharactersAsync().Returns(new List<CharacterData> { character });
        _entities.LoadLocationsAsync().Returns(new List<LocationData> { location });
        _entities.LoadItemsAsync().Returns(new List<ItemData> { item });
        _entities.LoadLoreAsync().Returns(new List<LoreData> { lore });

        var report = await sut.CascadeAsync("c1", "Bob", "Robert");

        Assert.Equal(4, report.SectionLinksUpdated);
        Assert.Equal("Met [[Robert]] at dawn.", character.Sections[0].Content);
        // The shown text is the author's wording and must survive untouched.
        Assert.Equal("Owned by [[Robert|the innkeeper]].", location.Sections[0].Content);
        Assert.Equal("Forged for [[Robert]].", item.Sections[0].Content);
        Assert.Equal("Speaks of [[Robert]].", lore.Sections[0].Content);
    }

    [Fact]
    public async Task Cascade_LeavesBareProseOccurrencesAlone()
    {
        // "Bob" in running prose is not a reference. Rewriting it would be an
        // unrequested edit to the author's writing.
        var character = new CharacterData
        {
            Id = "c2",
            Name = "Alice",
            Sections = [new EntitySection { Title = "Notes", Content = "Bob arrived. Then [[Bob]] left." }]
        };
        var sut = Build();
        _entities.LoadCharactersAsync().Returns(new List<CharacterData> { character });

        await sut.CascadeAsync("c1", "Bob", "Robert");

        Assert.Equal("Bob arrived. Then [[Robert]] left.", character.Sections[0].Content);
    }

    [Fact]
    public async Task Cascade_SectionWithoutLinksIsNotRewritten()
    {
        var character = new CharacterData
        {
            Id = "c2",
            Name = "Alice",
            Sections = [new EntitySection { Title = "Notes", Content = "No links here at all." }]
        };
        var sut = Build();
        _entities.LoadCharactersAsync().Returns(new List<CharacterData> { character });

        var report = await sut.CascadeAsync("c1", "Bob", "Robert");

        Assert.Equal(0, report.SectionLinksUpdated);
        await _entities.DidNotReceive().SaveCharacterAsync(Arg.Any<CharacterData>());
    }

    [Fact]
    public async Task Cascade_UpdatesCustomEntityRelationshipsAndSections()
    {
        var faction = new CustomEntityData
        {
            Id = "f1",
            Name = "Guild",
            EntityTypeKey = "faction",
            Relationships = [new EntityRelationship { Role = "leader", Target = "Bob" }],
            Sections = [new EntitySection { Title = "Notes", Content = "Founded by [[Bob]]." }]
        };
        var sut = Build();
        _entities.GetCustomEntityTypes().Returns(
            new List<CustomEntityTypeDefinition> { new() { TypeKey = "faction" } });
        _entities.LoadCustomEntitiesAsync("faction").Returns(new List<CustomEntityData> { faction });

        var report = await sut.CascadeAsync("c1", "Bob", "Robert");

        Assert.Equal(1, report.RelationshipsUpdated);
        Assert.Equal(1, report.SectionLinksUpdated);
        await _entities.Received(1).SaveCustomEntityAsync(faction);
    }

    [Fact]
    public async Task Cascade_UpdatesPovOverridesAndSavesOnce()
    {
        var chapter = new ChapterData { Guid = "ch1" };
        var povScene = new SceneData { Id = "s1", AnalysisOverrides = new SceneAnalysisOverrides { Pov = "Bob" } };
        var otherScene = new SceneData { Id = "s2", AnalysisOverrides = new SceneAnalysisOverrides { Pov = "Alice" } };
        var noOverride = new SceneData { Id = "s3" };

        var sut = Build();
        _project.GetChaptersOrdered().Returns(new List<ChapterData> { chapter });
        _project.GetScenesForChapter("ch1").Returns(new List<SceneData> { povScene, otherScene, noOverride });

        var report = await sut.CascadeAsync("c1", "Bob", "Robert");

        Assert.Equal(1, report.PovOverridesUpdated);
        Assert.Equal("Robert", povScene.AnalysisOverrides!.Pov);
        Assert.Equal("Alice", otherScene.AnalysisOverrides!.Pov);
        await _project.Received(1).SaveScenesAsync();
    }

    [Fact]
    public async Task Cascade_NoPovMatches_DoesNotSaveScenes()
    {
        var chapter = new ChapterData { Guid = "ch1" };
        var scene = new SceneData { Id = "s1", AnalysisOverrides = new SceneAnalysisOverrides { Pov = "Alice" } };
        var sut = Build();
        _project.GetChaptersOrdered().Returns(new List<ChapterData> { chapter });
        _project.GetScenesForChapter("ch1").Returns(new List<SceneData> { scene });

        await sut.CascadeAsync("c1", "Bob", "Robert");

        await _project.DidNotReceive().SaveScenesAsync();
    }

    [Fact]
    public async Task Cascade_NullCollectionsAreTolerated()
    {
        var character = new CharacterData { Id = "c2", Name = "Alice", Relationships = null!, Sections = null! };
        var sut = Build();
        _entities.LoadCharactersAsync().Returns(new List<CharacterData> { character });

        var report = await sut.CascadeAsync("c1", "Bob", "Robert");

        Assert.Equal(0, report.RelationshipsUpdated);
        Assert.Equal(0, report.SectionLinksUpdated);
    }

    [Fact]
    public async Task Cascade_WikiLinkMatchIsCaseInsensitiveAndToleratesPadding()
    {
        var character = new CharacterData
        {
            Id = "c2",
            Name = "Alice",
            Sections = [new EntitySection { Title = "N", Content = "See [[ bob ]] and [[BOB|him]]." }]
        };
        var sut = Build();
        _entities.LoadCharactersAsync().Returns(new List<CharacterData> { character });

        await sut.CascadeAsync("c1", "Bob", "Robert");

        Assert.Equal("See [[Robert]] and [[Robert|him]].", character.Sections[0].Content);
    }
}
