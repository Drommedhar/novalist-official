using Novalist.Backend;
using Novalist.Backend.Rpc;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Xunit;

namespace Novalist.Backend.Tests;

/// <summary>
/// Settling an entry so it cannot be changed by accident.
///
/// A world bible is a contract with the reader: once a character's eyes are
/// brown in three published chapters, changing that field is a decision rather
/// than a typo. Nothing stopped a stray keystroke in a detail pane from
/// rewriting canon silently, and nothing recorded that it had.
/// </summary>
public sealed class EntityLockTests : IDisposable
{
    private readonly string _root;
    private readonly Workspace _workspace;
    private readonly EntitiesRpc _rpc;
    private readonly EntityService _entities;
    private readonly CharacterData _mira = new() { Name = "Mira" };

    public EntityLockTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "nl-lock-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _workspace = new Workspace(Path.Combine(_root, "settings"));
        _workspace.Projects.CreateProjectAsync(_root, "LockNovel", "Book").GetAwaiter().GetResult();
        _workspace.OpenProjectAsync(_workspace.Projects.ProjectRoot!).GetAwaiter().GetResult();
        _entities = new EntityService(_workspace.Projects);
        _entities.SaveCharacterAsync(_mira).GetAwaiter().GetResult();
        _rpc = new EntitiesRpc(_workspace);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private async Task<CharacterData> ReloadAsync()
        => (await _entities.LoadCharactersAsync()).Single(c => c.Id == _mira.Id);

    [Fact]
    public async Task AnEntryStartsUnsettled()
    {
        Assert.False((await ReloadAsync()).Locked);

        var summary = (await _rpc.ListAsync("character")).Single();
        Assert.False(summary.Locked);
    }

    [Fact]
    public async Task SettlingAnEntryRefusesTheNextWrite()
    {
        await _rpc.SetLockedAsync("character", _mira.Id, true);

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _rpc.UpdateAsync("character", _mira.Id,
                new Dictionary<string, string> { ["eyeColor"] = "green" }));

        Assert.Equal(EntitiesRpc.LockedMessage, refused.Message);
        Assert.Equal(string.Empty, (await ReloadAsync()).EyeColor);
    }

    [Fact]
    public async Task AppendingProseToASettledEntryIsAWriteLikeAnyOther()
    {
        await _rpc.SetLockedAsync("character", _mira.Id, true);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _rpc.AppendToSectionAsync("character", _mira.Id, "Appearance", "Tall."));

        Assert.Empty((await ReloadAsync()).Sections);
    }

    [Fact]
    public async Task UnsettlingLetsTheWriteThrough()
    {
        await _rpc.SetLockedAsync("character", _mira.Id, true);
        await _rpc.SetLockedAsync("character", _mira.Id, false);

        await _rpc.UpdateAsync("character", _mira.Id,
            new Dictionary<string, string> { ["eyeColor"] = "green" });

        // A lock that cannot be undone is a lock nobody uses.
        Assert.Equal("green", (await ReloadAsync()).EyeColor);
    }

    [Fact]
    public async Task TheLockSurvivesReopeningTheProject()
    {
        await _rpc.SetLockedAsync("character", _mira.Id, true);

        await _workspace.OpenProjectAsync(_workspace.Projects.ProjectRoot!);

        Assert.True((await ReloadAsync()).Locked);
    }

    [Fact]
    public async Task TheListSaysWhichEntriesAreSettled()
    {
        await _rpc.SetLockedAsync("character", _mira.Id, true);

        Assert.True((await _rpc.ListAsync("character")).Single().Locked);
    }

    [Fact]
    public async Task EveryTypeCanBeSettled()
    {
        var place = new LocationData { Name = "The Rookery" };
        await _entities.SaveLocationAsync(place);
        var relic = new ItemData { Name = "The Crest" };
        await _entities.SaveItemAsync(relic);
        var oath = new LoreData { Name = "The Oath" };
        await _entities.SaveLoreAsync(oath);
        await _entities.SaveCustomEntityTypeAsync(
            new CustomEntityTypeDefinition { TypeKey = "ship", DisplayName = "Ship" });
        var ship = new CustomEntityData { EntityTypeKey = "ship", Name = "The Corvid" };
        await _entities.SaveCustomEntityAsync(ship);

        Assert.True(await _rpc.SetLockedAsync("location", place.Id, true));
        Assert.True(await _rpc.SetLockedAsync("item", relic.Id, true));
        Assert.True(await _rpc.SetLockedAsync("lore", oath.Id, true));
        // A bible lives in the types the writer invented as much as the four
        // that ship.
        Assert.True(await _rpc.SetLockedAsync("ship", ship.Id, true));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _rpc.UpdateAsync("ship", ship.Id,
                new Dictionary<string, string> { ["name"] = "Renamed" }));
    }

    [Fact]
    public async Task SettlingSomethingThatIsNotThereThrows()
        => await Assert.ThrowsAsync<InvalidOperationException>(
            () => _rpc.SetLockedAsync("character", "no-such-entry", true));
}
