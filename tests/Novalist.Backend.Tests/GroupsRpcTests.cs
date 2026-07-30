using Novalist.Backend;
using Novalist.Backend.Rpc;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Xunit;

namespace Novalist.Backend.Tests;

/// <summary>
/// Factions, houses, crews and families.
///
/// The group was a bare string on each Codex entry: it could say a house and a
/// ship belong to the Ravens and nothing else - no colour, no description, no
/// count, and no rename, so correcting "the Ravens" to "House Raven" meant
/// opening every entry that said the first thing. These tests are mostly about
/// the operations that reach into the entries.
/// </summary>
public sealed class GroupsRpcTests : IDisposable
{
    private readonly string _root;
    private readonly Workspace _workspace;
    private readonly GroupsRpc _rpc;
    private readonly EntityService _entities;

    public GroupsRpcTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "nl-grp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _workspace = new Workspace(Path.Combine(_root, "settings"));
        _workspace.Projects.CreateProjectAsync(_root, "GrpNovel", "Book").GetAwaiter().GetResult();
        _workspace.OpenProjectAsync(_workspace.Projects.ProjectRoot!).GetAwaiter().GetResult();
        _entities = new EntityService(_workspace.Projects);
        _rpc = new GroupsRpc(_workspace);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private async Task<CharacterData> CharacterAsync(string name, string group = "")
    {
        var character = new CharacterData { Name = name, Group = group };
        await _entities.SaveCharacterAsync(character);
        return character;
    }

    private async Task<LocationData> LocationAsync(string name, string group = "")
    {
        var location = new LocationData { Name = name, Group = group };
        await _entities.SaveLocationAsync(location);
        return location;
    }

    private async Task<ItemData> ItemAsync(string name, string group = "")
    {
        var item = new ItemData { Name = name, Group = group };
        await _entities.SaveItemAsync(item);
        return item;
    }

    private static EntityGroupDto New(string name, string colour = "#8b8b8b", string description = "")
        => new(string.Empty, name, colour, description, 0);

    // ── The registry ──

    [Fact]
    public async Task ABookWithNoGroupsListsNone() => Assert.Empty(await _rpc.ListAsync());

    [Fact]
    public async Task WithNoProjectOpenThereIsNothingToList()
        => Assert.Empty(await new GroupsRpc(new Workspace(Path.Combine(_root, "none"))).ListAsync());

    [Fact]
    public async Task SavingKeepsNameColourDescriptionAndOrder()
    {
        var all = await _rpc.SaveAsync(
            [New("House Raven", "#c04040", "The old northern house"), New("The Watch")]);

        Assert.Equal(["House Raven", "The Watch"], all.Select(g => g.Name));
        Assert.Equal("#c04040", all[0].Color);
        Assert.Equal("The old northern house", all[0].Description);
        Assert.All(all, g => Assert.NotEmpty(g.Id!));
    }

    [Fact]
    public async Task ANamelessGroupIsDropped()
        => Assert.Single(await _rpc.SaveAsync([New("House Raven"), New("  ")]));

    [Fact]
    public async Task TwoGroupsSpeltTheSameFoldIntoOne()
        => Assert.Single(await _rpc.SaveAsync([New("House Raven"), new(string.Empty, "house raven", null, null, 0)]));

    [Fact]
    public async Task AnEmptyColourFallsBackRatherThanDrawingNothing()
        => Assert.Equal("#8b8b8b", (await _rpc.SaveAsync([New("House Raven", "  ")]))[0].Color);

    [Fact]
    public async Task SavingNeedsABook()
    {
        var bare = new GroupsRpc(new Workspace(Path.Combine(_root, "no-project")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => bare.SaveAsync([]));
        await Assert.ThrowsAsync<InvalidOperationException>(() => bare.HarvestAsync());
    }

    [Fact]
    public async Task SavingNothingClearsTheRegistry()
    {
        await _rpc.SaveAsync([New("House Raven")]);
        Assert.Empty(await _rpc.SaveAsync(null!));
    }

    // ── Counting across types ──

    [Fact]
    public async Task AGroupCountsEveryTypeThatBelongsToIt()
    {
        await CharacterAsync("Mira", "House Raven");
        await LocationAsync("The Rookery", "House Raven");
        await ItemAsync("The Crest", "House Raven");
        await ItemAsync("A spoon");

        var all = await _rpc.SaveAsync([New("House Raven")]);

        // A faction is exactly the thing that spans types, which is why a
        // character-only group could never express one.
        Assert.Equal(3, all[0].MemberCount);
    }

    [Fact]
    public async Task MembersComeBackWithTheirTypeSoARowCanOpen()
    {
        var mira = await CharacterAsync("Mira", "House Raven");
        await LocationAsync("The Rookery", "House Raven");
        await ItemAsync("The Crest", "House Raven");
        await _entities.SaveLoreAsync(new LoreData { Name = "The Oath", Group = "House Raven" });
        await _entities.SaveCustomEntityTypeAsync(
            new CustomEntityTypeDefinition { TypeKey = "ship", DisplayName = "Ship" });
        await _entities.SaveCustomEntityAsync(
            new CustomEntityData { EntityTypeKey = "ship", Name = "The Corvid", Group = "House Raven" });
        var id = (await _rpc.SaveAsync([New("House Raven")]))[0].Id!;

        var members = await _rpc.MembersAsync(id);

        // Every type, custom ones included: a faction is exactly the thing that
        // spans them, which is why a character-only group expressed nothing.
        Assert.Equal(
            ["character", "item", "location", "lore", "ship"],
            members.Select(m => m.TypeKey).Order());
        Assert.Contains(members, m => m.Id == mira.Id && m.TypeKey == "character");
    }

    [Fact]
    public async Task RenamingRewritesEveryTypeIncludingCustomOnes()
    {
        await ItemAsync("The Crest", "the Ravens");
        var oath = new LoreData { Name = "The Oath", Group = "the Ravens" };
        await _entities.SaveLoreAsync(oath);
        await _entities.SaveCustomEntityTypeAsync(
            new CustomEntityTypeDefinition { TypeKey = "ship", DisplayName = "Ship" });
        var corvid = new CustomEntityData
        {
            EntityTypeKey = "ship", Name = "The Corvid", Group = "the Ravens"
        };
        await _entities.SaveCustomEntityAsync(corvid);
        var id = (await _rpc.SaveAsync([New("the Ravens")]))[0].Id!;

        await _rpc.RenameAsync(id, "House Raven");

        Assert.Equal("House Raven", (await _entities.LoadItemsAsync())[0].Group);
        Assert.Equal(
            "House Raven",
            (await _entities.LoadLoreAsync()).Single(l => l.Id == oath.Id).Group);
        Assert.Equal(
            "House Raven",
            (await _entities.LoadCustomEntitiesAsync("ship")).Single(e => e.Id == corvid.Id).Group);
    }

    [Fact]
    public async Task AGroupThatIsGoneHasNoMembers()
        => Assert.Empty(await _rpc.MembersAsync("no-such-group"));

    // ── Renaming ──

    [Fact]
    public async Task RenamingReachesEveryEntryOfEveryType()
    {
        var mira = await CharacterAsync("Mira", "the Ravens");
        var rookery = await LocationAsync("The Rookery", "The Ravens");
        var id = (await _rpc.SaveAsync([New("the Ravens")]))[0].Id!;

        var all = await _rpc.RenameAsync(id, "House Raven");

        Assert.Equal("House Raven", all[0].Name);
        Assert.Equal(
            "House Raven",
            (await _entities.LoadCharactersAsync()).Single(c => c.Id == mira.Id).Group);
        Assert.Equal(
            "House Raven",
            (await _entities.LoadLocationsAsync()).Single(l => l.Id == rookery.Id).Group);
    }

    [Fact]
    public async Task RenamingOntoAGroupThatExistsIsRefused()
    {
        var saved = await _rpc.SaveAsync([New("House Raven"), New("The Watch")]);

        var all = await _rpc.RenameAsync(saved[0].Id!, "the watch");

        // Merging two factions is a thing the writer has to ask for.
        Assert.Equal(["House Raven", "The Watch"], all.Select(g => g.Name));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RenamingToNothingIsRefused(string name)
    {
        var id = (await _rpc.SaveAsync([New("House Raven")]))[0].Id!;

        Assert.Equal("House Raven", (await _rpc.RenameAsync(id, name))[0].Name);
    }

    [Fact]
    public async Task RenamingSomethingThatIsGoneIsQuiet()
    {
        await _rpc.SaveAsync([New("House Raven")]);
        Assert.Single(await _rpc.RenameAsync("no-such-group", "The Watch"));
    }

    // ── Deleting ──

    [Fact]
    public async Task DeletingTakesTheGroupOffTheEntriesToo()
    {
        var mira = await CharacterAsync("Mira", "House Raven");
        var id = (await _rpc.SaveAsync([New("House Raven")]))[0].Id!;

        Assert.Empty(await _rpc.DeleteAsync(id));

        // Leaving forty entries claiming a group nobody lists is how this
        // drifted in the first place.
        Assert.Equal(
            string.Empty,
            (await _entities.LoadCharactersAsync()).Single(c => c.Id == mira.Id).Group);
    }

    [Fact]
    public async Task DeletingCanLeaveTheEntriesAlone()
    {
        var mira = await CharacterAsync("Mira", "House Raven");
        var id = (await _rpc.SaveAsync([New("House Raven")]))[0].Id!;

        await _rpc.DeleteAsync(id, clearFromEntities: false);

        Assert.Equal(
            "House Raven",
            (await _entities.LoadCharactersAsync()).Single(c => c.Id == mira.Id).Group);
    }

    [Fact]
    public async Task DeletingSomethingThatIsGoneIsQuiet()
    {
        await _rpc.SaveAsync([New("House Raven")]);
        Assert.Single(await _rpc.DeleteAsync("no-such-group"));
    }

    // ── Harvesting ──

    [Fact]
    public async Task HarvestingPicksUpEveryGroupTheCodexUses()
    {
        await CharacterAsync("Mira", "House Raven");
        await LocationAsync("The Rookery", "The Watch");
        await ItemAsync("A spoon");

        var all = await _rpc.HarvestAsync();

        Assert.Equal(["House Raven", "The Watch"], all.Select(g => g.Name).Order());
    }

    [Fact]
    public async Task HarvestingDoesNotDuplicateWhatIsAlreadyThere()
    {
        await CharacterAsync("Mira", "house raven");
        await _rpc.SaveAsync([New("House Raven", "#c04040")]);

        var all = await _rpc.HarvestAsync();

        var kept = Assert.Single(all);
        Assert.Equal("House Raven", kept.Name);
        Assert.Equal("#c04040", kept.Color);
    }

    [Fact]
    public async Task HarvestingNothingChangesNothing()
    {
        await _rpc.SaveAsync([New("House Raven")]);
        Assert.Single(await _rpc.HarvestAsync());
    }

    [Fact]
    public async Task TheRegistrySurvivesReopeningTheProject()
    {
        await _rpc.SaveAsync([New("House Raven", "#c04040", "Northern")]);

        await _workspace.OpenProjectAsync(_workspace.Projects.ProjectRoot!);

        var group = Assert.Single(await _rpc.ListAsync());
        Assert.Equal("House Raven", group.Name);
        Assert.Equal("Northern", group.Description);
    }
}
