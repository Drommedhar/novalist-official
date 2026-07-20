using Novalist.Backend;
using Novalist.Backend.Extensions;
using Novalist.Backend.Rpc;
using Novalist.Backend.Tests.TestHelpers;
using Novalist.Sdk.Models;
using Xunit;

namespace Novalist.Backend.Tests;

/// <summary>
/// Covers <see cref="ExtensionContribRpc"/> — the RPC surface that exposes
/// inline actions, editor context-menu items, hotkeys, themes, status-bar items,
/// and declarative settings schemas to the renderer, plus the callback routing.
/// </summary>
[Collection("Avalonia")]
public sealed class ExtensionContribRpcTests : IDisposable
{
    private const string SampleId = "com.novalist.writingtoolkit";

    private readonly string _root;
    private readonly TempDir _extDir = new();
    private readonly Workspace _workspace;
    private readonly ExtensionContribRpc _rpc;
    private string _chapterGuid = string.Empty;
    private string _sceneId = string.Empty;

    public ExtensionContribRpcTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "nl-contrib-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        DeploySample(_extDir.Path);

        _workspace = new Workspace(Path.Combine(_root, "settings"));
        _workspace.Projects.CreateProjectAsync(_root, "N", "B").GetAwaiter().GetResult();
        _workspace.OpenProjectAsync(_workspace.Projects.ProjectRoot!).GetAwaiter().GetResult();
        _workspace.ExtensionsLoaderOverride = new ExtensionLoader(_extDir.Path);
        _workspace.ExtensionsHost.LoadAllAsync().GetAwaiter().GetResult();

        var chapter = _workspace.Projects.CreateChapterAsync("C").GetAwaiter().GetResult();
        var scene = _workspace.Projects.CreateSceneAsync(chapter.Guid, "S").GetAwaiter().GetResult();
        _chapterGuid = chapter.Guid;
        _sceneId = scene.Id;

        _rpc = new ExtensionContribRpc(_workspace);
    }

    public void Dispose()
    {
        _workspace.Dispose();
        _extDir.Dispose();
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private static void DeploySample(string extRoot)
    {
        var folder = Path.Combine(extRoot, "Sample");
        Directory.CreateDirectory(folder);
        var dll = Path.Combine(AppContext.BaseDirectory, "Novalist.Sdk.Example.dll");
        File.Copy(dll, Path.Combine(folder, "Novalist.Sdk.Example.dll"));
        File.WriteAllText(Path.Combine(folder, "extension.json"),
            $$"""{ "id": "{{SampleId}}", "name": "Sample", "entryAssembly": "Novalist.Sdk.Example.dll" }""");
    }

    // A workspace that never touches its extension host (ExtensionHostOrNull null).
    private static ExtensionContribRpc NoHostRpc(out Workspace ws)
    {
        ws = new Workspace(Path.Combine(Path.GetTempPath(), "nl-nohost-" + Guid.NewGuid().ToString("N")));
        return new ExtensionContribRpc(ws);
    }

    // ── Inline actions ──────────────────────────────────────────────

    [Fact]
    public void InlineActions_Surfaced()
    {
        var actions = _rpc.InlineActions();
        Assert.Equal(2, actions.Length);
        Assert.Equal("ext.writingtoolkit.uppercase", actions[0].Id);
    }

    [Fact]
    public void InlineActions_NoHost_Empty()
    {
        var rpc = NoHostRpc(out var ws);
        using (ws) Assert.Empty(rpc.InlineActions());
    }

    [Fact]
    public async Task ExecuteInlineAction_Replace_Insert_Unknown_NoHost()
    {
        var replace = await _rpc.ExecuteInlineActionAsync(
            "ext.writingtoolkit.uppercase", "hi", _chapterGuid, _sceneId, CancellationToken.None);
        Assert.Equal("HI", replace!.Text);
        Assert.Equal("replace", replace.Disposition);

        var insert = await _rpc.ExecuteInlineActionAsync(
            "ext.writingtoolkit.wordcount", "a b", _chapterGuid, _sceneId, CancellationToken.None);
        Assert.Equal("insertAfter", insert!.Disposition);

        Assert.Null(await _rpc.ExecuteInlineActionAsync(
            "nope", "x", null, null, CancellationToken.None));

        var rpc = NoHostRpc(out var ws);
        using (ws)
            Assert.Null(await rpc.ExecuteInlineActionAsync("x", "y", null, null, CancellationToken.None));
    }

    // ── Context menu ────────────────────────────────────────────────

    [Fact]
    public void ContextMenuItems_Surfaced_AndNoHostEmpty()
    {
        var items = _rpc.ContextMenuItems();
        Assert.Equal(2, items.Length);

        var rpc = NoHostRpc(out var ws);
        using (ws) Assert.Empty(rpc.ContextMenuItems());
    }

    [Fact]
    public void ExecuteContextMenuItem_RunsAgainstScene_AndChapter_AndNoHost()
    {
        var activations = 0;
        _workspace.HostServices!.ContentViewActivated += (_, _) => activations++;

        var items = _rpc.ContextMenuItems();
        var sceneItem = items.First(i => i.Context == "Scene");
        var chapterItem = items.First(i => i.Context == "Chapter");

        // Scene item: a real scene context makes it visible + runs.
        _rpc.ExecuteContextMenuItem(sceneItem.Id, _chapterGuid, _sceneId);
        Assert.Equal(1, activations);

        // Chapter item with empty guids (BuildSceneContext returns null) still runs.
        _rpc.ExecuteContextMenuItem(chapterItem.Id, string.Empty, string.Empty);
        Assert.Equal(2, activations);

        var rpc = NoHostRpc(out var ws);
        using (ws) rpc.ExecuteContextMenuItem("x", null, null); // no-op, no throw
    }

    // ── Hotkeys ─────────────────────────────────────────────────────

    [Fact]
    public void Hotkeys_SurfacedFromRegistry()
    {
        Assert.Contains(_rpc.Hotkeys(), h => h.ActionId == "ext.writingtoolkit.wordfreq");
    }

    [Fact]
    public void ExecuteHotkey_Unknown_Guarded_AndSuccess()
    {
        Assert.False(_rpc.ExecuteHotkey("no.such.hotkey"));

        var ran = false;
        HotkeyRegistry.Register(new HotkeyDescriptor
        {
            ActionId = "test.ct.blocked",
            OnExecute = () => ran = true,
            CanExecute = () => false
        });
        HotkeyRegistry.Register(new HotkeyDescriptor
        {
            ActionId = "test.ct.ok",
            OnExecute = () => ran = true
        });
        try
        {
            Assert.False(_rpc.ExecuteHotkey("test.ct.blocked"));
            Assert.False(ran);
            Assert.True(_rpc.ExecuteHotkey("test.ct.ok"));
            Assert.True(ran);
        }
        finally
        {
            HotkeyRegistry.Unregister("test.ct.blocked");
            HotkeyRegistry.Unregister("test.ct.ok");
        }
    }

    // ── Themes ──────────────────────────────────────────────────────

    [Fact]
    public void Themes_Surfaced_AndNoHostEmpty()
    {
        Assert.Equal(2, _rpc.Themes().Length);
        var rpc = NoHostRpc(out var ws);
        using (ws) Assert.Empty(rpc.Themes());
    }

    // ── Status bar ──────────────────────────────────────────────────

    [Fact]
    public void StatusBar_Surfaced_Execute_AndNoHostEmpty()
    {
        var items = _rpc.StatusBarItems();
        var item = Assert.Single(items);
        Assert.NotNull(item.Tooltip);
        Assert.True(item.HasCommand);

        _rpc.ExecuteStatusBarItem(item.Id);
        _rpc.ExecuteStatusBarItem(item.Id); // toggle back

        var rpc = NoHostRpc(out var ws);
        using (ws)
        {
            Assert.Empty(rpc.StatusBarItems());
            rpc.ExecuteStatusBarItem("x"); // no-op
        }
    }

    [Fact]
    public void Safe_SwallowsThrow()
    {
        Assert.Equal(string.Empty, ExtensionContribRpc.Safe(() => throw new InvalidOperationException()));
        Assert.Equal("ok", ExtensionContribRpc.Safe(() => "ok"));
    }

    // ── Settings schema ─────────────────────────────────────────────

    [Fact]
    public async Task SettingsSchema_Surfaced_Saved_AndNoHost()
    {
        var schemas = _rpc.SettingsSchemas();
        var schema = Assert.Single(schemas);
        Assert.Equal(SampleId, schema.ExtensionId);
        Assert.Contains(schema.Fields, f => f.Key == "duration");

        // Conditional-visibility metadata flows through to the DTO: a field with no
        // condition carries nulls; a gated field carries its key + allowed values.
        var duration = schema.Fields.First(f => f.Key == "duration");
        Assert.Null(duration.VisibleWhenKey);
        Assert.Null(duration.VisibleWhenValues);
        var gated = schema.Fields.First(f => f.Key == "promptCategory");
        Assert.Equal("autoStartBreaks", gated.VisibleWhenKey);
        Assert.Equal(new[] { "true" }, gated.VisibleWhenValues);

        await _rpc.SaveSettingsSchemaAsync(SampleId, new Dictionary<string, string> { ["duration"] = "45" });
        Assert.Equal("45", _rpc.SettingsSchemas().Single().Fields.First(f => f.Key == "duration").Value);

        // Null values dictionary is tolerated.
        await _rpc.SaveSettingsSchemaAsync(SampleId, null!);

        var rpc = NoHostRpc(out var ws);
        using (ws)
        {
            Assert.Empty(rpc.SettingsSchemas());
            await rpc.SaveSettingsSchemaAsync("x", new Dictionary<string, string>());
        }
    }

    [Fact]
    public async Task SettingsSchemaAction_RefreshesSuggestions_AndHandlesMisses()
    {
        // The "suggestKeywords" action fills the promptKeyword field's suggestions.
        var before = _rpc.SettingsSchemas().Single().Fields.First(f => f.Key == "promptKeyword");
        Assert.Empty(before.Suggestions!);

        var refreshed = await _rpc.ExecuteSettingsSchemaActionAsync(
            SampleId, "suggestKeywords", new Dictionary<string, string>());
        Assert.NotNull(refreshed);
        var keyword = refreshed!.Fields.First(f => f.Key == "promptKeyword");
        Assert.Contains("mystery", keyword.Suggestions!);
        // The action button itself is surfaced as an 'action'-typed field.
        Assert.Contains(refreshed.Fields, f => f.Key == "suggestKeywords" && f.Type == "action");

        // Unknown action key -> the extension leaves the form unchanged (null).
        Assert.Null(await _rpc.ExecuteSettingsSchemaActionAsync(
            SampleId, "nope", new Dictionary<string, string>()));
        // Unknown extension id -> null.
        Assert.Null(await _rpc.ExecuteSettingsSchemaActionAsync(
            "not.an.extension", "suggestKeywords", new Dictionary<string, string>()));

        // No host -> null.
        var rpc = NoHostRpc(out var ws);
        using (ws)
        {
            Assert.Null(await rpc.ExecuteSettingsSchemaActionAsync(
                SampleId, "suggestKeywords", null!));
        }
    }
}
