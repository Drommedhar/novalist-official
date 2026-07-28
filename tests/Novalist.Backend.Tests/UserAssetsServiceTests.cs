using Novalist.Backend.Appearance;
using Novalist.Backend.Tests.TestHelpers;
using Xunit;

namespace Novalist.Backend.Tests;

/// <summary>
/// Covers <see cref="UserAssetsService"/> — discovery of user-supplied themes
/// and interface locales, the validation that keeps a token map from smuggling
/// arbitrary CSS through a declaration, and the degrade-to-empty behaviour that
/// keeps one unusable file from costing the user the rest.
/// </summary>
public sealed class UserAssetsServiceTests : IDisposable
{
    private readonly TempDir _root = new();
    private readonly UserAssetsService _service;

    public UserAssetsServiceTests()
    {
        _service = new UserAssetsService(_root.Path);
    }

    public void Dispose() => _root.Dispose();

    private string WriteTheme(string fileName, string content)
    {
        Directory.CreateDirectory(_service.ThemesDirectory);
        var path = Path.Combine(_service.ThemesDirectory, fileName);
        File.WriteAllText(path, content);
        return path;
    }

    private void WriteLocale(string fileName, string content)
    {
        Directory.CreateDirectory(_service.LocalesDirectory);
        File.WriteAllText(Path.Combine(_service.LocalesDirectory, fileName), content);
    }

    // ── Directories ─────────────────────────────────────────────────────

    [Fact]
    public void Directories_HangOffTheSettingsRoot()
    {
        Assert.Equal(Path.Combine(_root.Path, "Themes"), _service.ThemesDirectory);
        Assert.Equal(Path.Combine(_root.Path, "Locales"), _service.LocalesDirectory);
        Assert.Equal(Path.Combine(_root.Path, "Analysis"), _service.AnalysisDirectory);
    }

    [Fact]
    public void DefaultRoot_HonoursTheSettingsDirEnvironmentVariable()
    {
        var previous = Environment.GetEnvironmentVariable("NOVALIST_SETTINGS_DIR");
        try
        {
            Environment.SetEnvironmentVariable("NOVALIST_SETTINGS_DIR", _root.Path);
            Assert.Equal(_root.Path, UserAssetsService.DefaultRoot());

            Environment.SetEnvironmentVariable("NOVALIST_SETTINGS_DIR", null);
            Assert.EndsWith("Novalist", UserAssetsService.DefaultRoot());
        }
        finally
        {
            Environment.SetEnvironmentVariable("NOVALIST_SETTINGS_DIR", previous);
        }
    }

    [Fact]
    public void DefaultConstructor_UsesTheDefaultRoot()
    {
        var previous = Environment.GetEnvironmentVariable("NOVALIST_SETTINGS_DIR");
        try
        {
            Environment.SetEnvironmentVariable("NOVALIST_SETTINGS_DIR", _root.Path);
            Assert.Equal(Path.Combine(_root.Path, "Themes"), new UserAssetsService().ThemesDirectory);
        }
        finally
        {
            Environment.SetEnvironmentVariable("NOVALIST_SETTINGS_DIR", previous);
        }
    }

    [Fact]
    public void EnsureDirectories_CreatesAllThree_AndIsIdempotent()
    {
        _service.EnsureDirectories();
        _service.EnsureDirectories();

        Assert.True(Directory.Exists(_service.ThemesDirectory));
        Assert.True(Directory.Exists(_service.LocalesDirectory));
        Assert.True(Directory.Exists(_service.AnalysisDirectory));
    }

    [Fact]
    public void EnsureDirectories_SwallowsAnUnusableRoot()
    {
        // A file where the folder should be: creating a child directory throws,
        // and startup must survive it.
        var blocked = Path.Combine(_root.Path, "blocked");
        File.WriteAllText(blocked, "not a directory");

        var service = new UserAssetsService(blocked);
        service.EnsureDirectories();

        Assert.False(Directory.Exists(service.ThemesDirectory));
    }

    // ── Theme discovery ─────────────────────────────────────────────────

    [Fact]
    public void DiscoverThemes_NoFolder_IsEmpty()
    {
        Assert.Empty(_service.DiscoverThemes());
    }

    [Fact]
    public void DiscoverThemes_ReadsTokenMapsAndStylesheets_OrderedByName()
    {
        WriteTheme("nord.json", """
            { "name": "Nord", "tokens": { "--nl-accent": "#88c0d0" } }
            """);
        WriteTheme("Amber.css", ":root { --nl-accent: #ffbf00; }");
        WriteTheme("notes.txt", "ignored");

        var themes = _service.DiscoverThemes();

        Assert.Collection(
            themes,
            amber =>
            {
                Assert.Equal("Amber", amber.Name);
                Assert.Equal("user-amber", amber.Slug);
                Assert.Empty(amber.Tokens);
                Assert.Equal(":root { --nl-accent: #ffbf00; }", amber.Css);
            },
            nord =>
            {
                Assert.Equal("Nord", nord.Name);
                Assert.Equal("user-nord", nord.Slug);
                Assert.Equal("#88c0d0", nord.Tokens["--nl-accent"]);
                Assert.Null(nord.Css);
            });
    }

    [Fact]
    public void DiscoverThemes_FallsBackToTheFileNameWhenNameIsMissing()
    {
        WriteTheme("sea-glass.json", """{ "tokens": { "--nl-accent": "#0ff" } }""");

        var theme = Assert.Single(_service.DiscoverThemes());
        Assert.Equal("sea-glass", theme.Name);
        Assert.Equal("user-sea-glass", theme.Slug);
    }

    [Fact]
    public void DiscoverThemes_SkipsUnusableFiles_WithoutLosingTheGoodOnes()
    {
        WriteTheme("broken.json", "{ not json");
        WriteTheme("empty.json", """{ "name": "Empty", "tokens": {} }""");
        WriteTheme("null.json", "null");
        WriteTheme("good.json", """{ "name": "Good", "tokens": { "--nl-text": "#fff" } }""");

        var theme = Assert.Single(_service.DiscoverThemes());
        Assert.Equal("Good", theme.Name);
    }

    [Fact]
    public void DiscoverThemes_DropsASecondFileClaimingTheSameSlug()
    {
        // Both stems slug to "user-nord". The winner is the first in
        // case-insensitive file-name order (".css" before ".json"), which makes
        // the outcome deterministic rather than filesystem-dependent.
        WriteTheme("nord.json", """{ "name": "Nord", "tokens": { "--nl-text": "#fff" } }""");
        WriteTheme("Nord.css", ":root { --nl-text: #000; }");

        var theme = Assert.Single(_service.DiscoverThemes());
        Assert.Equal("user-nord", theme.Slug);
        Assert.Equal(":root { --nl-text: #000; }", theme.Css);
        Assert.Empty(theme.Tokens);
    }

    [Fact]
    public void DiscoverThemes_SurvivesAnUnreadableFolder()
    {
        // The Themes path is a file, so enumeration throws rather than returning.
        Directory.CreateDirectory(_root.Path);
        File.WriteAllText(_service.ThemesDirectory, "not a directory");

        Assert.Empty(_service.DiscoverThemes());
    }

    [Fact]
    public void DiscoverThemes_SkipsAFileItCannotRead()
    {
        WriteTheme("good.json", """{ "name": "Good", "tokens": { "--nl-text": "#fff" } }""");
        var locked = WriteTheme("locked.json", """{ "tokens": { "--nl-text": "#000" } }""");

        // Held open exclusively: reading it throws, and the other theme must
        // still come through.
        using var hold = new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None);

        var theme = Assert.Single(_service.DiscoverThemes());
        Assert.Equal("Good", theme.Name);
    }

    // ── Token validation ────────────────────────────────────────────────

    [Theory]
    [InlineData("--nl-accent", true)]
    [InlineData("--nl-surface-window", true)]
    [InlineData("--nl-", false)]        // prefix only, names nothing
    [InlineData("--nv-gold", false)]    // brand layer is not overridable
    [InlineData("--other", false)]
    [InlineData("color", false)]
    [InlineData("--nl-a;b", false)]     // punctuation that could break the rule
    [InlineData(null, false)]
    public void IsTokenName_AcceptsOnlyTheNlTier(string? key, bool expected)
        => Assert.Equal(expected, UserAssetsService.IsTokenName(key));

    [Theory]
    [InlineData("#88c0d0", true)]
    [InlineData("rgb(136 192 208 / 0.6)", true)]
    [InlineData("linear-gradient(180deg, #eee, #ddd)", true)]
    [InlineData("red; } body { display: none", false)]
    [InlineData("red } body { display:none", false)]
    [InlineData("red /* comment", false)]
    [InlineData("comment */ red", false)]
    [InlineData("<script>", false)]
    [InlineData("   ", false)]
    [InlineData(null, false)]
    public void IsTokenValue_RejectsAnythingThatCouldEscapeTheDeclaration(string? value, bool expected)
        => Assert.Equal(expected, UserAssetsService.IsTokenValue(value));

    [Fact]
    public void IsTokenValue_RejectsAnAbsurdlyLongValue()
        => Assert.False(UserAssetsService.IsTokenValue(new string('a', 513)));

    [Fact]
    public void ParseTokenTheme_DropsInvalidEntriesButKeepsTheRest()
    {
        var theme = UserAssetsService.ParseTokenTheme("""
            {
              "name": "Mixed",
              "tokens": {
                "--nl-accent": "#88c0d0",
                "--nv-gold": "#fff",
                "--nl-text": "red; } body { display:none",
                "--nl-border": "  #333  "
              }
            }
            """, "mixed");

        Assert.NotNull(theme);
        Assert.Equal(2, theme.Tokens.Count);
        Assert.Equal("#88c0d0", theme.Tokens["--nl-accent"]);
        Assert.Equal("#333", theme.Tokens["--nl-border"]);
    }

    [Fact]
    public void ParseTokenTheme_MissingTokensSection_IsSkipped()
        => Assert.Null(UserAssetsService.ParseTokenTheme("""{ "name": "Bare" }""", "bare"));

    [Theory]
    [InlineData("Nord", "user-nord")]
    [InlineData("Sea Glass", "user-sea-glass")]
    [InlineData("  Café  Noir ", "user-caf-noir")]
    [InlineData("!!!", "user-theme")]
    [InlineData("", "user-theme")]
    public void Slugify_ProducesACssSafePrefixedSlug(string name, string expected)
        => Assert.Equal(expected, UserAssetsService.Slugify(name));

    // ── Locale discovery ────────────────────────────────────────────────

    [Fact]
    public void DiscoverLocales_NoFolder_IsEmpty()
        => Assert.Empty(_service.DiscoverLocales());

    [Fact]
    public void DiscoverLocales_ReadsCodeFromFileName_AndNameFromContent()
    {
        WriteLocale("pt-BR.json", """{ "language": { "name": "Português" } }""");
        WriteLocale("fr.json", """{ "language": { "name": "Français" } }""");

        var locales = _service.DiscoverLocales();

        Assert.Collection(
            locales,
            fr =>
            {
                Assert.Equal("fr", fr.Code);
                Assert.Equal("Français", fr.Name);
                Assert.Contains("Français", fr.Json);
            },
            pt =>
            {
                Assert.Equal("pt-BR", pt.Code);
                Assert.Equal("Português", pt.Name);
            });
    }

    [Fact]
    public void DiscoverLocales_FallsBackToTheCodeWhenTheFileNamesNoLanguage()
    {
        WriteLocale("eo.json", """{ "shell": { "binder": "Bindilo" } }""");

        var locale = Assert.Single(_service.DiscoverLocales());
        Assert.Equal("eo", locale.Code);
        Assert.Equal("eo", locale.Name);
    }

    [Fact]
    public void DiscoverLocales_SkipsMalformedAndMisnamedFiles()
    {
        WriteLocale("broken.json", "{ not json");
        WriteLocale("readme.txt", "ignored");
        WriteLocale("a.json", "{}");                      // one-letter code
        WriteLocale("not a tag!.json", "{}");
        WriteLocale("array.json", "[1, 2]");              // not an object
        WriteLocale("de.json", """{ "language": { "name": "Deutsch" } }""");

        var locale = Assert.Single(_service.DiscoverLocales());
        Assert.Equal("de", locale.Code);
    }

    [Theory]
    [InlineData("fr", true)]
    [InlineData("pt-BR", true)]
    [InlineData("zh-Hans-CN", true)]
    [InlineData("a", false)]
    [InlineData("toolongsegment", false)]
    [InlineData("en-US-x-private", false)]
    [InlineData("en_US", false)]
    public void IsLanguageCode_AcceptsTagsNovalistCanUse(string code, bool expected)
        => Assert.Equal(expected, UserAssetsService.IsLanguageCode(code));

    [Fact]
    public void ReadLanguageName_HandlesTheShapesAFileCanTake()
    {
        Assert.Equal("Deutsch", UserAssetsService.ReadLanguageName("""{"language":{"name":"Deutsch"}}"""));
        Assert.Equal(string.Empty, UserAssetsService.ReadLanguageName("""{"language":{"code":"de"}}"""));
        Assert.Equal(string.Empty, UserAssetsService.ReadLanguageName("""{"language":"de"}"""));
        Assert.Equal(string.Empty, UserAssetsService.ReadLanguageName("{}"));
        Assert.Null(UserAssetsService.ReadLanguageName("[]"));
        Assert.Null(UserAssetsService.ReadLanguageName("{ not json"));
    }

    [Fact]
    public void DiscoverLocales_SurvivesAnUnreadableFolder()
    {
        Directory.CreateDirectory(_root.Path);
        File.WriteAllText(_service.LocalesDirectory, "not a directory");

        Assert.Empty(_service.DiscoverLocales());
    }
}
