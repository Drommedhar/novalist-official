using Novalist.Core.Tests.TestHelpers;
using Novalist.Desktop.Services;
using Xunit;

namespace Novalist.Desktop.Tests.Services;

public class ExtensionLocalizationServiceTests
{
    private static string WriteLocale(TempDir dir, string lang, string json)
    {
        var path = Path.Combine(dir.Path, $"{lang}.json");
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public void Resolve_PrefersLanguage_ThenFallback_ThenKey()
    {
        using var dir = new TempDir();
        WriteLocale(dir, "en", """{ "greeting": "Hello", "only_en": "EN" }""");
        WriteLocale(dir, "de", """{ "greeting": "Hallo" }""");
        var sut = new ExtensionLocalizationService(dir.Path, "de");

        Assert.Equal("Hallo", sut.T("greeting"));   // language wins
        Assert.Equal("EN", sut.T("only_en"));        // falls back to en
        Assert.Equal("missing.key", sut["missing.key"]); // returns key
    }

    [Fact]
    public void English_UsesFallbackDirectly()
    {
        using var dir = new TempDir();
        WriteLocale(dir, "en", """{ "x": "X" }""");
        var sut = new ExtensionLocalizationService(dir.Path, "en");
        Assert.Equal("X", sut.T("x"));
    }

    [Fact]
    public void FlattensNestedKeys_AndStringifiesNonStrings()
    {
        using var dir = new TempDir();
        WriteLocale(dir, "en", """{ "menu": { "file": { "open": "Open" } }, "count": 5, "flag": true }""");
        var sut = new ExtensionLocalizationService(dir.Path, "en");
        Assert.Equal("Open", sut.T("menu.file.open"));
        Assert.Equal("5", sut.T("count"));
        Assert.Equal("True", sut.T("flag"));
    }

    [Fact]
    public void T_WithArgs_FormatsAndToleratesBadTemplate()
    {
        using var dir = new TempDir();
        WriteLocale(dir, "en", """{ "hi": "Hi {0}!", "bad": "Hi {0} {1}" }""");
        var sut = new ExtensionLocalizationService(dir.Path, "en");
        Assert.Equal("Hi Jane!", sut.T("hi", "Jane"));
        // Too few args -> FormatException swallowed, returns the raw template.
        Assert.Equal("Hi {0} {1}", sut.T("bad", "only-one"));
    }

    [Fact]
    public void MissingLanguageFile_FallsBackToEnglish()
    {
        using var dir = new TempDir();
        WriteLocale(dir, "en", """{ "x": "X" }""");
        var sut = new ExtensionLocalizationService(dir.Path, "fr"); // no fr.json
        Assert.Equal("X", sut.T("x"));
    }

    [Fact]
    public void CorruptJson_TreatedAsEmpty()
    {
        using var dir = new TempDir();
        WriteLocale(dir, "en", "{ not valid json");
        var sut = new ExtensionLocalizationService(dir.Path, "en");
        Assert.Equal("anything", sut.T("anything")); // empty dicts -> key returned
    }

    [Fact]
    public void Reload_SwitchesLanguage_AndRaisesPropertyChanged()
    {
        using var dir = new TempDir();
        WriteLocale(dir, "en", """{ "g": "Hello" }""");
        WriteLocale(dir, "de", """{ "g": "Hallo" }""");
        var sut = new ExtensionLocalizationService(dir.Path, "en");
        Assert.Equal("Hello", sut.T("g"));

        var raised = false;
        sut.PropertyChanged += (_, _) => raised = true;
        sut.Reload("de");
        Assert.Equal("Hallo", sut.T("g"));
        Assert.True(raised);
    }
}
