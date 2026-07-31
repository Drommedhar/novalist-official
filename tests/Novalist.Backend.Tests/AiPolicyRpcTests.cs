using Novalist.Backend;
using Novalist.Backend.Rpc;
using Novalist.Core.Models;
using Xunit;

namespace Novalist.Backend.Tests;

/// <summary>
/// The AI-inclusion setting from the RPC the Codex panel calls, and the host
/// service an extension is expected to assemble its model context from.
/// </summary>
public sealed class AiPolicyRpcTests : IDisposable
{
    private readonly string _root;
    private readonly Workspace _workspace;
    private readonly EntitiesRpc _rpc;

    public AiPolicyRpcTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "nl-ai-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _workspace = new Workspace(Path.Combine(_root, "settings"));
        _workspace.Projects.CreateProjectAsync(_root, "AiNovel", "Book").GetAwaiter().GetResult();
        _workspace.OpenProjectAsync(_workspace.Projects.ProjectRoot!).GetAwaiter().GetResult();
        _rpc = new EntitiesRpc(_workspace);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private async Task<string> CharacterAsync(string name, params EntitySection[] sections)
    {
        var created = await _rpc.CreateAsync("character", name);
        var id = created.GetProperty("id").GetString()!;
        if (sections.Length > 0)
        {
            var entities = new Novalist.Core.Services.EntityService(_workspace.Projects);
            var character = (await entities.LoadCharactersAsync()).First(c => c.Id == id);
            character.Sections = [.. sections];
            await entities.SaveCharacterAsync(character);
        }
        return id;
    }

    [Fact]
    public async Task ANewEntryDefaultsToBeingSentWhenMentioned()
    {
        var id = await CharacterAsync("Rose");

        var policy = await _rpc.GetAiPolicyAsync("character", id);

        Assert.Equal("WhenMentioned", policy.Inclusion);
    }

    [Fact]
    public async Task TheSettingRoundTrips()
    {
        var id = await CharacterAsync("Rose");

        await _rpc.SetAiPolicyAsync("character", id, "Never", []);

        Assert.Equal("Never", (await _rpc.GetAiPolicyAsync("character", id)).Inclusion);
    }

    [Fact]
    public async Task AnUnknownSettingFallsBackToTheDefaultRatherThanToNever()
    {
        // Silently hiding an entry the writer expects the model to see is the
        // more surprising failure of the two.
        var id = await CharacterAsync("Rose");

        await _rpc.SetAiPolicyAsync("character", id, "nonsense", []);

        Assert.Equal("WhenMentioned", (await _rpc.GetAiPolicyAsync("character", id)).Inclusion);
    }

    [Fact]
    public async Task SectionsAreListedWithTheirTitles()
    {
        var id = await CharacterAsync(
            "Rose",
            new EntitySection { Title = "Public" },
            new EntitySection { Title = "The twist" });

        var policy = await _rpc.GetAiPolicyAsync("character", id);

        Assert.Equal(["Public", "The twist"], policy.Sections.Select(s => s.Title));
        Assert.All(policy.Sections, s => Assert.False(s.Hidden));
    }

    [Fact]
    public async Task WithholdingASectionRoundTrips()
    {
        var id = await CharacterAsync(
            "Rose",
            new EntitySection { Title = "Public" },
            new EntitySection { Title = "The twist" });

        await _rpc.SetAiPolicyAsync("character", id, "WhenMentioned", [1]);

        var policy = await _rpc.GetAiPolicyAsync("character", id);
        Assert.False(policy.Sections[0].Hidden);
        Assert.True(policy.Sections[1].Hidden);
    }

    [Fact]
    public async Task AStaleSectionIndexIsIgnoredRatherThanThrowing()
    {
        // The panel's view of the sections can be one edit behind.
        var id = await CharacterAsync("Rose", new EntitySection { Title = "Only" });

        var policy = await _rpc.SetAiPolicyAsync("character", id, "WhenMentioned", [0, 7]);

        Assert.True(policy.Sections.Single().Hidden);
    }

    [Fact]
    public async Task WithholdingCanBeTakenBack()
    {
        var id = await CharacterAsync("Rose", new EntitySection { Title = "Once secret" });
        await _rpc.SetAiPolicyAsync("character", id, "WhenMentioned", [0]);

        var policy = await _rpc.SetAiPolicyAsync("character", id, "WhenMentioned", []);

        Assert.False(policy.Sections.Single().Hidden);
    }

    [Fact]
    public async Task AnUnknownEntryReadsAsTheDefault()
    {
        var policy = await _rpc.GetAiPolicyAsync("character", "nope");

        Assert.Equal("WhenMentioned", policy.Inclusion);
        Assert.Empty(policy.Sections);
    }

    [Fact]
    public async Task SettingAnUnknownEntryThrows()
    {
        await Assert.ThrowsAnyAsync<Exception>(
            () => _rpc.SetAiPolicyAsync("character", "nope", "Never", []));
    }

    [Fact]
    public async Task EveryEntryTypeCarriesTheSetting()
    {
        foreach (var type in new[] { "location", "item", "lore" })
        {
            var created = await _rpc.CreateAsync(type, "Thing");
            var id = created.GetProperty("id").GetString()!;

            await _rpc.SetAiPolicyAsync(type, id, "Always", []);

            Assert.Equal("Always", (await _rpc.GetAiPolicyAsync(type, id)).Inclusion);
        }
    }

    [Fact]
    public async Task ACustomTypeCarriesTheSettingToo()
    {
        await _rpc.SaveCustomTypeAsync(new CustomTypeSpecDto(
            TypeKey: null, DisplayName: "Faction", DisplayNamePlural: null, Fields: [],
            IncludeImages: false, IncludeRelationships: false, IncludeSections: true));
        var created = await _rpc.CreateAsync("faction", "The Ravens");
        var id = created.GetProperty("id").GetString()!;

        await _rpc.SetAiPolicyAsync("faction", id, "Never", []);

        Assert.Equal("Never", (await _rpc.GetAiPolicyAsync("faction", id)).Inclusion);
    }

    [Fact]
    public async Task TheSettingSurvivesAReload()
    {
        var id = await CharacterAsync("Rose");
        await _rpc.SetAiPolicyAsync("character", id, "Always", []);

        await _workspace.OpenProjectAsync(_workspace.Projects.ProjectRoot!);

        Assert.Equal(
            "Always",
            (await new EntitiesRpc(_workspace).GetAiPolicyAsync("character", id)).Inclusion);
    }

    // ── What a reader may see: a different question from what a model may ──

    [Fact]
    public async Task AnEntryAndItsSectionsCanBeKeptFromReaders()
    {
        var id = await CharacterAsync("The Hollow King",
            new EntitySection { Title = "Appearance", Content = "Tall." },
            new EntitySection { Title = "The truth", Content = "He did it." });

        var policy = await _rpc.SetReaderPolicyAsync("character", id, hidden: false, [1]);

        Assert.False(policy.Hidden);
        Assert.False(policy.Sections[0].Hidden);
        Assert.True(policy.Sections[1].Hidden);

        // It survives a round trip rather than only living in the reply.
        var read = await _rpc.GetReaderPolicyAsync("character", id);
        Assert.True(read.Sections[1].Hidden);

        var whole = await _rpc.SetReaderPolicyAsync("character", id, hidden: true, []);
        Assert.True(whole.Hidden);
        Assert.False((await _rpc.GetReaderPolicyAsync("character", id)).Sections[1].Hidden);
    }

    [Fact]
    public async Task ANewEntryIsVisibleToReaders()
    {
        var id = await CharacterAsync("Rose");

        Assert.False((await _rpc.GetReaderPolicyAsync("character", id)).Hidden);
    }

    [Fact]
    public async Task KeepingAnEntryFromReadersIsNotKeepingItFromTheModel()
    {
        // Two axes on purpose: a writer may be happy for a model to know the
        // twist while planning and never for a reader to find it.
        var id = await CharacterAsync("Mara",
            new EntitySection { Title = "The truth", Content = "She did it." });

        await _rpc.SetReaderPolicyAsync("character", id, hidden: false, [0]);

        Assert.False((await _rpc.GetAiPolicyAsync("character", id)).Sections[0].Hidden);
    }

    [Fact]
    public async Task AnIndexThatNamesNoSectionIsIgnored()
    {
        // The panel's view of the sections can be one edit behind.
        var id = await CharacterAsync("Rose",
            new EntitySection { Title = "Appearance", Content = "Tall." });

        var policy = await _rpc.SetReaderPolicyAsync("character", id, hidden: false, [7]);

        Assert.False(policy.Sections[0].Hidden);
    }

    [Fact]
    public async Task AReaderPolicyOnAnEntryThatIsNotThereIsRefused()
    {
        await Assert.ThrowsAnyAsync<Exception>(
            () => _rpc.SetReaderPolicyAsync("character", "no-such-id", hidden: true, []));
    }
}

