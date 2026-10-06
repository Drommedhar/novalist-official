using System.Text.Json;
using System.Text.RegularExpressions;
using Markdig;
using Novalist.Core.Models;

namespace Novalist.Core.Services;

/// <summary>
/// Imports an Obsidian Novalist plugin project into the standalone format.
/// </summary>
public partial class PluginImportService
{
    private static readonly JsonSerializerOptions JsonWriteOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Progress callback: (stepDescription, currentItem, totalItems).
    /// </summary>
    public Action<string, int, int>? ProgressChanged;

    /// <summary>
    /// Log messages collected during import for debugging.
    /// </summary>
    public List<string> Log { get; } = [];

    /// <summary>
    /// Import a plugin vault folder into a new standalone project.
    /// </summary>
    /// <param name="vaultRoot">Path to the Obsidian vault root.</param>
    /// <param name="projectPath">Path within the vault where the Novalist project lives (relative to vault root, or vault root itself).</param>
    /// <param name="outputDirectory">Parent directory where the standalone project folder will be created.</param>
    /// <param name="projectName">Name for the new standalone project.</param>
    /// <param name="bookName">Name for the imported book.</param>
    public async Task<PluginImportResult> ImportAsync(
        string vaultRoot,
        string projectPath,
        string outputDirectory,
        string projectName,
        string bookName)
    {
        var sourceRoot = string.IsNullOrEmpty(projectPath) || projectPath == "."
            ? vaultRoot : Path.Combine(vaultRoot, projectPath);
        var settings = await LoadPluginSettingsAsync(vaultRoot);
        var folders = ResolveFolderNames(settings);
        var worldBibleSource = ResolveWorldBibleSource(settings, vaultRoot);

        ReportProgress("Parsing chapters...", 0, 0);
        var chapters = await ParseChapterFilesAsync(Path.Combine(sourceRoot, folders.Chapters));
        var entities = await ParseImportEntitiesAsync(sourceRoot, worldBibleSource, folders);
        var output = CreateImportOutput(outputDirectory, projectName, bookName, settings);
        var scenes = await WriteImportedChaptersAsync(chapters, output);

        CopyImportedImages(vaultRoot, sourceRoot, worldBibleSource, folders, entities, output);
        ApplyCharacterTemplateDateMode(entities.All.Characters, output.Book.CharacterTemplates);
        await WriteImportedEntitiesAsync(entities, output);
        await WriteImportedProjectAsync(output, scenes, settings);
        var result = CollectImportResult(output.ProjectDir, settings);

        ReportProgress("Import complete!", 1, 1);
        return result;
    }

    private void ReportProgress(string step, int current, int total)
    {
        ProgressChanged?.Invoke(step, current, total);
    }

    private void LogLine(string message)
    {
        Log.Add(message);
    }

    private async Task<ImportedEntities> ParseImportEntitiesAsync(string sourceRoot, string? worldBibleSource, FolderNames folderNames)
    {
        // ── 2. Parse entities ───────────────────────────────────────
        ReportProgress("Parsing characters...", 0, 0);
        var characters = await ParseEntityFilesAsync<CharacterData>(
            Path.Combine(sourceRoot, folderNames.Characters), "CharacterSheet", ParseCharacterSheet);

        ReportProgress("Parsing locations...", 0, 0);
        var locations = await ParseEntityFilesAsync<LocationData>(
            Path.Combine(sourceRoot, folderNames.Locations), "LocationSheet", ParseLocationSheet);

        ReportProgress("Parsing items...", 0, 0);
        var items = await ParseEntityFilesAsync<ItemData>(
            Path.Combine(sourceRoot, folderNames.Items), "ItemSheet", ParseItemSheet);

        ReportProgress("Parsing lore...", 0, 0);
        var lore = await ParseEntityFilesAsync<LoreData>(
            Path.Combine(sourceRoot, folderNames.Lore), "LoreSheet", ParseLoreSheet);

        // Parse world bible entities
        var wbCharacters = new List<CharacterData>();
        var wbLocations = new List<LocationData>();
        var wbItems = new List<ItemData>();
        var wbLore = new List<LoreData>();

        if (worldBibleSource != null)
        {
            ReportProgress("Parsing world bible...", 0, 0);
            wbCharacters = await ParseEntityFilesAsync<CharacterData>(
                Path.Combine(worldBibleSource, folderNames.Characters), "CharacterSheet", ParseCharacterSheet);
            wbLocations = await ParseEntityFilesAsync<LocationData>(
                Path.Combine(worldBibleSource, folderNames.Locations), "LocationSheet", ParseLocationSheet);
            wbItems = await ParseEntityFilesAsync<ItemData>(
                Path.Combine(worldBibleSource, folderNames.Items), "ItemSheet", ParseItemSheet);
            wbLore = await ParseEntityFilesAsync<LoreData>(
                Path.Combine(worldBibleSource, folderNames.Lore), "LoreSheet", ParseLoreSheet);

            foreach (var c in wbCharacters) c.IsWorldBible = true;
            foreach (var l in wbLocations) l.IsWorldBible = true;
            foreach (var i in wbItems) i.IsWorldBible = true;
            foreach (var l in wbLore) l.IsWorldBible = true;
        }

        // ── 3. Build entity name→ID lookup for wikilink resolution ──
        var allCharacters = characters.Concat(wbCharacters).ToList();
        var allLocations = locations.Concat(wbLocations).ToList();
        var allItems = items.Concat(wbItems).ToList();
        var allLore = lore.Concat(wbLore).ToList();

        var entityNameToId = BuildEntityNameLookup(allCharacters, allLocations, allItems, allLore);

        // Resolve wikilink references to entity IDs
        ResolveWikilinkReferences(allCharacters, allLocations, entityNameToId);

        return new ImportedEntities(new EntityBatch(characters, locations, items, lore),
            new EntityBatch(wbCharacters, wbLocations, wbItems, wbLore),
            new EntityBatch(allCharacters, allLocations, allItems, allLore));
    }

    private ImportOutput CreateImportOutput(string outputDirectory, string projectName, string bookName, PluginSettingsData? pluginSettings)
    {
        // ── 4. Build standalone project structure ───────────────────
        ReportProgress("Creating project structure...", 0, 0);

        var safeName = SanitizeFileName(projectName);
        var projectDir = Path.Combine(outputDirectory, safeName);
        Directory.CreateDirectory(projectDir);

        var bookId = $"book-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
        var bookFolderName = SanitizeFileName(bookName);

        var metadata = new ProjectMetadata
        {
            Id = $"project-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
            Name = projectName,
            CreatedAt = DateTime.UtcNow,
            ActiveBookId = bookId
        };

        var book = new BookData
        {
            Id = bookId,
            Name = bookName,
            FolderName = bookFolderName,
            CreatedAt = DateTime.UtcNow
        };

        // Import templates from plugin settings
        ImportTemplates(pluginSettings, book);

        metadata.Books.Add(book);

        // Create directories
        var novalistDir = Path.Combine(projectDir, ".novalist");
        Directory.CreateDirectory(novalistDir);

        var bookRoot = Path.Combine(projectDir, bookFolderName);
        Directory.CreateDirectory(bookRoot);
        Directory.CreateDirectory(Path.Combine(bookRoot, ".book"));
        Directory.CreateDirectory(Path.Combine(bookRoot, book.ChapterFolder));
        Directory.CreateDirectory(Path.Combine(bookRoot, book.CharacterFolder));
        Directory.CreateDirectory(Path.Combine(bookRoot, book.LocationFolder));
        Directory.CreateDirectory(Path.Combine(bookRoot, book.ItemFolder));
        Directory.CreateDirectory(Path.Combine(bookRoot, book.LoreFolder));
        Directory.CreateDirectory(Path.Combine(bookRoot, book.ImageFolder));
        Directory.CreateDirectory(Path.Combine(bookRoot, book.SnapshotFolder));

        // World Bible folders
        var wbRoot = Path.Combine(projectDir, metadata.WorldBibleFolder);
        Directory.CreateDirectory(wbRoot);
        Directory.CreateDirectory(Path.Combine(wbRoot, metadata.CharacterFolder));
        Directory.CreateDirectory(Path.Combine(wbRoot, metadata.LocationFolder));
        Directory.CreateDirectory(Path.Combine(wbRoot, metadata.ItemFolder));
        Directory.CreateDirectory(Path.Combine(wbRoot, metadata.LoreFolder));
        Directory.CreateDirectory(Path.Combine(wbRoot, metadata.ImageFolder));

        return new ImportOutput(projectDir, novalistDir, bookRoot, wbRoot, metadata, book);
    }

    private async Task<ScenesManifest> WriteImportedChaptersAsync(List<ParsedChapter> parsedChapters, ImportOutput output)
    {
        // ── 5. Write chapters, scenes ───────────────────────────────
        ReportProgress("Writing chapters and scenes...", 0, parsedChapters.Count);
        var scenesManifest = new ScenesManifest();
        var chapterOrder = 1;

        foreach (var pc in parsedChapters.OrderBy(c => c.Order))
        {
            var chapter = new ChapterData
            {
                Guid = pc.Guid ?? Guid.NewGuid().ToString(),
                Title = pc.Title,
                Order = chapterOrder,
                Status = MapChapterStatus(pc.Status),
                Act = pc.Act ?? string.Empty,
                Date = pc.Date ?? string.Empty,
                FolderName = $"{chapterOrder:D2} - {SanitizeFileName(pc.Title)}"
            };

            output.Book.Chapters.Add(chapter);

            var chapterDir = Path.Combine(output.BookRoot, output.Book.ChapterFolder, chapter.FolderName);
            Directory.CreateDirectory(chapterDir);

            var scenes = new List<SceneData>();
            var sceneOrder = 1;

            foreach (var ps in pc.Scenes)
            {
                var sceneFileName = $"scene-{sceneOrder:D2}.novalist";
                var scene = new SceneData
                {
                    Title = ps.Title,
                    Order = sceneOrder,
                    FileName = sceneFileName,
                    ChapterGuid = chapter.Guid,
                    WordCount = CountWords(ps.Content)
                };

                scenes.Add(scene);
                await File.WriteAllTextAsync(Path.Combine(chapterDir, sceneFileName),
                    ConvertMarkdownToHtml(ps.Content));
                sceneOrder++;
            }

            scenesManifest.Chapters[chapter.Guid] = scenes;
            ReportProgress("Writing chapters and scenes...", chapterOrder, parsedChapters.Count);
            chapterOrder++;
        }

        return scenesManifest;
    }

    private void CopyImportedImages(string vaultRoot, string sourceRoot, string? worldBibleSource, FolderNames folderNames, ImportedEntities entities, ImportOutput output)
    {
        // ── 6. Copy images ──────────────────────────────────────────
        ReportProgress("Copying images...", 0, 0);
        var sourceImageDir = Path.Combine(sourceRoot, folderNames.Images);
        var destBookImageDir = Path.Combine(output.BookRoot, output.Book.ImageFolder);
        LogLine($"[Images] sourceRoot={sourceRoot}");
        LogLine($"[Images] sourceImageDir={sourceImageDir} exists={Directory.Exists(sourceImageDir)}");
        LogLine($"[Images] destBookImageDir={destBookImageDir}");
        LogLine($"[Images] folderNames.Images={folderNames.Images}, book.ImageFolder={output.Book.ImageFolder}");
        if (Directory.Exists(sourceImageDir))
        {
            CopyImageFiles(sourceImageDir, destBookImageDir);
            // Log copied files
            foreach (var f in Directory.GetFiles(destBookImageDir, "*", SearchOption.AllDirectories))
                LogLine($"[Images] Copied: {Path.GetRelativePath(output.BookRoot, f)}");
        }

        string? destWbImageDir = null;
        if (worldBibleSource != null)
        {
            var wbImageDir = Path.Combine(worldBibleSource, folderNames.Images);
            destWbImageDir = Path.Combine(output.WorldBibleRoot, output.Metadata.ImageFolder);
            if (Directory.Exists(wbImageDir))
            {
                CopyImageFiles(wbImageDir, destWbImageDir);
            }
        }

        // ── 6b. Remap entity image paths and copy individually referenced images ──
        var sourceRootRelative = Path.GetRelativePath(vaultRoot, sourceRoot)
            .Replace('\\', '/').TrimEnd('/');
        LogLine($"[Images] vaultRoot={vaultRoot}");
        LogLine($"[Images] sourceRootRelative={sourceRootRelative}");

        RemapAndCopyEntityImages(entities.All.Characters.SelectMany(c => c.Images), vaultRoot, sourceRootRelative,
            folderNames.Images, output.Book.ImageFolder, destBookImageDir);
        RemapAndCopyEntityImages(entities.All.Locations.SelectMany(l => l.Images), vaultRoot, sourceRootRelative,
            folderNames.Images, output.Book.ImageFolder, destBookImageDir);
        RemapAndCopyEntityImages(entities.All.Items.SelectMany(i => i.Images), vaultRoot, sourceRootRelative,
            folderNames.Images, output.Book.ImageFolder, destBookImageDir);
        RemapAndCopyEntityImages(entities.All.Lore.SelectMany(l => l.Images), vaultRoot, sourceRootRelative,
            folderNames.Images, output.Book.ImageFolder, destBookImageDir);

    }

    private async Task WriteImportedProjectAsync(ImportOutput output, ScenesManifest scenesManifest, PluginSettingsData? pluginSettings)
    {
        // ── 8. Write project metadata ───────────────────────────────
        ReportProgress("Saving project...", 0, 0);
        var metadataJson = JsonSerializer.Serialize(output.Metadata, JsonWriteOptions);
        await File.WriteAllTextAsync(Path.Combine(output.NovalistDir, "project.json"), metadataJson);

        var scenesJson = JsonSerializer.Serialize(scenesManifest, JsonWriteOptions);
        await File.WriteAllTextAsync(Path.Combine(output.BookRoot, ".book", "scenes.json"), scenesJson);

        // Write project settings
        var projectSettings = new ProjectSettings();
        if (pluginSettings != null)
        {
            var goals = pluginSettings.GetObjectOrDefault("wordCountGoals");
            if (goals != null)
            {
                var g = goals.Value;
                if (g.TryGetProperty("dailyGoal", out var dg) && dg.ValueKind == JsonValueKind.Number)
                    projectSettings.WordCountGoals.DailyGoal = dg.GetInt32();
                if (g.TryGetProperty("projectGoal", out var pg) && pg.ValueKind == JsonValueKind.Number)
                    projectSettings.WordCountGoals.ProjectGoal = pg.GetInt32();
            }
        }

        var settingsJson = JsonSerializer.Serialize(projectSettings, JsonWriteOptions);
        await File.WriteAllTextAsync(Path.Combine(output.NovalistDir, "settings.json"), settingsJson);

    }

    private static PluginImportResult CollectImportResult(string projectDir, PluginSettingsData? pluginSettings)
    {
        // ── 9. Collect app-level settings to merge ──────────────────
        var result = new PluginImportResult { ProjectPath = projectDir };

        if (pluginSettings != null)
        {
            var pairs = pluginSettings.GetObjectOrDefault("relationshipPairs");
            if (pairs != null)
            {
                foreach (var prop in pairs.Value.EnumerateObject())
                {
                    if (prop.Value.ValueKind == JsonValueKind.Array)
                    {
                        var values = new List<string>();
                        foreach (var item in prop.Value.EnumerateArray())
                        {
                            var s = item.GetString();
                            if (!string.IsNullOrEmpty(s))
                                values.Add(s);
                        }
                        if (values.Count > 0)
                            result.RelationshipPairs[prop.Name] = values;
                    }
                }
            }

            var pluginLang = pluginSettings.GetStringOrDefault("language", "");
            if (!string.IsNullOrEmpty(pluginLang))
                result.AutoReplacementLanguage = pluginLang;

            var pluginReplacements = pluginSettings.GetArrayOrDefault("autoReplacements");
            if (pluginReplacements != null)
            {
                try
                {
                    var json = pluginReplacements.Value.GetRawText();
                    result.AutoReplacements = JsonSerializer.Deserialize<List<AutoReplacementPair>>(json,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
                }
                catch { /* ignore parse errors */ }
            }
        }

        return result;
    }

    private sealed record EntityBatch(List<CharacterData> Characters, List<LocationData> Locations,
        List<ItemData> Items, List<LoreData> Lore)
    {
        public int Count => Characters.Count + Locations.Count + Items.Count + Lore.Count;
    }

    private sealed record ImportedEntities(EntityBatch Book, EntityBatch WorldBible, EntityBatch All);

    private sealed record ImportOutput(string ProjectDir, string NovalistDir, string BookRoot,
        string WorldBibleRoot, ProjectMetadata Metadata, BookData Book);

    private static string? ResolveWorldBibleSource(PluginSettingsData? settings, string vaultRoot)
    {
        var wbPath = settings?.GetStringOrDefault("worldBiblePath", "");
        if (string.IsNullOrEmpty(wbPath)) return null;
        var novalistRoot = settings?.GetStringOrDefault("novalistRoot", "");
        var root = string.IsNullOrEmpty(novalistRoot) ? vaultRoot : Path.Combine(vaultRoot, novalistRoot);
        var candidate = Path.Combine(root, wbPath);
        return Directory.Exists(candidate) ? candidate : null;
    }

    private async Task WriteImportedEntitiesAsync(ImportedEntities entities, ImportOutput output)
    {
        var total = entities.All.Count;
        ReportProgress("Writing entities...", 0, total);
        await WriteEntityBatchAsync(output.BookRoot, output.Book.CharacterFolder, entities.Book.Characters);
        await WriteEntityBatchAsync(output.WorldBibleRoot, output.Metadata.CharacterFolder, entities.WorldBible.Characters);
        await WriteEntityBatchAsync(output.BookRoot, output.Book.LocationFolder, entities.Book.Locations);
        await WriteEntityBatchAsync(output.WorldBibleRoot, output.Metadata.LocationFolder, entities.WorldBible.Locations);
        await WriteEntityBatchAsync(output.BookRoot, output.Book.ItemFolder, entities.Book.Items);
        await WriteEntityBatchAsync(output.WorldBibleRoot, output.Metadata.ItemFolder, entities.WorldBible.Items);
        await WriteEntityBatchAsync(output.BookRoot, output.Book.LoreFolder, entities.Book.Lore);
        await WriteEntityBatchAsync(output.WorldBibleRoot, output.Metadata.LoreFolder, entities.WorldBible.Lore);
        ReportProgress("Writing entities...", total, total);
    }

    private static async Task WriteEntityBatchAsync<T>(string root, string folder, IEnumerable<T> entities)
        where T : IEntityData
    {
        foreach (var entity in entities)
            await WriteEntityJsonAsync(Path.Combine(root, folder, $"{entity.Id}.json"), entity);
    }
}
