using System.Collections.Generic;
using System.Threading.Tasks;
using Novalist.Sdk.Models;

namespace Novalist.Sdk.Hooks;

/// <summary>
/// Contributes a declarative settings schema that a host can render as a form
/// without executing any extension UI code. This is the portable counterpart to
/// <see cref="ISettingsContributor"/> (whose <see cref="SettingsPage.CreateView"/>
/// returns an Avalonia control the Electron host cannot render). Extensions that
/// need their advanced configuration reachable on every host should implement
/// this interface.
/// </summary>
public interface ISettingsSchemaContributor
{
    /// <summary>
    /// Returns the current schema (fields plus their present values). Called
    /// whenever the host opens the extension's settings, so the returned
    /// <see cref="SettingsField.Value"/>s should reflect live state.
    /// </summary>
    SettingsSchema GetSettingsSchema();

    /// <summary>
    /// Applies edited values back to the extension and persists them. Keys match
    /// <see cref="SettingsField.Key"/>; only changed fields may be present, so
    /// implementations should treat a missing key as "unchanged".
    /// </summary>
    Task ApplySettingsAsync(IReadOnlyDictionary<string, string> values);

    /// <summary>
    /// Invoked when the user activates a <see cref="SettingsFieldType.Action"/>
    /// field. <paramref name="actionKey"/> is that field's
    /// <see cref="SettingsField.Key"/>; <paramref name="values"/> holds the form's
    /// current (possibly unsaved) values so the action can act on live input
    /// (e.g. fetch models from the base URL the user just typed). Return a
    /// refreshed <see cref="SettingsSchema"/> to replace the rendered form (for
    /// example with a field's <see cref="SettingsField.Suggestions"/> populated),
    /// or <c>null</c> to leave the form unchanged. Defaults to a no-op so existing
    /// implementations need no changes.
    /// </summary>
    Task<SettingsSchema?> ExecuteSchemaActionAsync(
        string actionKey, IReadOnlyDictionary<string, string> values)
        => Task.FromResult<SettingsSchema?>(null);
}
