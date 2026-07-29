using System.Text.Json;
using Novalist.Backend;
using Novalist.Backend.Rpc;
using Novalist.Core.Services;
using Xunit;

namespace Novalist.Backend.Tests;

/// <summary>
/// What Novalist can read and write each language in, and the path a
/// contributor takes to add one.
///
/// Novalist bundles three interface languages and three analysis lexicons.
/// Everything past that is a dropped-in file, so the surface that reports the
/// gap honestly and lets it be filled without a restart is the feature.
/// </summary>
public sealed class LanguagePacksRpcTests : IDisposable
{
    private readonly string _root;
    private readonly Workspace _workspace;
    private readonly AppearanceRpc _rpc;

    public LanguagePacksRpcTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "nl-lang-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _workspace = new Workspace(_root);
        _rpc = new AppearanceRpc(_workspace);
    }

    public void Dispose()
    {
        // The lexicon loader keeps its user directory in a static, so a test that
        // pointed it at a temp folder has to put it back or the next one reads a
        // directory that no longer exists.
        SceneAnalysisLexicon.RegisterUserDirectory(null);
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    /// <summary>Reads through Rescan rather than LanguagePacks so the lookup
    /// re-registers this workspace's folders first. The lexicon loader keeps its
    /// user directory in a process-wide static, and every other test class that
    /// builds a Workspace writes to it.</summary>
    private LanguagePackDto Pack(string code) =>
        _rpc.Rescan().Single(p => p.Code == code);

    [Fact]
    public void TheThreeBundledLanguagesReportAsBundled()
    {
        foreach (var code in new[] { "en", "de", "zh-CN" })
        {
            Assert.Equal("bundled", Pack(code).Interface);
            Assert.Equal("bundled", Pack(code).Lexicon);
        }
    }

    [Fact]
    public void AWritingLanguageWithNoSupportIsListedRatherThanOmitted()
    {
        // French is on the Quote Style list, so a writer can pick it. Leaving it
        // out of the report would leave them guessing why their scenes have no
        // detected emotion.
        var french = Pack("fr");

        Assert.Equal("missing", french.Interface);
        Assert.Equal("missing", french.Lexicon);
    }

    [Fact]
    public void InterfaceAndLexiconAreReportedSeparately()
    {
        // Reading the menus in English while writing in French is normal, so one
        // combined "supported" flag would be wrong for half the people asking.
        File.WriteAllText(
            Path.Combine(_workspace.UserAssets.AnalysisDirectory, "analysis.fr.json"),
            SceneAnalysisLexicon.TemplateFor("fr"));

        var french = _rpc.Rescan().Single(p => p.Code == "fr");

        Assert.Equal("missing", french.Interface);
        Assert.Equal("user", french.Lexicon);
    }

    [Fact]
    public void ADroppedInterfaceLocaleReportsAsTheUsers()
    {
        File.WriteAllText(
            Path.Combine(_workspace.UserAssets.LocalesDirectory, "fr.json"),
            "{\"_name\": \"Fran\\u00e7ais\", \"shell\": {\"words\": \"mots\"}}");

        var french = _rpc.Rescan().Single(p => p.Code == "fr");

        Assert.Equal("user", french.Interface);
    }

    [Fact]
    public void RescanPicksUpAFileDroppedAfterStartupWithoutARestart()
    {
        Assert.Equal("missing", Pack("nl").Lexicon);

        File.WriteAllText(
            Path.Combine(_workspace.UserAssets.AnalysisDirectory, "analysis.nl.json"),
            SceneAnalysisLexicon.TemplateFor("nl"));

        Assert.Equal("user", _rpc.Rescan().Single(p => p.Code == "nl").Lexicon);
    }

    [Fact]
    public void EveryLanguageIsListedOnce()
    {
        var codes = _rpc.Rescan().Select(p => p.Code).ToList();

        Assert.Equal(codes.Count, codes.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    // ── The lexicon template ──

    [Fact]
    public void WriteLexiconTemplate_LandsInTheAnalysisFolder()
    {
        var path = _rpc.WriteLexiconTemplate("nl");

        Assert.Equal(
            Path.Combine(_workspace.UserAssets.AnalysisDirectory, "analysis.nl.json"), path);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void WriteLexiconTemplate_CarriesTheEnglishListsAsAStartingPoint()
    {
        var json = JsonDocument.Parse(SceneAnalysisLexicon.TemplateFor("nl")).RootElement;

        // Seeded rather than blank, so the work is translating a real list.
        Assert.NotEmpty(json.GetProperty("conflict").EnumerateArray());
        Assert.NotEmpty(json.GetProperty("speechVerbs").EnumerateArray());
        Assert.Contains("nl", json.GetProperty("_comment").GetString());
    }

    [Fact]
    public void WriteLexiconTemplate_RefusesToOverwriteWorkAlreadyDone()
    {
        _rpc.WriteLexiconTemplate("nl");

        Assert.Throws<InvalidOperationException>(() => _rpc.WriteLexiconTemplate("nl"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void WriteLexiconTemplate_NeedsALanguageTag(string tag)
    {
        Assert.Throws<InvalidOperationException>(() => _rpc.WriteLexiconTemplate(tag));
    }

    [Fact]
    public void WriteLexiconTemplate_TrimsTheTag()
    {
        var path = _rpc.WriteLexiconTemplate("  nl  ");

        Assert.EndsWith("analysis.nl.json", path);
    }
}
