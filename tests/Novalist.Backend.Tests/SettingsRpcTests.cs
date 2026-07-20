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

    [Fact]
    public async Task Get_WithProject_ExposesProjectMeta()
    {
        await OpenProjectAsync();
        var view = await _rpc.GetAsync();
        var project = view.GetProperty("project");
        Assert.Equal(JsonValueKind.Object, project.ValueKind);
        Assert.Equal(string.Empty, project.GetProperty("author").GetString());
        Assert.True(project.GetProperty("watchFilesystem").GetBoolean());
        Assert.Equal(JsonValueKind.Null, project.GetProperty("deadline").ValueKind);
    }

    [Fact]
    public async Task Get_WithoutProject_HasNullProjectMeta()
    {
        var view = await _rpc.GetAsync();
        Assert.Equal(JsonValueKind.Null, view.GetProperty("project").ValueKind);
    }

    [Fact]
    public async Task UpdateProjectMeta_PersistsAuthorWatchAndDeadline()
    {
        await OpenProjectAsync();
        var view = await _rpc.UpdateProjectMetaAsync(Patch(
            """{"author": "Jane Doe", "watchFilesystem": false, "deadline": "2026-12-31"}"""));

        var project = view.GetProperty("project");
        Assert.Equal("Jane Doe", project.GetProperty("author").GetString());
        Assert.False(project.GetProperty("watchFilesystem").GetBoolean());
        Assert.Equal("2026-12-31", project.GetProperty("deadline").GetString());

        // Blank / null values clear back to the neutral state.
        var cleared = await _rpc.UpdateProjectMetaAsync(Patch(
            """{"author": null, "deadline": "  "}"""));
        Assert.Equal(string.Empty, cleared.GetProperty("project").GetProperty("author").GetString());
        Assert.Equal(JsonValueKind.Null, cleared.GetProperty("project").GetProperty("deadline").ValueKind);
    }

    [Fact]
    public async Task UpdateProjectMeta_UnknownKey_Throws_AndWithoutProject_Throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _rpc.UpdateProjectMetaAsync(Patch("""{"author": "x"}""")));

        await OpenProjectAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _rpc.UpdateProjectMetaAsync(Patch("""{"bogus": "x"}""")));
    }

    [Fact]
    public async Task HotkeyBindings_SetResetAndResetAll()
    {
        var set = await _rpc.SetHotkeyBindingAsync("app.nav.dashboard", "Ctrl+Alt+D");
        Assert.Equal("Ctrl+Alt+D",
            set.GetProperty("global").GetProperty("hotkeyBindings").GetProperty("app.nav.dashboard").GetString());

        await _rpc.SetHotkeyBindingAsync("app.nav.timeline", "Ctrl+Alt+T");
        var reset = await _rpc.ResetHotkeyBindingAsync("app.nav.dashboard");
        var bindings = reset.GetProperty("global").GetProperty("hotkeyBindings");
        Assert.False(bindings.TryGetProperty("app.nav.dashboard", out _));
        Assert.True(bindings.TryGetProperty("app.nav.timeline", out _));

        var all = await _rpc.ResetAllHotkeysAsync();
        Assert.Empty(all.GetProperty("global").GetProperty("hotkeyBindings").EnumerateObject());
    }

    [Fact]
    public void LogInfo_And_ClearLogs_HandleMissingPopulatedAndEmptyDirectories()
    {
        // Missing directory: reported as-is, no current log, nothing to clear.
        var missing = Path.Combine(_root, "no-such-logs");
        Assert.Equal(new LogInfoDto(missing, null), SettingsRpc.ResolveLogInfo(missing));
        Assert.Equal(0, SettingsRpc.ClearLogFiles(missing));

        // Empty directory: exists but has no log file.
        var empty = Path.Combine(_root, "empty-logs");
        Directory.CreateDirectory(empty);
        Assert.Null(SettingsRpc.ResolveLogInfo(empty).CurrentLog);

        // Populated directory: newest .log wins, and clearing removes them all.
        var logs = Path.Combine(_root, "logs");
        Directory.CreateDirectory(logs);
        var older = Path.Combine(logs, "2026-01-01.log");
        var newer = Path.Combine(logs, "2026-01-02.log");
        File.WriteAllText(older, "a");
        File.WriteAllText(newer, "b");
        File.SetLastWriteTimeUtc(older, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(newer, new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc));
        Assert.Equal(newer, SettingsRpc.ResolveLogInfo(logs).CurrentLog);
        Assert.Equal(2, SettingsRpc.ClearLogFiles(logs));
        Assert.Empty(Directory.EnumerateFiles(logs, "*.log"));
    }

    [Fact]
    public void LogInfo_And_ClearLogs_RpcMethods_UseOverrideAndDefault()
    {
        // Default (no override): reads the OS app-data path read-only; must not throw.
        SettingsRpc.LogsDirectoryOverride = null;
        var defaultInfo = _rpc.LogInfo();
        Assert.EndsWith(Path.Combine("Novalist", "logs"), defaultInfo.Directory);

        // Overridden to a temp directory for the mutating path.
        var dir = Path.Combine(_root, "rpc-logs");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "run.log"), "x");
        try
        {
            SettingsRpc.LogsDirectoryOverride = dir;
            Assert.Equal(dir, _rpc.LogInfo().Directory);
            Assert.Equal(1, _rpc.ClearLogs());
        }
        finally
        {
            SettingsRpc.LogsDirectoryOverride = null;
        }
    }
}
