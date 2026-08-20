using System.Text.Json;
using Novalist.Core.Models;

namespace Novalist.Core.Services;

public class SettingsService : ISettingsService
{
    private readonly string _settingsPath;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// One save at a time.
    ///
    /// Two writes that overlap do not merge, they collide: Windows refuses the
    /// second with "the file is being used by another process", and the change
    /// it was carrying is lost with no sign to the writer that anything went
    /// wrong. Settings are written per edit, and two edits in quick succession
    /// - tabbing between two fields of the same form - is ordinary use, not an
    /// edge case.
    /// </summary>
    private readonly SemaphoreSlim _saveLock = new(1, 1);

    public AppSettings Settings { get; private set; } = new();

    private SettingsOverrides? _activeOverrides;
    public IEffectiveSettings Effective { get; }

    public void SetActiveOverrides(SettingsOverrides? overrides) => _activeOverrides = overrides;

    /// <param name="settingsDirectory">
    /// Directory the settings.json lives in. Defaults to
    /// <c>%APPDATA%/Novalist</c>; tests pass a temp directory.
    /// </param>
    public SettingsService(string? settingsDirectory = null)
    {
        var novalistDir = settingsDirectory
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Novalist");
        Directory.CreateDirectory(novalistDir);
        _settingsPath = Path.Combine(novalistDir, "settings.json");

        Effective = new EffectiveSettings(() => Settings, () => _activeOverrides);
    }

    public async Task LoadAsync()
    {
        if (File.Exists(_settingsPath))
        {
            var json = await File.ReadAllTextAsync(_settingsPath);
            Settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        Settings.EnsureDefaults();
    }

    public async Task SaveAsync()
    {
        await _saveLock.WaitAsync();
        try
        {
            // Serialized inside the lock as well as written: the object is
            // being edited by whoever asked for the save, and reading it
            // outside would let one save capture half of the next one's change.
            var json = JsonSerializer.Serialize(Settings, JsonOptions);
            await File.WriteAllTextAsync(_settingsPath, json);
        }
        finally
        {
            _saveLock.Release();
        }
    }

    /// <summary>
    /// Reduces a project folder to one spelling, so the same project cannot sit
    /// in the recents list twice. Windows hands us "d:/git/x", "D:\git\x" and
    /// "D:\git\x\" for the same folder, and a stray separator in front of the
    /// drive letter has turned up as well.
    /// </summary>
    internal static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;

        // On Linux the alt separator is the separator, so this is a no-op there
        // and a backslash stays a legal filename character.
        var text = path.Trim().Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);

        // "\D:\git\x" - a separator before a drive letter is never meaningful.
        if (text.Length > 2 && text[0] == Path.DirectorySeparatorChar && char.IsLetter(text[1]) && text[2] == ':')
            text = text[1..];

        try
        {
            text = Path.GetFullPath(text);
        }
        catch (ArgumentException)
        {
            // Not a path we can resolve. Compare what we were given rather than
            // drop the entry.
        }

        text = text.TrimEnd(Path.DirectorySeparatorChar);
        return OperatingSystem.IsWindows() ? text.ToLowerInvariant() : text;
    }

    public void AddRecentProject(string name, string path, string coverImagePath = "")
    {
        var key = NormalizePath(path);
        Settings.RecentProjects.RemoveAll(r => NormalizePath(r.Path) == key);
        Settings.RecentProjects.Insert(0, new RecentProject
        {
            Name = name,
            Path = path,
            LastOpened = DateTime.UtcNow,
            CoverImagePath = coverImagePath
        });

        // Keep only the 10 most recent
        if (Settings.RecentProjects.Count > 10)
            Settings.RecentProjects.RemoveRange(10, Settings.RecentProjects.Count - 10);
    }

    public void RemoveRecentProject(string path)
    {
        var key = NormalizePath(path);
        Settings.RecentProjects.RemoveAll(r => NormalizePath(r.Path) == key);
    }

    /// <summary>
    /// Follow a recent project to where it lives now, keeping its place in the
    /// list and its cover.
    ///
    /// The same project, not a new one: re-adding it would stamp it as
    /// just-opened and push it to the top of a list the writer has not touched.
    /// The cover is stored as an absolute path inside the project, so it moves
    /// with it - and only the part that named the old folder is rewritten, so a
    /// cover the writer pointed somewhere else entirely is left alone.
    ///
    /// A row that already names the destination is the same project reached by
    /// its new address; the one being moved carries the history, so the other
    /// goes.
    /// </summary>
    public void RelocateRecentProject(string oldPath, string newPath)
    {
        if (string.IsNullOrWhiteSpace(oldPath) || string.IsNullOrWhiteSpace(newPath)) return;
        var key = NormalizePath(oldPath);
        var entry = Settings.RecentProjects.FirstOrDefault(r => NormalizePath(r.Path) == key);
        if (entry == null) return;

        var newKey = NormalizePath(newPath);
        Settings.RecentProjects.RemoveAll(r => !ReferenceEquals(r, entry) && NormalizePath(r.Path) == newKey);

        entry.CoverImagePath = Rebase(entry.CoverImagePath, oldPath, newPath);
        entry.Path = newPath;
    }

    /// <summary>A path under <paramref name="oldRoot"/>, re-expressed under
    /// <paramref name="newRoot"/>. Anything else is returned untouched.</summary>
    private static string Rebase(string path, string oldRoot, string newRoot)
    {
        if (string.IsNullOrEmpty(path)) return path;
        var prefix = oldRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(prefix, StringComparison.Ordinal)) return path;
        return newRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar + path[prefix.Length..];
    }
}
