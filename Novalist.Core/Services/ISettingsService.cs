using Novalist.Core.Models;

namespace Novalist.Core.Services;

public interface ISettingsService
{
    AppSettings Settings { get; }

    /// <summary>
    /// Resolved read-only view: per-project override when set, else global.
    /// Read overridable settings (language, book/editor formatting) through
    /// this; keep machine state on <see cref="Settings"/>.
    /// </summary>
    IEffectiveSettings Effective { get; }

    /// <summary>
    /// Sets the active project's overrides used by <see cref="Effective"/>.
    /// Pass null when no project is open (revert to pure global).
    /// </summary>
    void SetActiveOverrides(SettingsOverrides? overrides);

    Task LoadAsync();
    Task SaveAsync();
    void AddRecentProject(string name, string path, string coverImagePath = "");
    void RemoveRecentProject(string path);
}
