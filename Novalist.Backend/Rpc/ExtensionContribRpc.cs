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
               .Select(a => new InlineActionInfoDto(
                   a.Id, a.Label, a.Group, a.Icon, a.Priority,
                   a.AllowsEmptySelection, SlashKeyword(a)))
               .ToArray()
           ?? [];

    /// <summary>The word typed after the slash. Falls back to the part of the id
    /// after the last dot, so "ai.continue" is "/continue" without every
    /// contributor having to restate it.</summary>
    internal static string SlashKeyword(InlineActionDescriptor action)
    {
        if (!string.IsNullOrWhiteSpace(action.SlashKeyword)) return action.SlashKeyword.Trim();
        var dot = action.Id.LastIndexOf('.');
        return dot >= 0 && dot < action.Id.Length - 1 ? action.Id[(dot + 1)..] : action.Id;
    }

    [JsonRpcMethod("extensions/inlineAction/execute")]
    public async Task<InlineActionResultDto?> ExecuteInlineActionAsync(
        string actionId, string selectedText, string? chapterGuid, string? sceneId,
        CancellationToken cancellationToken,
        string? precedingText = null, string? directive = null)
    {
        if (Host == null) return null;
        var request = new InlineActionRequest
        {
            SelectedText = selectedText ?? string.Empty,
            SceneId = sceneId ?? string.Empty,
            ChapterGuid = chapterGuid ?? string.Empty,
            PrecedingText = precedingText ?? string.Empty,
            Directive = directive ?? string.Empty,
        };
        var result = await Host.ExecuteInlineActionAsync(actionId, request, cancellationToken);
        if (result == null) return null;
        return new InlineActionResultDto(result.Text, DispositionName(result.Disposition), result.Error);
    }

    internal static string DispositionName(InlineActionDisposition disposition) => disposition switch
    {
        InlineActionDisposition.InsertAfterSelection => "insertAfter",
        InlineActionDisposition.InsertAtCaret => "insertAtCaret",
        _ => "replace"
    };

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

    // ── Commands ────────────────────────────────────────────────────────

    /// <summary>
    /// Every command extensions have registered.
    ///
    /// The registry existed and filled up - the AI assistant alone puts eight
    /// commands in it - but nothing ever read it, so "Critique this scene" was
    /// a string in a dictionary no surface listed and no caller could run. An
    /// SDK method an extension can call and the writer can never reach is worse
    /// than one that does not exist, because the extension author believes they
    /// have shipped it.
    /// </summary>
    [JsonRpcMethod("extensions/commands")]
    public ExtensionCommandInfoDto[] Commands()
        => Host?.Host.GetCommands()
               .Select(c => new ExtensionCommandInfoDto(
                   c.Id, c.Title, c.Description, c.ArgumentsSchema, c.Mutates))
               .ToArray()
           ?? [];

    /// <summary>
    /// Runs one, by id. False for an id nothing has registered, which is what a
    /// stale palette entry or a script naming a command from an extension the
    /// writer has since removed looks like.
    /// </summary>
    [JsonRpcMethod("extensions/command/execute")]
    public async Task<bool> ExecuteCommandAsync(string commandId, string? argumentsJson = null)
    {
        if (Host == null) return false;
        return await Host.Host.InvokeCommandAsync(commandId, argumentsJson);
    }

    // ── Themes (item 5) ─────────────────────────────────────────────────

    [JsonRpcMethod("extensions/themes")]
    public ExtensionThemeInfoDto[] Themes()
        => Host?.EnumerateThemes().Select(BuildThemeDto).ToArray() ?? [];

    /// <summary>
    /// Flattens a contributed theme into the shape the renderer applies: a token
    /// map plus, when the theme points at one, the text of its stylesheet.
    /// AccentColor is folded in as the accent tokens so a theme that sets only an
    /// accent still works, and an explicit token wins over it.
    /// </summary>
    internal static ExtensionThemeInfoDto BuildThemeDto(
        (string ExtensionId, string FolderPath, ThemeOverride Theme) source)
    {
        var (extensionId, folderPath, theme) = source;
        var tokens = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(theme.AccentColor)
            && Appearance.UserAssetsService.IsTokenValue(theme.AccentColor))
        {
            tokens["--nl-accent"] = theme.AccentColor.Trim();
            tokens["--nl-accent-hover"] = theme.AccentColor.Trim();
        }
        foreach (var (key, value) in theme.Tokens ?? new Dictionary<string, string>())
        {
            if (!Appearance.UserAssetsService.IsTokenName(key)
                || !Appearance.UserAssetsService.IsTokenValue(value))
                continue;
            tokens[key] = value.Trim();
        }
        return new ExtensionThemeInfoDto(
            extensionId,
            theme.Name,
            Appearance.UserAssetsService.Slugify($"{extensionId}-{theme.Name}"),
            theme.AccentColor,
            tokens,
            ReadThemeStylesheet(folderPath, theme.ResourcePath));
    }

    /// <summary>
    /// Reads a theme's stylesheet from the extension folder. Null when the theme
    /// declares none, the path escapes the folder, or the file cannot be read —
    /// a missing stylesheet costs the theme its extra rules, not its place in
    /// the dropdown.
    /// </summary>
    internal static string? ReadThemeStylesheet(string folderPath, string? resourcePath)
    {
        if (string.IsNullOrWhiteSpace(resourcePath) || string.IsNullOrWhiteSpace(folderPath))
            return null;
        try
        {
            var root = Path.GetFullPath(folderPath);
            var full = Path.GetFullPath(Path.Combine(root, resourcePath));
            if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                return null;
            return File.Exists(full) ? File.ReadAllText(full) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Extensions.Log.Warn($"Could not read an extension theme stylesheet: {ex.GetType().Name}");
            return null;
        }
    }

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

/// <summary>An inline action as the editor sees it. <c>AllowsEmptySelection</c>
/// decides whether it is offered at a bare caret and listed in the slash menu;
/// <c>SlashKeyword</c> is what the writer types after the slash.</summary>
public sealed record InlineActionInfoDto(
    string Id, string Label, string Group, string Icon, int Priority,
    bool AllowsEmptySelection, string SlashKeyword);
public sealed record InlineActionResultDto(string Text, string Disposition, string? Error);
public sealed record ContextMenuInfoDto(string Id, string Label, string Icon, string? IconPath, string Context);
public sealed record ExtensionHotkeyInfoDto(string ActionId, string DisplayName, string Category, string DefaultGesture);

/// <summary>
/// A command an extension registered. <paramref name="ArgumentsSchema"/> is the
/// JSON Schema for its arguments, empty for a command that takes none - the
/// palette runs those directly and leaves the rest to a caller that can supply
/// them.
/// </summary>
public sealed record ExtensionCommandInfoDto(
    string Id, string Title, string Description, string ArgumentsSchema, bool Mutates);
public sealed record ExtensionThemeInfoDto(
    string ExtensionId,
    string Name,
    string Slug,
    string? AccentColor,
    IReadOnlyDictionary<string, string> Tokens,
    string? Css);
public sealed record StatusBarInfoDto(string Id, string Alignment, int Order, string Text, string? Tooltip, bool HasCommand);
public sealed record SettingsSchemaDto(string ExtensionId, string ExtensionName, string Title, IReadOnlyList<SettingsFieldDto> Fields);
public sealed record SettingsFieldDto(
    string Key, string Label, string Type, string Value,
    IReadOnlyList<string>? Options, double? Min, double? Max, string? Group, string? Help,
    string? VisibleWhenKey, IReadOnlyList<string>? VisibleWhenValues, IReadOnlyList<string>? Suggestions);
