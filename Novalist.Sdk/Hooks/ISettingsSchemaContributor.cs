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
}
