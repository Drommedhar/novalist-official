using Novalist.Backend.Rpc;
using Novalist.Backend.Tests.TestHelpers;
using Xunit;

namespace Novalist.Backend.Tests;

/// <summary>
/// Covers <see cref="AppearanceRpc"/> — the renderer's view of the user's
/// dropped themes and interface locales, and the folder paths behind the
/// "open folder" buttons in Settings.
/// </summary>
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
}
