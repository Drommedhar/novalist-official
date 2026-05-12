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

    public AppSettings Settings { get; private set; } = new();

    public SettingsService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var novalistDir = Path.Combine(appData, "Novalist");
        Directory.CreateDirectory(novalistDir);
        _settingsPath = Path.Combine(novalistDir, "settings.json");
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
        var json = JsonSerializer.Serialize(Settings, JsonOptions);
        await File.WriteAllTextAsync(_settingsPath, json);
    }

    public void AddRecentProject(string name, string path, string coverImagePath = "")
    {
        Settings.RecentProjects.RemoveAll(r => r.Path == path);
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
        Settings.RecentProjects.RemoveAll(r => string.Equals(r.Path, path, StringComparison.OrdinalIgnoreCase));
    }
}
