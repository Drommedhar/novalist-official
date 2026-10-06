using System.Text.Json;
using System.Text.RegularExpressions;
using Markdig;
using Novalist.Core.Models;

namespace Novalist.Core.Services;

public partial class PluginImportService
{
    /// <summary>
    /// Detects whether a folder looks like a Novalist plugin project.
    /// Returns the list of project sub-paths found (from data.json or folder detection).
    /// </summary>
    public static async Task<PluginDetectionResult> DetectPluginProjectAsync(string vaultRoot)
    {
        var result = new PluginDetectionResult { VaultRoot = vaultRoot };

        // Try to read plugin settings
        var dataJsonPath = Path.Combine(vaultRoot, ".obsidian", "plugins", "novalist", "data.json");
        if (File.Exists(dataJsonPath))
        {
            var json = await File.ReadAllTextAsync(dataJsonPath);
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("projects", out var projects) && projects.ValueKind == JsonValueKind.Array)
            {
                foreach (var proj in projects.EnumerateArray())
                {
                    var name = proj.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                    var path = proj.TryGetProperty("path", out var p) ? p.GetString() ?? "" : "";
                    result.Projects.Add(new PluginProjectInfo { Name = name, Path = path });
                }
            }

            result.HasPluginData = true;
        }

        // If no projects found from data.json, try to detect by folder structure
        if (result.Projects.Count == 0)
        {
            // Check if root itself has Chapters folder
            if (Directory.Exists(Path.Combine(vaultRoot, "Chapters")))
            {
                result.Projects.Add(new PluginProjectInfo
                {
                    Name = Path.GetFileName(vaultRoot),
                    Path = ""
                });
            }
            else
            {
                // Check immediate subdirectories
                foreach (var dir in Directory.GetDirectories(vaultRoot))
                {
                    var dirName = Path.GetFileName(dir);
                    if (dirName.StartsWith('.')) continue;
                    if (Directory.Exists(Path.Combine(dir, "Chapters")))
                    {
                        result.Projects.Add(new PluginProjectInfo
                        {
                            Name = dirName,
                            Path = dirName
                        });
                    }
                }
            }
        }

        return result;
    }

    // ── Template Import ─────────────────────────────────────────────

    private static void ImportTemplates(PluginSettingsData? settings, BookData book)
    {
        if (settings == null) return;

        book.CharacterTemplates = ParseTemplateArray<CharacterTemplate>(settings, "characterTemplates");
        book.LocationTemplates = ParseTemplateArray<LocationTemplate>(settings, "locationTemplates");
        book.ItemTemplates = ParseTemplateArray<ItemTemplate>(settings, "itemTemplates");
        book.LoreTemplates = ParseTemplateArray<LoreTemplate>(settings, "loreTemplates");

        book.ActiveCharacterTemplateId = settings.GetStringOrDefault("activeCharacterTemplateId", "");
        book.ActiveLocationTemplateId = settings.GetStringOrDefault("activeLocationTemplateId", "");
        book.ActiveItemTemplateId = settings.GetStringOrDefault("activeItemTemplateId", "");
        book.ActiveLoreTemplateId = settings.GetStringOrDefault("activeLoreTemplateId", "");
    }

    /// <summary>
    /// For characters whose template uses ageMode "date", the plugin stores the birth date
    /// in the Age field. Move it to BirthDate and set AgeMode/AgeIntervalUnit accordingly.
    /// </summary>
    private static void ApplyCharacterTemplateDateMode(List<CharacterData> characters, List<CharacterTemplate> templates)
    {
        if (templates.Count == 0) return;

        var lookup = templates.ToDictionary(t => t.Id, StringComparer.Ordinal);

        foreach (var c in characters)
        {
            if (string.IsNullOrEmpty(c.TemplateId) || !lookup.TryGetValue(c.TemplateId, out var template))
                continue;

            if (!string.Equals(template.AgeMode, "date", StringComparison.OrdinalIgnoreCase))
                continue;

            // Plugin stores the birth date (YYYY-MM-DD) in the Age field when ageMode is "date"
            if (!string.IsNullOrWhiteSpace(c.Age))
            {
                c.BirthDate = c.Age;
                c.Age = string.Empty;
            }

            c.AgeMode = "date";
            c.AgeIntervalUnit = template.AgeIntervalUnit ?? IntervalUnit.Years;
        }
    }

    private static List<T> ParseTemplateArray<T>(PluginSettingsData settings, string key)
    {
        var element = settings.GetArrayOrDefault(key);
        if (element == null) return [];

        try
        {
            var json = element.Value.GetRawText();
            return JsonSerializer.Deserialize<List<T>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? [];
        }
        catch
        {
            return [];
        }
    }

    // ── Utilities ───────────────────────────────────────────────────

    internal static string StripWikilink(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        // Remove ![[...]] embeds and [[...]] wikilinks
        var stripped = value.Trim();
        if (stripped.StartsWith("![[") && stripped.EndsWith("]]"))
            stripped = stripped[3..^2];
        else if (stripped.StartsWith("[[") && stripped.EndsWith("]]"))
            stripped = stripped[2..^2];
        // Handle alias: path|alias
        var pipeIdx = stripped.IndexOf('|');
        if (pipeIdx > 0)
            stripped = stripped[..pipeIdx];
        return stripped.Trim();
    }

    private static async Task<PluginSettingsData?> LoadPluginSettingsAsync(string vaultRoot)
    {
        var dataJsonPath = Path.Combine(vaultRoot, ".obsidian", "plugins", "novalist", "data.json");
        if (!File.Exists(dataJsonPath)) return null;

        var json = await File.ReadAllTextAsync(dataJsonPath);
        var doc = JsonDocument.Parse(json);
        return new PluginSettingsData(doc.RootElement);
    }

    private static FolderNames ResolveFolderNames(PluginSettingsData? settings)
    {
        return new FolderNames
        {
            Chapters = settings?.GetStringOrDefault("chapterFolder", "Chapters") ?? "Chapters",
            Characters = settings?.GetStringOrDefault("characterFolder", "Characters") ?? "Characters",
            Locations = settings?.GetStringOrDefault("locationFolder", "Locations") ?? "Locations",
            Items = settings?.GetStringOrDefault("itemFolder", "Items") ?? "Items",
            Lore = settings?.GetStringOrDefault("loreFolder", "Lore") ?? "Lore",
            Images = settings?.GetStringOrDefault("imageFolder", "Images") ?? "Images"
        };
    }

    private sealed class FolderNames
    {
        public string Chapters { get; set; } = "Chapters";
        public string Characters { get; set; } = "Characters";
        public string Locations { get; set; } = "Locations";
        public string Items { get; set; } = "Items";
        public string Lore { get; set; } = "Lore";
        public string Images { get; set; } = "Images";
    }
}
