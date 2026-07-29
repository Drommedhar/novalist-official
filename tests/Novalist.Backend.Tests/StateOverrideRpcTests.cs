using Novalist.Backend;
using Novalist.Backend.Rpc;
using Xunit;

namespace Novalist.Backend.Tests;

/// <summary>Time-scoped restatements from the RPC the Codex panel and the peek
/// card call.</summary>
public sealed class StateOverrideRpcTests : IDisposable
{
    private readonly string _root;
    private readonly Workspace _workspace;
    private readonly EntitiesRpc _rpc;

    public StateOverrideRpcTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "nl-state-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _workspace = new Workspace(Path.Combine(_root, "settings"));
        _workspace.Projects.CreateProjectAsync(_root, "StateNovel", "Book").GetAwaiter().GetResult();
        _workspace.OpenProjectAsync(_workspace.Projects.ProjectRoot!).GetAwaiter().GetResult();
        _rpc = new EntitiesRpc(_workspace);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private async Task<string> LocationAsync(string name = "Aelthorn")
    {
        var created = await _rpc.CreateAsync("location", name);
        return created.GetProperty("id").GetString()!;
    }

    private static StateOverrideDto Override(
        string chapter, string? scene = null, string? description = null,
        string? name = null, string? note = null)
        => new(null, chapter, scene, name, description, null, note);

    [Fact]
    public async Task ANewEntryHasNoRestatements()
    {
        Assert.Empty(await _rpc.GetStateOverridesAsync("location", await LocationAsync()));
    }

    [Fact]
    public async Task RestatementsRoundTrip()
    {
        var id = await LocationAsync();

        var saved = await _rpc.SetStateOverridesAsync(
            "location", id, [Override("ch-2", description: "Razed.", note: "The siege.")]);

        Assert.Equal("Razed.", saved.Single().Description);
        Assert.Equal("The siege.", saved.Single().Note);
    }

    [Fact]
    public async Task AnEmptyRestatementIsDropped()
    {
        // It would claim the entry differs there while saying nothing about how.
        var id = await LocationAsync();

        var saved = await _rpc.SetStateOverridesAsync(
            "location", id, [Override("ch-2", note: "just a note")]);

        Assert.Empty(saved);
    }

    [Fact]
    public async Task ResolveReturnsTheRestatementForThatChapter()
    {
        var id = await LocationAsync();
        await _rpc.SetStateOverridesAsync("location", id, [Override("ch-2", description: "Razed.")]);

        var resolved = await _rpc.ResolveStateAsync("location", id, null, "ch-2", "The Fall", null);

        Assert.True(resolved.IsOverridden);
        Assert.Equal("Razed.", resolved.Description);
        Assert.Equal("Ch: The Fall", resolved.ScopeLabel);
    }

    [Fact]
    public async Task ResolveElsewhereReadsTheEntryAsItself()
    {
        var id = await LocationAsync();
        await _rpc.SetStateOverridesAsync("location", id, [Override("ch-2", description: "Razed.")]);

        var resolved = await _rpc.ResolveStateAsync("location", id, null, "ch-1", "Before", null);

        Assert.False(resolved.IsOverridden);
    }

    [Fact]
    public async Task ResolvingAnUnknownEntryIsNotOverridden()
    {
        var resolved = await _rpc.ResolveStateAsync("location", "nope", null, "ch-1", null, null);

        Assert.False(resolved.IsOverridden);
    }

    [Fact]
    public async Task SettingOnAnUnknownEntryThrows()
    {
        await Assert.ThrowsAnyAsync<Exception>(
            () => _rpc.SetStateOverridesAsync("location", "nope", [Override("ch-1", description: "x")]));
    }

    [Fact]
    public async Task RestatementsSurviveAReload()
    {
        var id = await LocationAsync();
        await _rpc.SetStateOverridesAsync("location", id, [Override("ch-2", description: "Razed.")]);

        await _workspace.OpenProjectAsync(_workspace.Projects.ProjectRoot!);

        Assert.Single(await new EntitiesRpc(_workspace).GetStateOverridesAsync("location", id));
    }

    [Fact]
    public async Task EveryEntityTypeCanBeRestated()
    {
        foreach (var type in new[] { "item", "lore", "character" })
        {
            var created = await _rpc.CreateAsync(type, "Thing");
            var id = created.GetProperty("id").GetString()!;

            var saved = await _rpc.SetStateOverridesAsync(
                "" + type, id, [Override("ch-2", description: "Changed.")]);

            Assert.Single(saved);
        }
    }

    [Fact]
    public async Task AFieldCanBeRestated()
    {
        var id = await LocationAsync();

        var saved = await _rpc.SetStateOverridesAsync(
            "location", id,
            [new StateOverrideDto(null, "ch-2", null, null, null,
                new Dictionary<string, string> { ["Owner"] = "The thief" }, null)]);

        Assert.Equal("The thief", saved.Single().Fields!["Owner"]);
    }

    [Fact]
    public async Task ABlankFieldKeyIsDropped()
    {
        var id = await LocationAsync();

        var saved = await _rpc.SetStateOverridesAsync(
            "location", id,
            [new StateOverrideDto(null, "ch-2", null, null, "Razed.",
                new Dictionary<string, string> { ["  "] = "nothing" }, null)]);

        Assert.True(saved.Single().Fields == null || saved.Single().Fields!.Count == 0);
    }
}
