using Novalist.Backend;
using Novalist.Backend.Rpc;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Xunit;

namespace Novalist.Backend.Tests;

/// <summary>
/// The book's own completion list, over the wire.
/// </summary>
public sealed class CompletionRpcTests : IDisposable
{
    private readonly string _root;
    private readonly Workspace _workspace;
    private readonly CompletionRpc _rpc;

    public CompletionRpcTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "nl-comp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _workspace = new Workspace(Path.Combine(_root, "settings"));
        _workspace.Projects.CreateProjectAsync(_root, "CompNovel", "Book").GetAwaiter().GetResult();
        _workspace.OpenProjectAsync(_workspace.Projects.ProjectRoot!).GetAwaiter().GetResult();
        _rpc = new CompletionRpc(_workspace);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    [Fact]
    public void ABookStartsWithNothingToComplete()
    {
        var list = _rpc.Get();

        Assert.Empty(list.Words);
        Assert.Equal(CompletionList.MinimumTrigger, list.Trigger);
    }

    [Fact]
    public void WithNoProjectOpenTheListIsEmptyRatherThanMissing()
        => Assert.Empty(new CompletionRpc(new Workspace(Path.Combine(_root, "none"))).Get().Words);

    [Fact]
    public async Task SavingKeepsTheWritersOrderAndDropsTheRest()
    {
        var saved = await _rpc.SaveAsync(["  Aerthorn ", "", "kaeryn", "AERTHORN", "Sill"]);

        Assert.Equal(["Aerthorn", "kaeryn", "Sill"], saved.Words);
    }

    [Fact]
    public async Task TheTriggerRoundTripsAndIsClamped()
    {
        Assert.Equal(5, (await _rpc.SaveAsync(["Aerthorn"], 5)).Trigger);
        Assert.Equal(CompletionList.MinimumTrigger, (await _rpc.SaveAsync(["Aerthorn"], 1)).Trigger);
    }

    [Fact]
    public async Task NotSayingATriggerLeavesItAlone()
    {
        await _rpc.SaveAsync(["Aerthorn"], 6);

        Assert.Equal(6, (await _rpc.SaveAsync(["Aerthorn", "Sill"])).Trigger);
    }

    [Fact]
    public async Task SavingNeedsABook()
    {
        var bare = new CompletionRpc(new Workspace(Path.Combine(_root, "no-project")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => bare.SaveAsync([]));
        await Assert.ThrowsAsync<InvalidOperationException>(() => bare.AddCodexNamesAsync());
    }

    [Fact]
    public async Task SavingNothingClearsTheList()
    {
        await _rpc.SaveAsync(["Aerthorn"]);
        Assert.Empty((await _rpc.SaveAsync(null)).Words);
    }

    [Fact]
    public async Task TheCodexCanBePouredIntoTheList()
    {
        var entities = new EntityService(_workspace.Projects);
        await entities.SaveCharacterAsync(new CharacterData { Name = "Mira", Surname = "Vance" });
        await entities.SaveLocationAsync(new LocationData { Name = "The Rookery" });
        await entities.SaveItemAsync(new ItemData { Name = "The Crest" });
        await entities.SaveLoreAsync(new LoreData { Name = "The Oath" });
        await entities.SaveCustomEntityTypeAsync(
            new CustomEntityTypeDefinition { TypeKey = "ship", DisplayName = "Ship" });
        await entities.SaveCustomEntityAsync(
            new CustomEntityData { EntityTypeKey = "ship", Name = "The Corvid" });

        var saved = await _rpc.AddCodexNamesAsync();

        // Retyping the cast into this box is exactly the work the list exists
        // to remove, and a faction lives in a custom type as often as not.
        Assert.Contains("The Rookery", saved.Words);
        Assert.Contains("The Crest", saved.Words);
        Assert.Contains("The Oath", saved.Words);
        Assert.Contains("The Corvid", saved.Words);
        Assert.Contains(saved.Words, w => w.Contains("Mira", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PouringTheCodexInTwiceDoesNotDoubleTheList()
    {
        var entities = new EntityService(_workspace.Projects);
        await entities.SaveCharacterAsync(new CharacterData { Name = "Mira" });
        await _rpc.SaveAsync(["Aerthorn"]);

        await _rpc.AddCodexNamesAsync();
        var twice = await _rpc.AddCodexNamesAsync();

        Assert.Equal(twice.Words.Count, twice.Words.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        // What the writer typed is kept, and kept first.
        Assert.Equal("Aerthorn", twice.Words[0]);
    }

    [Fact]
    public async Task TheListSurvivesReopeningTheProject()
    {
        await _rpc.SaveAsync(["Aerthorn"], 4);

        await _workspace.OpenProjectAsync(_workspace.Projects.ProjectRoot!);

        var list = _rpc.Get();
        Assert.Equal(["Aerthorn"], list.Words);
        Assert.Equal(4, list.Trigger);
    }
}
