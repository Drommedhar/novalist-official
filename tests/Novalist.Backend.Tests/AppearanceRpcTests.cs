using Novalist.Backend.Rpc;
using Novalist.Backend.Tests.TestHelpers;
using Xunit;

namespace Novalist.Backend.Tests;

/// <summary>
/// Covers <see cref="AppearanceRpc"/> — the renderer's view of the user's
/// dropped themes and interface locales, and the folder paths behind the
/// "open folder" buttons in Settings.
/// </summary>
[Collection(LexiconStaticsCollection.Name)]
public sealed class AppearanceRpcTests : IDisposable
{
    private readonly TempDir _root = new();
    private readonly Workspace _workspace;
    private readonly AppearanceRpc _rpc;

    public AppearanceRpcTests()
    {
        _workspace = new Workspace(_root.Path);
        _rpc = new AppearanceRpc(_workspace);
    }

    public void Dispose()
    {
        _workspace.Dispose();
        _root.Dispose();
    }

    [Fact]
    public void Directories_AreTheFoldersTheServiceScans()
    {
        var dirs = _rpc.Directories();

        Assert.Equal(_workspace.UserAssets.ThemesDirectory, dirs.Themes);
        Assert.Equal(_workspace.UserAssets.LocalesDirectory, dirs.Locales);
        Assert.Equal(_workspace.UserAssets.AnalysisDirectory, dirs.Analysis);
    }

    [Fact]
    public void Workspace_CreatesTheAssetFoldersOnConstruction()
    {
        Assert.True(Directory.Exists(_workspace.UserAssets.ThemesDirectory));
        Assert.True(Directory.Exists(_workspace.UserAssets.LocalesDirectory));
        Assert.True(Directory.Exists(_workspace.UserAssets.AnalysisDirectory));
    }

    [Fact]
    public void Themes_AreEmptyUntilTheUserDropsOne()
        => Assert.Empty(_rpc.Themes());

    [Fact]
    public void Themes_SurfaceTokenMapsAndStylesheets()
    {
        File.WriteAllText(
            Path.Combine(_workspace.UserAssets.ThemesDirectory, "nord.json"),
            """{ "name": "Nord", "tokens": { "--nl-accent": "#88c0d0" } }""");
        File.WriteAllText(
            Path.Combine(_workspace.UserAssets.ThemesDirectory, "zinc.css"),
            ":root { --nl-accent: #71717a; }");

        var themes = _rpc.Themes();

        var nord = Assert.Single(themes, t => t.Name == "Nord");
        Assert.Equal("user-nord", nord.Slug);
        Assert.Equal("#88c0d0", nord.Tokens["--nl-accent"]);
        Assert.Null(nord.Css);

        var zinc = Assert.Single(themes, t => t.Name == "zinc");
        Assert.Empty(zinc.Tokens);
        Assert.Equal(":root { --nl-accent: #71717a; }", zinc.Css);
    }

    [Fact]
    public void Locales_AreEmptyUntilTheUserDropsOne()
        => Assert.Empty(_rpc.Locales());

    [Fact]
    public void Locales_SurfaceCodeNameAndRawJson()
    {
        const string json = """{ "language": { "name": "Français" }, "shell": { "binder": "Classeur" } }""";
        File.WriteAllText(Path.Combine(_workspace.UserAssets.LocalesDirectory, "fr.json"), json);

        var locale = Assert.Single(_rpc.Locales());
        Assert.Equal("fr", locale.Code);
        Assert.Equal("Français", locale.Name);
        Assert.Equal(json, locale.Json);
    }

    // ── Design-token overrides ──

    [Fact]
    public void NothingIsOverriddenUntilTheWriterChangesSomething()
        => Assert.Empty(_rpc.Tokens());

    [Fact]
    public async Task OverridesRoundTrip()
    {
        var saved = await _rpc.SetTokensAsync(new Dictionary<string, string>
        {
            ["nl-accent"] = "#c04040",
            ["nl-radius-md"] = "10px"
        });

        Assert.Equal("#c04040", saved["nl-accent"]);
        Assert.Equal("10px", saved["nl-radius-md"]);
        Assert.Equal(saved, _rpc.Tokens());
    }

    [Fact]
    public async Task TheLeadingDashesAreNotStored()
    {
        // The editor sends what the stylesheet calls the token; storing both
        // spellings would make two entries for one thing.
        var saved = await _rpc.SetTokensAsync(new Dictionary<string, string>
        {
            ["--nl-accent"] = "#c04040"
        });

        Assert.Equal("#c04040", Assert.Single(saved).Value);
        Assert.Equal("nl-accent", saved.Keys.Single());
    }

    [Fact]
    public async Task ValuesAreTrimmed()
        => Assert.Equal(
            "#c04040",
            (await _rpc.SetTokensAsync(new Dictionary<string, string>
            {
                ["nl-accent"] = "  #c04040  "
            }))["nl-accent"]);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ATokenSetToNothingIsNotAnOverride(string value)
    {
        // It is the theme's value, and storing it would pin today's colour into
        // the settings file for ever.
        Assert.Empty(await _rpc.SetTokensAsync(new Dictionary<string, string>
        {
            ["nl-accent"] = value
        }));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("--")]
    public async Task ANamelessTokenIsDropped(string name)
        => Assert.Empty(await _rpc.SetTokensAsync(new Dictionary<string, string>
        {
            [name] = "#c04040"
        }));

    [Fact]
    public async Task SendingNothingClearsEverything()
    {
        await _rpc.SetTokensAsync(new Dictionary<string, string> { ["nl-accent"] = "#c04040" });

        Assert.Empty(await _rpc.SetTokensAsync(null));
        Assert.Empty(_rpc.Tokens());
    }

    [Fact]
    public async Task OverridesSurviveAReload()
    {
        await _rpc.SetTokensAsync(new Dictionary<string, string> { ["nl-accent"] = "#c04040" });

        await _workspace.Settings.LoadAsync();

        Assert.Equal("#c04040", _rpc.Tokens()["nl-accent"]);
    }
}
