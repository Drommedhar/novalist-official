using System.Text.Json;
using Novalist.Backend;
using Novalist.Backend.Rpc;
using Xunit;

namespace Novalist.Backend.Tests;

/// <summary>
/// The writer's own dictionary, and the spell-check settings reaching the
/// renderer. The words live in the settings file rather than the platform
/// dictionary so they survive a reinstall.
/// </summary>
public sealed class SpellRpcTests : IDisposable
{
    private readonly string _root;
    private readonly Workspace _workspace;
    private readonly SpellRpc _rpc;

    public SpellRpcTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "nl-spell-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _workspace = new Workspace(Path.Combine(_root, "settings"));
        _rpc = new SpellRpc(_workspace);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    [Fact]
    public async Task Words_StartEmpty()
    {
        Assert.Empty(await _rpc.GetWordsAsync());
    }

    [Fact]
    public async Task AddWord_ReturnsTheFullListSoTheCallerNeedsNoSecondRead()
    {
        await _rpc.AddWordAsync("Aelthorn");

        Assert.Equal(["Aelthorn", "Vaskaya"], await _rpc.AddWordAsync("Vaskaya"));
    }

    [Fact]
    public async Task AddWord_TrimsWhatTheMenuHandedOver()
    {
        Assert.Equal(["Aelthorn"], await _rpc.AddWordAsync("  Aelthorn  "));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AddWord_BlankIsIgnored(string word)
    {
        Assert.Empty(await _rpc.AddWordAsync(word));
    }

    [Fact]
    public async Task AddWord_TheSameWordTwiceIsStoredOnce()
    {
        await _rpc.AddWordAsync("Aelthorn");

        Assert.Equal(["Aelthorn"], await _rpc.AddWordAsync("Aelthorn"));
    }

    [Fact]
    public async Task AddWord_CasingMakesADifferentWord()
    {
        // "Rose" the name and "rose" the flower are not the same entry.
        await _rpc.AddWordAsync("Aelthorn");

        Assert.Equal(2, (await _rpc.AddWordAsync("aelthorn")).Length);
    }

    [Fact]
    public async Task RemoveWord_TakesItBackOut()
    {
        await _rpc.AddWordAsync("Aelthorn");
        await _rpc.AddWordAsync("Vaskaya");

        Assert.Equal(["Vaskaya"], await _rpc.RemoveWordAsync("Aelthorn"));
    }

    [Fact]
    public async Task RemoveWord_AWordThatWasNeverThereChangesNothing()
    {
        await _rpc.AddWordAsync("Aelthorn");

        Assert.Equal(["Aelthorn"], await _rpc.RemoveWordAsync("nope"));
    }

    [Fact]
    public async Task Words_SurviveAReload()
    {
        await _rpc.AddWordAsync("Aelthorn");

        var reopened = new Workspace(Path.Combine(_root, "settings"));
        try
        {
            Assert.Equal(["Aelthorn"], await new SpellRpc(reopened).GetWordsAsync());
        }
        finally
        {
            reopened.Dispose();
        }
    }

    // ── Settings ──

    [Fact]
    public async Task Settings_ReportSpellCheckOnByDefault()
    {
        var settings = new SettingsRpc(_workspace);

        var effective = (await settings.GetAsync()).GetProperty("effective");

        Assert.True(effective.GetProperty("spellCheckEnabled").GetBoolean());
    }

    [Fact]
    public async Task Settings_ReportTheWritingLanguageWhenNoDictionaryIsPicked()
    {
        var settings = new SettingsRpc(_workspace);
        await settings.UpdateGlobalAsync(Patch("autoReplacementLanguage", "\"de\""));

        var effective = (await settings.GetAsync()).GetProperty("effective");

        Assert.Equal(
            ["de"],
            effective.GetProperty("spellCheckLanguages").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public async Task Settings_AcceptAListOfDictionaries()
    {
        var settings = new SettingsRpc(_workspace);

        await settings.UpdateGlobalAsync(Patch("spellCheckLanguages", "[\"en-GB\", \"de-DE\"]"));

        Assert.Equal(
            ["en-GB", "de-DE"],
            _workspace.Settings.Settings.SpellCheckLanguages);
    }

    [Fact]
    public async Task Settings_BlankDictionaryTagsAreDropped()
    {
        var settings = new SettingsRpc(_workspace);

        await settings.UpdateGlobalAsync(Patch("spellCheckLanguages", "[\"en-GB\", \"\"]"));

        Assert.Equal(["en-GB"], _workspace.Settings.Settings.SpellCheckLanguages);
    }

    [Fact]
    public async Task Settings_ADictionaryListThatIsNotAListIsRefused()
    {
        var settings = new SettingsRpc(_workspace);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => settings.UpdateGlobalAsync(Patch("spellCheckLanguages", "\"en-GB\"")));
    }

    [Fact]
    public async Task Settings_TurningSpellCheckOffRoundTrips()
    {
        var settings = new SettingsRpc(_workspace);

        var result = await settings.UpdateGlobalAsync(Patch("spellCheckEnabled", "false"));

        Assert.False(result.GetProperty("effective").GetProperty("spellCheckEnabled").GetBoolean());
    }

    private static Dictionary<string, JsonElement> Patch(string key, string json) =>
        new() { [key] = JsonDocument.Parse(json).RootElement };

    // ── The Codex feeds the dictionary ──
    //
    // A secondary-world manuscript was a wall of red underlines: the Codex knew
    // every name in the book and the checker knew none of them.

    [Fact]
    public async Task Dictionary_CarriesEveryCodexNameAndTheWritersOwnWords()
    {
        _workspace.Projects.CreateProjectAsync(_root, "SpellNovel", "Book").GetAwaiter().GetResult();
        await _workspace.OpenProjectAsync(_workspace.Projects.ProjectRoot!);
        var entities = new Novalist.Core.Services.EntityService(_workspace.Projects);
        await entities.SaveCharacterAsync(new Novalist.Core.Models.CharacterData
        {
            Name = "Mira",
            Surname = "Vance",
            Aliases = ["Blackthorn"]
        });
        await entities.SaveLocationAsync(new Novalist.Core.Models.LocationData { Name = "Hillsford" });
        await entities.SaveItemAsync(new Novalist.Core.Models.ItemData { Name = "Skyglass" });
        await entities.SaveLoreAsync(new Novalist.Core.Models.LoreData { Name = "The Sundering" });
        await _rpc.AddWordAsync("thaumic");

        var dictionary = await _rpc.GetDictionaryAsync();

        // A name is checked word by word, so both halves have to be taught.
        Assert.Contains("Mira", dictionary);
        Assert.Contains("Vance", dictionary);
        Assert.Contains("Blackthorn", dictionary);
        Assert.Contains("Hillsford", dictionary);
        Assert.Contains("Skyglass", dictionary);
        Assert.Contains("Sundering", dictionary);
        Assert.Contains("thaumic", dictionary);
        // "The" is a word the checker already knows; one-letter fragments teach
        // it nothing at all.
        Assert.DoesNotContain(dictionary, w => w.Length <= 1);
    }

    [Fact]
    public async Task Dictionary_WithNoProjectOpen_IsJustTheWritersOwnWords()
    {
        await _rpc.AddWordAsync("thaumic");

        Assert.Equal(["thaumic"], await _rpc.GetDictionaryAsync());
    }

    [Fact]
    public async Task Dictionary_DoesNotRepeatANameTheWriterAlsoTaught()
    {
        _workspace.Projects.CreateProjectAsync(_root, "SpellNovel", "Book").GetAwaiter().GetResult();
        await _workspace.OpenProjectAsync(_workspace.Projects.ProjectRoot!);
        await new Novalist.Core.Services.EntityService(_workspace.Projects)
            .SaveCharacterAsync(new Novalist.Core.Models.CharacterData { Name = "Mira" });
        await _rpc.AddWordAsync("Mira");

        Assert.Single(await _rpc.GetDictionaryAsync(), w => w == "Mira");
    }
}
