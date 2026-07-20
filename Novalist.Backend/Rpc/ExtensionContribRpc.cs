using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Novalist.Sdk.Hooks;
using Novalist.Sdk.Models;
using Novalist.Sdk.Services;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

/// <summary>
/// Surfaces the declarative extension contributions the new (Electron) host
/// renders itself — inline actions, editor context-menu items, hotkeys, themes,
/// status-bar items, and declarative settings schemas — and routes the callbacks
/// that cannot cross the RPC boundary back to the owning contributor by an opaque
/// id. Mirrors the wiring the frozen Avalonia host did inline in its views.
/// </summary>
public sealed class ExtensionContribRpc
{
    private readonly Workspace _workspace;

    public ExtensionContribRpc(Workspace workspace)
    {
        _workspace = workspace;
    }

    private Extensions.ExtensionManager? Host => _workspace.ExtensionHostOrNull;

    // ── Inline actions (item 1) ─────────────────────────────────────────

    [JsonRpcMethod("extensions/inlineActions")]
    public InlineActionInfoDto[] InlineActions()
        => Host?.GetInlineActionDescriptors()
               .Select(a => new InlineActionInfoDto(a.Id, a.Label, a.Group, a.Icon, a.Priority))
               .ToArray()
           ?? [];

    [JsonRpcMethod("extensions/inlineAction/execute")]
    public async Task<InlineActionResultDto?> ExecuteInlineActionAsync(
        string actionId, string selectedText, string? chapterGuid, string? sceneId,
        CancellationToken cancellationToken)
    {
        if (Host == null) return null;
        var request = new InlineActionRequest
        {
            SelectedText = selectedText ?? string.Empty,
            SceneId = sceneId ?? string.Empty,
            ChapterGuid = chapterGuid ?? string.Empty,
        };
        var result = await Host.ExecuteInlineActionAsync(actionId, request, cancellationToken);
        if (result == null) return null;
        return new InlineActionResultDto(
            result.Text,
            result.Disposition == InlineActionDisposition.InsertAfterSelection ? "insertAfter" : "replace",
            result.Error);
    }

    // ── Context menu (item 2) ───────────────────────────────────────────
    // The AI Assistant's "generate synopsis" uses Context="Scene"; the desktop
    // surfaced Scene/Chapter items in the project tree. On the new host we also
    // offer Scene/Editor items in the editor's context menu, operating on the
    // currently open scene.

    [JsonRpcMethod("extensions/contextMenuItems")]
    public ContextMenuInfoDto[] ContextMenuItems()
        => Host?.EnumerateContextMenuItemsWithIds()
               .Select(x => new ContextMenuInfoDto(x.Id, x.Item.Label, x.Item.Icon, x.Item.IconPath, x.Item.Context))
               .ToArray()
           ?? [];

    [JsonRpcMethod("extensions/contextMenuItem/execute")]
    public void ExecuteContextMenuItem(string id, string? chapterGuid, string? sceneId)
    {
        if (Host == null) return;
        Host.ExecuteContextMenuItem(id, BuildSceneContext(chapterGuid, sceneId));
    }

    private object? BuildSceneContext(string? chapterGuid, string? sceneId)
    {
        if (Host == null || string.IsNullOrEmpty(chapterGuid) || string.IsNullOrEmpty(sceneId))
            return null;
        var project = (IExtensionProjectService)Host.Host;
        return project.GetScenesForChapter(chapterGuid).FirstOrDefault(s => s.Id == sceneId);
    }

    // ── Hotkeys (item 4) ────────────────────────────────────────────────

    [JsonRpcMethod("extensions/hotkeys")]
    public ExtensionHotkeyInfoDto[] Hotkeys()
        => Extensions.HotkeyRegistry.All
            .Select(h => new ExtensionHotkeyInfoDto(
                h.ActionId, h.EffectiveDisplayName, h.EffectiveCategory, h.DefaultGesture))
            .ToArray();

    [JsonRpcMethod("extensions/hotkey/execute")]
    public bool ExecuteHotkey(string actionId)
    {
        var descriptor = Extensions.HotkeyRegistry.Find(actionId);
        if (descriptor == null) return false;
        if (descriptor.CanExecute != null && !descriptor.CanExecute()) return false;
        descriptor.OnExecute?.Invoke();
        return true;
    }

    // ── Themes (item 5) ─────────────────────────────────────────────────

    [JsonRpcMethod("extensions/themes")]
    public ExtensionThemeInfoDto[] Themes()
        => Host?.EnumerateThemes()
               .Select(x => new ExtensionThemeInfoDto(x.ExtensionId, x.Theme.Name, x.Theme.AccentColor))
               .ToArray()
           ?? [];

    // ── Status bar (item 6) ─────────────────────────────────────────────

    [JsonRpcMethod("extensions/statusBarItems")]
    public StatusBarInfoDto[] StatusBarItems()
        => Host?.EnumerateStatusBarItemsWithIds()
               .Select(x => new StatusBarInfoDto(
                   x.Id,
                   x.Item.Alignment,
                   x.Item.Order,
                   Safe(x.Item.GetText),
                   x.Item.GetTooltip == null ? null : Safe(x.Item.GetTooltip),
                   x.Item.OnClick != null))
               .OrderBy(x => x.Order)
               .ToArray()
           ?? [];

    [JsonRpcMethod("extensions/statusBarItem/execute")]
    public void ExecuteStatusBarItem(string id) => Host?.ExecuteStatusBarItem(id);

    internal static string Safe(System.Func<string> f)
    {
        try { return f() ?? string.Empty; }
        catch { return string.Empty; }
    }

    // ── Declarative settings schema (item 8) ────────────────────────────

    [JsonRpcMethod("extensions/settingsSchema")]
    public SettingsSchemaDto[] SettingsSchemas()
        => Host?.EnumerateSettingsSchemas()
               .Select(x => ToDto(x.ExtensionId, x.ExtensionName, x.Contributor.GetSettingsSchema()))
               .ToArray()
           ?? [];

    [JsonRpcMethod("extensions/settingsSchema/save")]
    public async Task SaveSettingsSchemaAsync(string extensionId, Dictionary<string, string> values)
    {
        if (Host == null) return;
        await Host.ApplySettingsSchemaAsync(extensionId, values ?? new Dictionary<string, string>());
    }

    /// <summary>Runs a schema action button (e.g. "Refresh models") for an
    /// extension, passing the form's current values, and returns the refreshed
    /// schema — or null when the extension leaves the form unchanged.</summary>
    [JsonRpcMethod("extensions/settingsSchema/action")]
    public async Task<SettingsSchemaDto?> ExecuteSettingsSchemaActionAsync(
        string extensionId, string actionKey, Dictionary<string, string> values)
    {
        if (Host == null) return null;
        var name = Host.EnumerateSettingsSchemas()
            .FirstOrDefault(x => x.ExtensionId == extensionId).ExtensionName ?? extensionId;
        var schema = await Host.ExecuteSchemaActionAsync(
            extensionId, actionKey, values ?? new Dictionary<string, string>());
        return schema == null ? null : ToDto(extensionId, name, schema);
    }

    private static SettingsSchemaDto ToDto(string extId, string extName, Novalist.Sdk.Models.SettingsSchema schema)
        => new(
            extId, extName, schema.Title,
            schema.Fields.Select(f => new SettingsFieldDto(
                f.Key, f.Label, f.Type.ToString().ToLowerInvariant(), f.Value,
                f.Options?.ToArray(), f.Min, f.Max, f.Group, f.Help,
                f.VisibleWhenKey, f.VisibleWhenValues?.ToArray(), f.Suggestions?.ToArray())).ToArray());
}

public sealed record InlineActionInfoDto(string Id, string Label, string Group, string Icon, int Priority);
public sealed record InlineActionResultDto(string Text, string Disposition, string? Error);
public sealed record ContextMenuInfoDto(string Id, string Label, string Icon, string? IconPath, string Context);
public sealed record ExtensionHotkeyInfoDto(string ActionId, string DisplayName, string Category, string DefaultGesture);
public sealed record ExtensionThemeInfoDto(string ExtensionId, string Name, string? AccentColor);
public sealed record StatusBarInfoDto(string Id, string Alignment, int Order, string Text, string? Tooltip, bool HasCommand);
public sealed record SettingsSchemaDto(string ExtensionId, string ExtensionName, string Title, IReadOnlyList<SettingsFieldDto> Fields);
public sealed record SettingsFieldDto(
    string Key, string Label, string Type, string Value,
    IReadOnlyList<string>? Options, double? Min, double? Max, string? Group, string? Help,
    string? VisibleWhenKey, IReadOnlyList<string>? VisibleWhenValues, IReadOnlyList<string>? Suggestions);
