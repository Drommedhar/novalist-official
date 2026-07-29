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
}
