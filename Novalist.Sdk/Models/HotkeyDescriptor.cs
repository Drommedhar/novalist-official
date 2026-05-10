namespace Novalist.Sdk.Models;

/// <summary>
/// Describes a keyboard shortcut action that can be registered with the host
/// hotkey system. Both built-in and extension-contributed shortcuts use this model.
/// </summary>
public sealed class HotkeyDescriptor
{
    /// <summary>
    /// Unique action identifier. Convention:
    /// built-in = "app.{category}.{action}" (e.g. "app.nav.dashboard"),
    /// extension = "ext.{extensionId}.{action}" (e.g. "ext.com.novalist.writingtoolkit.wordfreq").
    /// </summary>
    public string ActionId { get; init; } = string.Empty;

    /// <summary>Human-readable display name (localized by the caller). When
    /// <see cref="DisplayNameProvider"/> is set it takes precedence — use the
    /// provider when the name should re-translate on language change.</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>Late-bound display name provider. Callers that want their
    /// command label to refresh on language switch should set this with a
    /// lambda that calls into the host's localization service.</summary>
    public Func<string>? DisplayNameProvider { get; init; }

    /// <summary>Resolved display name, preferring the provider when present.</summary>
    public string EffectiveDisplayName
        => DisplayNameProvider?.Invoke() ?? DisplayName;

    /// <summary>Grouping label for the settings UI (e.g. "Navigation", "Editor", or extension display name).</summary>
    public string Category { get; init; } = string.Empty;

    /// <summary>Late-bound category provider. Same semantics as
    /// <see cref="DisplayNameProvider"/>.</summary>
    public Func<string>? CategoryProvider { get; init; }

    public string EffectiveCategory => CategoryProvider?.Invoke() ?? Category;

    /// <summary>
    /// Default key gesture as a string (e.g. "Ctrl+Shift+N").
    /// Uses Avalonia KeyGesture string format.
    /// </summary>
    public string DefaultGesture { get; init; } = string.Empty;

    /// <summary>Callback invoked when the hotkey is triggered.</summary>
    public Action? OnExecute { get; set; }

    /// <summary>
    /// Optional guard. When set, the hotkey only fires if this returns true.
    /// Enables context-aware dispatch (e.g. formatting keys only when editor is focused).
    /// </summary>
    public Func<bool>? CanExecute { get; set; }
}
