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
        await Assert.ThrowsAsync<InvalidOperationException>(() => _rpc.PinSectionAsync("editor"));
    }

    // ── Per-section project-override switch ─────────────────────────────

    private static bool Overridden(JsonElement view, string section)
        => view.GetProperty("overriddenSections").GetProperty(section).GetBoolean();

    [Fact]
    public async Task OverriddenSections_IsNullWithoutProject()
    {
        var view = await _rpc.GetAsync();
        Assert.Equal(JsonValueKind.Null, view.GetProperty("overriddenSections").ValueKind);
    }

    [Fact]
    public async Task OverriddenSections_StartFalse_AndFollowWhatIsStored()
    {
        await OpenProjectAsync();

        var fresh = await _rpc.GetAsync();
        Assert.False(Overridden(fresh, "appearance"));
        Assert.False(Overridden(fresh, "editor"));
        Assert.False(Overridden(fresh, "writing"));

        var edited = await _rpc.UpdateProjectAsync(Patch("""{"language": "de"}"""));
        Assert.True(Overridden(edited, "appearance"));
        Assert.False(Overridden(edited, "editor"));
    }

    [Fact]
    public async Task OverriddenSections_SurviveReopeningTheProject()
    {
        await OpenProjectAsync();
        await _rpc.PinSectionAsync("appearance");
        var root = _workspace.Projects.ProjectRoot!;

        // Reopen from disk: the switch must reflect the stored override, which is
        // what a fresh Settings visit reads.
        await _workspace.OpenProjectAsync(root);

        Assert.True(Overridden(await _rpc.GetAsync(), "appearance"));
    }

    [Fact]
    public async Task PinSection_CopiesTheValuesInEffect_AndDetachesFromGlobal()
    {
        await OpenProjectAsync();
        await _rpc.UpdateGlobalAsync(Patch("""{"editorFontFamily": "Inter", "editorFontSize": 14}"""));

        var pinned = await _rpc.PinSectionAsync("editor");
        Assert.True(Overridden(pinned, "editor"));
        Assert.Equal("Inter", pinned.GetProperty("overrides").GetProperty("editorFontFamily").GetString());

        // The point of pinning: a later change to the global default no longer
        // reaches this project.
        var afterGlobal = await _rpc.UpdateGlobalAsync(Patch("""{"editorFontFamily": "Georgia"}"""));
        Assert.Equal("Inter",
            afterGlobal.GetProperty("effective").GetProperty("editorFontFamily").GetString());
    }

    [Fact]
    public async Task PinSection_ThenClear_RestoresTheGlobalValues()
    {
        await OpenProjectAsync();
        await _rpc.UpdateGlobalAsync(Patch(
            """{"language": "en", "theme": "Default", "editorFontSize": 14, "grammarCheckEnabled": true}"""));

        await _rpc.PinSectionAsync("appearance");
        await _rpc.PinSectionAsync("editor");
        await _rpc.PinSectionAsync("writing");
        await _rpc.UpdateProjectAsync(Patch(
            """{"language": "de", "editorFontSize": 20, "grammarCheckEnabled": false}"""));

        var overridden = await _rpc.GetAsync();
        Assert.Equal("de", overridden.GetProperty("effective").GetProperty("language").GetString());

        await _rpc.ClearSectionAsync("appearance");
        await _rpc.ClearSectionAsync("editor");
        var view = await _rpc.ClearSectionAsync("writing");

        // Unticking every switch puts the project back on the global values.
        var effective = view.GetProperty("effective");
        Assert.Equal("en", effective.GetProperty("language").GetString());
        Assert.Equal("Default", effective.GetProperty("theme").GetString());
        Assert.Equal(14, effective.GetProperty("editorFontSize").GetDouble());
        Assert.True(effective.GetProperty("grammarCheckEnabled").GetBoolean());
        Assert.False(Overridden(view, "appearance"));
        Assert.False(Overridden(view, "editor"));
        Assert.False(Overridden(view, "writing"));
    }

    [Fact]
    public async Task PinSection_IsIdempotent_AndRejectsAnUnknownSection()
    {
        await OpenProjectAsync();

        await _rpc.PinSectionAsync("writing");
        var twice = await _rpc.PinSectionAsync("writing");
        Assert.True(Overridden(twice, "writing"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => _rpc.PinSectionAsync("bogus"));
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
    public async Task UpdateProjectMeta_PersistsWordGoals()
    {
        await OpenProjectAsync();
        var view = await _rpc.UpdateProjectMetaAsync(Patch(
            """{"dailyGoal": 1200, "projectGoal": 90000}"""));

        var project = view.GetProperty("project");
        Assert.Equal(1200, project.GetProperty("dailyGoal").GetInt32());
        Assert.Equal(90000, project.GetProperty("projectGoal").GetInt32());

        // A negative goal is nonsense; it clamps rather than being stored.
        var clamped = await _rpc.UpdateProjectMetaAsync(Patch(
            """{"dailyGoal": -5, "projectGoal": -1}"""));
        Assert.Equal(0, clamped.GetProperty("project").GetProperty("dailyGoal").GetInt32());
        Assert.Equal(0, clamped.GetProperty("project").GetProperty("projectGoal").GetInt32());
    }

    [Fact]
    public async Task UpdateProjectMeta_PersistsTheLongerHorizons()
    {
        await OpenProjectAsync();

        // Off until asked for: nobody is handed a weekly budget they did not set.
        var initial = await _rpc.GetAsync();
        Assert.Equal(0, initial.GetProperty("project").GetProperty("weeklyGoal").GetInt32());

        var view = await _rpc.UpdateProjectMetaAsync(Patch(
            """{"weeklyGoal": 4000, "monthlyGoal": 16000}"""));
        Assert.Equal(4000, view.GetProperty("project").GetProperty("weeklyGoal").GetInt32());
        Assert.Equal(16000, view.GetProperty("project").GetProperty("monthlyGoal").GetInt32());

        var clamped = await _rpc.UpdateProjectMetaAsync(Patch(
            """{"weeklyGoal": -1, "monthlyGoal": -1}"""));
        Assert.Equal(0, clamped.GetProperty("project").GetProperty("weeklyGoal").GetInt32());
        Assert.Equal(0, clamped.GetProperty("project").GetProperty("monthlyGoal").GetInt32());
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
