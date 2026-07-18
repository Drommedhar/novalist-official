using System.Text.Json;
using Novalist.Backend;
using Novalist.Backend.Rpc;
using Xunit;

namespace Novalist.Backend.Tests;

public sealed class SettingsRpcTests : IDisposable
{
    private readonly string _root;
    private readonly Workspace _workspace;
    private readonly SettingsRpc _rpc;

    public SettingsRpcTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "nl-set-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _workspace = new Workspace(Path.Combine(_root, "settings"));
        _rpc = new SettingsRpc(_workspace);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private async Task OpenProjectAsync()
    {
        await _workspace.Projects.CreateProjectAsync(_root, "SetNovel", "Book");
        await _workspace.OpenProjectAsync(_workspace.Projects.ProjectRoot!);
    }

    private static Dictionary<string, JsonElement> Patch(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;

    [Fact]
    public async Task Get_WithoutProject_HasNoOverrides()
    {
        var view = await _rpc.GetAsync();
        Assert.False(view.GetProperty("hasProject").GetBoolean());
        Assert.Equal(JsonValueKind.Null, view.GetProperty("overrides").ValueKind);
        Assert.Equal("en", view.GetProperty("effective").GetProperty("language").GetString());
    }

    [Fact]
    public async Task UpdateGlobal_ChangesEffective()
    {
        var view = await _rpc.UpdateGlobalAsync(Patch(
            """{"editorFontSize": 18, "theme": "Discord", "grammarCheckEnabled": false, "unknownKey": "ignored"}"""));

        var effective = view.GetProperty("effective");
        Assert.Equal(18, effective.GetProperty("editorFontSize").GetDouble());
        Assert.Equal("Discord", effective.GetProperty("theme").GetString());
        Assert.False(effective.GetProperty("grammarCheckEnabled").GetBoolean());
    }

    [Fact]
    public async Task ProjectOverrides_WinOverGlobal_AndClearSectionReverts()
    {
        await OpenProjectAsync();
        await _rpc.UpdateGlobalAsync(Patch("""{"editorFontFamily": "Inter"}"""));

        var overridden = await _rpc.UpdateProjectAsync(Patch(
            """{"editorFontFamily": "Georgia", "editorFontSize": 16}"""));
        Assert.Equal("Georgia",
            overridden.GetProperty("effective").GetProperty("editorFontFamily").GetString());
        Assert.True(overridden.GetProperty("hasProject").GetBoolean());

        var cleared = await _rpc.ClearSectionAsync("editor");
        Assert.Equal("Inter",
            cleared.GetProperty("effective").GetProperty("editorFontFamily").GetString());
    }

    [Fact]
    public async Task ClearSection_AllSections_AndErrors()
    {
        await OpenProjectAsync();
        await _rpc.UpdateProjectAsync(Patch(
            """{"language": "de", "dialogueCorrectionEnabled": true, "accentColor": null}"""));
        await _rpc.ClearSectionAsync("appearance");
        await _rpc.ClearSectionAsync("writing");

        var view = await _rpc.GetAsync();
        Assert.Equal("en", view.GetProperty("effective").GetProperty("language").GetString());

        await Assert.ThrowsAsync<InvalidOperationException>(() => _rpc.ClearSectionAsync("bogus"));
    }

    [Fact]
    public async Task UpdateProject_WithoutProject_Throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _rpc.UpdateProjectAsync(Patch("""{"language": "de"}""")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _rpc.ClearSectionAsync("editor"));
    }

    [Fact]
    public void Apply_UnsupportedPropertyType_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => SettingsRpc.Apply(
            new Novalist.Core.Models.AppSettings(),
            Patch("""{"recentProjects": "not-a-list"}""")));
    }
}
