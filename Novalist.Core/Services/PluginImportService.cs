using System.Text.Json;
using System.Text.RegularExpressions;
using Markdig;
using Novalist.Core.Models;

namespace Novalist.Core.Services;

/// <summary>
/// Imports an Obsidian Novalist plugin project into the standalone format.
/// </summary>
public class PluginImportService
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
            ? vaultRoot
            : Path.Combine(vaultRoot, projectPath);

        // Load plugin settings from data.json if available
        var pluginSettings = await LoadPluginSettingsAsync(vaultRoot);
        var folderNames = ResolveFolderNames(pluginSettings);

        // Resolve world bible path
        string? worldBibleSource = null;
        var wbPath = pluginSettings?.GetStringOrDefault("worldBiblePath", "");
        if (!string.IsNullOrEmpty(wbPath))
        {
            var candidateWb = Path.Combine(
                string.IsNullOrEmpty(pluginSettings?.GetStringOrDefault("novalistRoot", ""))
                    ? vaultRoot
                    : Path.Combine(vaultRoot, pluginSettings!.GetStringOrDefault("novalistRoot", "")),
                wbPath);
            if (Directory.Exists(candidateWb))
                worldBibleSource = candidateWb;
        }

        // ── 1. Parse chapters and scenes ────────────────────────────
        ReportProgress("Parsing chapters...", 0, 0);
        var chaptersDir = Path.Combine(sourceRoot, folderNames.Chapters);
        var parsedChapters = await ParseChapterFilesAsync(chaptersDir);

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

            book.Chapters.Add(chapter);

            var chapterDir = Path.Combine(bookRoot, book.ChapterFolder, chapter.FolderName);
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

        // ── 6. Copy images ──────────────────────────────────────────
        ReportProgress("Copying images...", 0, 0);
        var sourceImageDir = Path.Combine(sourceRoot, folderNames.Images);
        var destBookImageDir = Path.Combine(bookRoot, book.ImageFolder);
        LogLine($"[Images] sourceRoot={sourceRoot}");
        LogLine($"[Images] sourceImageDir={sourceImageDir} exists={Directory.Exists(sourceImageDir)}");
        LogLine($"[Images] destBookImageDir={destBookImageDir}");
        LogLine($"[Images] folderNames.Images={folderNames.Images}, book.ImageFolder={book.ImageFolder}");
        if (Directory.Exists(sourceImageDir))
        {
            CopyImageFiles(sourceImageDir, destBookImageDir);
            // Log copied files
            foreach (var f in Directory.GetFiles(destBookImageDir, "*", SearchOption.AllDirectories))
                LogLine($"[Images] Copied: {Path.GetRelativePath(bookRoot, f)}");
        }

        string? destWbImageDir = null;
        if (worldBibleSource != null)
        {
            var wbImageDir = Path.Combine(worldBibleSource, folderNames.Images);
            destWbImageDir = Path.Combine(wbRoot, metadata.ImageFolder);
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

        RemapAndCopyEntityImages(allCharacters.SelectMany(c => c.Images), vaultRoot, sourceRootRelative,
            folderNames.Images, book.ImageFolder, destBookImageDir);
        RemapAndCopyEntityImages(allLocations.SelectMany(l => l.Images), vaultRoot, sourceRootRelative,
            folderNames.Images, book.ImageFolder, destBookImageDir);
        RemapAndCopyEntityImages(allItems.SelectMany(i => i.Images), vaultRoot, sourceRootRelative,
            folderNames.Images, book.ImageFolder, destBookImageDir);
        RemapAndCopyEntityImages(allLore.SelectMany(l => l.Images), vaultRoot, sourceRootRelative,
            folderNames.Images, book.ImageFolder, destBookImageDir);

        // ── 7. Apply template-level settings to entities ────────────
        ApplyCharacterTemplateDateMode(allCharacters, book.CharacterTemplates);

        // ── 8. Write entities ───────────────────────────────────────
        ReportProgress("Writing entities...", 0, allCharacters.Count + allLocations.Count + allItems.Count + allLore.Count);
        var entityIndex = 0;
        var entityTotal = allCharacters.Count + allLocations.Count + allItems.Count + allLore.Count;

        foreach (var c in characters)
        {
            await WriteEntityJsonAsync(Path.Combine(bookRoot, book.CharacterFolder, $"{c.Id}.json"), c);
            entityIndex++;
        }

        foreach (var c in wbCharacters)
        {
            await WriteEntityJsonAsync(Path.Combine(wbRoot, metadata.CharacterFolder, $"{c.Id}.json"), c);
            entityIndex++;
        }

        foreach (var l in locations)
        {
            await WriteEntityJsonAsync(Path.Combine(bookRoot, book.LocationFolder, $"{l.Id}.json"), l);
            entityIndex++;
        }

        foreach (var l in wbLocations)
        {
            await WriteEntityJsonAsync(Path.Combine(wbRoot, metadata.LocationFolder, $"{l.Id}.json"), l);
            entityIndex++;
        }

        foreach (var i in items)
        {
            await WriteEntityJsonAsync(Path.Combine(bookRoot, book.ItemFolder, $"{i.Id}.json"), i);
            entityIndex++;
        }

        foreach (var i in wbItems)
        {
            await WriteEntityJsonAsync(Path.Combine(wbRoot, metadata.ItemFolder, $"{i.Id}.json"), i);
            entityIndex++;
        }

        foreach (var l in lore)
        {
            await WriteEntityJsonAsync(Path.Combine(bookRoot, book.LoreFolder, $"{l.Id}.json"), l);
            entityIndex++;
        }

        foreach (var l in wbLore)
        {
            await WriteEntityJsonAsync(Path.Combine(wbRoot, metadata.LoreFolder, $"{l.Id}.json"), l);
            entityIndex++;
        }

        ReportProgress("Writing entities...", entityTotal, entityTotal);

        // ── 8. Write project metadata ───────────────────────────────
        ReportProgress("Saving project...", 0, 0);
        var metadataJson = JsonSerializer.Serialize(metadata, JsonWriteOptions);
        await File.WriteAllTextAsync(Path.Combine(novalistDir, "project.json"), metadataJson);

        var scenesJson = JsonSerializer.Serialize(scenesManifest, JsonWriteOptions);
        await File.WriteAllTextAsync(Path.Combine(bookRoot, ".book", "scenes.json"), scenesJson);

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
        await File.WriteAllTextAsync(Path.Combine(novalistDir, "settings.json"), settingsJson);

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

        ReportProgress("Import complete!", 1, 1);
        return result;
    }

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

    // ── Chapter / Scene Parsing ─────────────────────────────────────

    private static async Task<List<ParsedChapter>> ParseChapterFilesAsync(string chaptersDir)
    {
        var result = new List<ParsedChapter>();
        if (!Directory.Exists(chaptersDir)) return result;

        var mdFiles = Directory.GetFiles(chaptersDir, "*.md");
        foreach (var file in mdFiles)
        {
            var content = await File.ReadAllTextAsync(file);
            var normalized = content.Replace("\r\n", "\n");
            var chapter = ParseChapterFile(normalized, Path.GetFileNameWithoutExtension(file));
            result.Add(chapter);
        }

        return result.OrderBy(c => c.Order).ToList();
    }

    private static ParsedChapter ParseChapterFile(string content, string fallbackTitle)
    {
        var chapter = new ParsedChapter { Title = fallbackTitle };

        // Extract frontmatter
        var fmMatch = Regex.Match(content, @"^---\n([\s\S]*?)\n---\n?", RegexOptions.Multiline);
        var bodyContent = content;

        if (fmMatch.Success)
        {
            var fm = fmMatch.Groups[1].Value;
            chapter.Guid = ParseFrontmatterField(fm, "guid");
            var orderStr = ParseFrontmatterField(fm, "order");
            if (int.TryParse(orderStr, out var order)) chapter.Order = order;
            chapter.Status = ParseFrontmatterField(fm, "status") ?? "outline";
            chapter.Act = ParseFrontmatterField(fm, "act");
            chapter.Date = ParseFrontmatterField(fm, "date");

            bodyContent = content[fmMatch.Length..];
        }

        // Extract title from H1
        var h1Match = Regex.Match(bodyContent, @"^#\s+(.+)$", RegexOptions.Multiline);
        if (h1Match.Success)
            chapter.Title = h1Match.Groups[1].Value.Trim();

        // Split scenes by H2 headings
        var h2Pattern = new Regex(@"^##\s+(.+)$", RegexOptions.Multiline);
        var h2Matches = h2Pattern.Matches(bodyContent);

        if (h2Matches.Count > 0)
        {
            for (int i = 0; i < h2Matches.Count; i++)
            {
                var sceneTitle = h2Matches[i].Groups[1].Value.Trim();
                var startIdx = h2Matches[i].Index + h2Matches[i].Length;
                var endIdx = i + 1 < h2Matches.Count ? h2Matches[i + 1].Index : bodyContent.Length;
                var sceneContent = bodyContent[startIdx..endIdx].Trim();

                chapter.Scenes.Add(new ParsedScene { Title = sceneTitle, Content = sceneContent });
            }
        }
        else
        {
            // No H2 headings: treat entire body as one scene
            var body = bodyContent;
            // Strip H1 heading if present
            if (h1Match.Success)
                body = bodyContent[(h1Match.Index + h1Match.Length)..].Trim();

            if (!string.IsNullOrWhiteSpace(body))
            {
                chapter.Scenes.Add(new ParsedScene
                {
                    Title = chapter.Title,
                    Content = body
                });
            }
        }

        return chapter;
    }

    private static string? ParseFrontmatterField(string frontmatter, string key)
    {
        var match = Regex.Match(frontmatter, $@"^{Regex.Escape(key)}:\s*(.*)$", RegexOptions.Multiline);
        if (!match.Success) return null;
        var value = match.Groups[1].Value.Trim();
        // Remove quotes if present
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
            value = value[1..^1];
        return string.IsNullOrEmpty(value) ? null : value;
    }

    // ── Entity Sheet Parsing ────────────────────────────────────────

    private static async Task<List<T>> ParseEntityFilesAsync<T>(
        string directory, string sheetHeading, Func<string, string, T> parser) where T : class
    {
        var result = new List<T>();
        if (!Directory.Exists(directory)) return result;

        var mdFiles = Directory.GetFiles(directory, "*.md");
        foreach (var file in mdFiles)
        {
            var content = await File.ReadAllTextAsync(file);
            var entity = parser(content, sheetHeading);
            if (entity != null)
                result.Add(entity);
        }

        return result;
    }

    private static string? GetSheetSection(string content, string heading)
    {
        var lines = content.Replace("\r\n", "\n").Split('\n');
        var startIdx = -1;
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].Trim() == $"## {heading}")
            {
                startIdx = i + 1;
                break;
            }
        }

        if (startIdx == -1) return null;

        var sectionLines = new List<string>();
        for (int i = startIdx; i < lines.Length; i++)
        {
            if (lines[i].TrimStart().StartsWith("## ")) break;
            sectionLines.Add(lines[i]);
        }

        return string.Join('\n', sectionLines);
    }

    private static string ParseSheetField(string content, string fieldName)
    {
        var match = Regex.Match(content, $@"^[ \t]*{Regex.Escape(fieldName)}:[ \t]*(.*?)$", RegexOptions.Multiline);
        return match.Success ? match.Groups[1].Value.Trim() : string.Empty;
    }

    private static string ParseMultiLineField(string content, string fieldName, string[] nextSections)
    {
        var startMarker = $"\n{fieldName}:\n";
        var idx = content.IndexOf(startMarker, StringComparison.Ordinal);
        if (idx == -1) return string.Empty;

        var startIdx = idx + startMarker.Length;
        var endIdx = content.Length;

        foreach (var next in nextSections)
        {
            var nextIdx = content.IndexOf($"\n{next}", startIdx, StringComparison.Ordinal);
            if (nextIdx != -1 && nextIdx < endIdx)
                endIdx = nextIdx;
        }

        return content[startIdx..endIdx].Trim();
    }

    private static List<(string key, string value)> ParseListSection(string content, string sectionName, string[] nextSections)
    {
        var result = new List<(string, string)>();
        var text = ParseMultiLineField(content, sectionName, nextSections);
        if (string.IsNullOrEmpty(text)) return result;

        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;
            var match = Regex.Match(trimmed, @"^[-*]\s*(.+?)\s*:\s*(.+)$");
            if (match.Success)
                result.Add((match.Groups[1].Value.Trim(), match.Groups[2].Value.Trim()));
        }

        return result;
    }

    private static List<EntitySection> ParseSections(string content, string[] nextSections)
    {
        var result = new List<EntitySection>();
        var text = ParseMultiLineField(content, "Sections", nextSections);
        if (string.IsNullOrEmpty(text)) return result;

        var blocks = Regex.Split(text, @"^\s*---\s*$", RegexOptions.Multiline);
        foreach (var block in blocks)
        {
            var trimmed = block.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;

            var lines = trimmed.Split('\n');
            var title = lines[0].Trim();
            var sectionContent = string.Join('\n', lines.Skip(1)).Trim();
            if (!string.IsNullOrEmpty(title))
                result.Add(new EntitySection { Title = title, Content = sectionContent });
        }

        return result;
    }

    private static List<EntityImage> ParseImages(string content, string[] nextSections)
    {
        var items = ParseListSection(content, "Images", nextSections);
        return items.Select(i => new EntityImage
        {
            Name = i.key,
            Path = StripWikilink(i.value)
        }).ToList();
    }

    private static Dictionary<string, string> ParseCustomProperties(string content, string[] nextSections)
    {
        var items = ParseListSection(content, "CustomProperties", nextSections);
        var dict = new Dictionary<string, string>();
        foreach (var (key, value) in items)
            dict[key] = value;
        return dict;
    }

    // ── Character Sheet ─────────────────────────────────────────────

    private static CharacterData ParseCharacterSheet(string content, string sheetHeading)
    {
        var normalized = content.Replace("\r\n", "\n");
        var data = new CharacterData();

        // Extract name from H1
        var titleMatch = Regex.Match(normalized, @"^#\s+(.+)$", RegexOptions.Multiline);
        if (titleMatch.Success)
        {
            var fullName = titleMatch.Groups[1].Value.Trim();
            var parts = fullName.Split(' ', 2);
            data.Name = parts[0];
            data.Surname = parts.Length > 1 ? parts[1] : string.Empty;
        }

        var sheet = GetSheetSection(normalized, sheetHeading);
        if (sheet == null) return data;

        data.Name = NonEmpty(ParseSheetField(sheet, "Name"), data.Name);
        data.Surname = NonEmpty(ParseSheetField(sheet, "Surname"), data.Surname);
        data.Gender = ParseSheetField(sheet, "Gender");
        data.Age = ParseSheetField(sheet, "Age");
        data.Role = ParseSheetField(sheet, "Role");
        data.Group = ParseSheetField(sheet, "Group");
        data.EyeColor = ParseSheetField(sheet, "EyeColor");
        data.HairColor = ParseSheetField(sheet, "HairColor");
        data.HairLength = ParseSheetField(sheet, "HairLength");
        data.Height = ParseSheetField(sheet, "Height");
        data.Build = ParseSheetField(sheet, "Build");
        data.SkinTone = ParseSheetField(sheet, "SkinTone");
        data.DistinguishingFeatures = ParseSheetField(sheet, "DistinguishingFeatures");
        data.TemplateId = NullIfEmpty(ParseSheetField(sheet, "TemplateId"));

        var charNextSections = new[] { "Images:", "CustomProperties:", "Sections:", "ChapterOverrides:" };

        // Relationships
        var rels = ParseListSection(sheet, "Relationships", charNextSections);
        data.Relationships = rels.Select(r => new EntityRelationship
        {
            Role = r.key,
            Target = StripWikilink(r.value)
        }).ToList();

        // Images
        data.Images = ParseImages(sheet, new[] { "CustomProperties:", "Sections:", "ChapterOverrides:" });

        // Custom properties
        data.CustomProperties = ParseCustomProperties(sheet, new[] { "Sections:", "ChapterOverrides:" });

        // Sections
        data.Sections = ParseSections(sheet, new[] { "ChapterOverrides:" });

        // Chapter overrides
        var overridesMatch = Regex.Match(sheet, @"^\s*ChapterOverrides:\s*([\s\S]*)$", RegexOptions.Multiline);
        if (overridesMatch.Success)
        {
            var overridesText = overridesMatch.Groups[1].Value;
            var chapterBlocks = Regex.Split(overridesText, @"^\s*Chapter:[ \t]*", RegexOptions.Multiline)
                .Where(b => !string.IsNullOrWhiteSpace(b)).ToArray();

            foreach (var block in chapterBlocks)
            {
                var lines = block.Split('\n');
                var co = new CharacterOverride { Chapter = lines[0].Trim() };

                for (int i = 1; i < lines.Length; i++)
                {
                    var line = lines[i].Trim();
                    if (string.IsNullOrEmpty(line)) continue;
                    var match = Regex.Match(line, @"^[-*]\s*(.+?)\s*:\s*(.*)$");
                    if (!match.Success) continue;
                    var key = match.Groups[1].Value.Trim().ToLowerInvariant();
                    var val = match.Groups[2].Value.Trim();

                    switch (key)
                    {
                        case "act": co.Act = val; break;
                        case "scene": co.Scene = val; break;
                        case "name": co.Name = val; break;
                        case "surname": co.Surname = val; break;
                        case "gender": co.Gender = val; break;
                        case "age": co.Age = val; break;
                        case "role": co.Role = val; break;
                        case "eyecolor": co.EyeColor = val; break;
                        case "haircolor": co.HairColor = val; break;
                        case "hairlength": co.HairLength = val; break;
                        case "height": co.Height = val; break;
                        case "build": co.Build = val; break;
                        case "skintone": co.SkinTone = val; break;
                        case "distinguishingfeatures": co.DistinguishingFeatures = val; break;
                    }
                }

                data.ChapterOverrides.Add(co);
            }
        }

        return data;
    }

    // ── Location Sheet ──────────────────────────────────────────────

    private static LocationData ParseLocationSheet(string content, string sheetHeading)
    {
        var normalized = content.Replace("\r\n", "\n");
        var data = new LocationData();

        var titleMatch = Regex.Match(normalized, @"^#\s+(.+)$", RegexOptions.Multiline);
        if (titleMatch.Success)
            data.Name = titleMatch.Groups[1].Value.Trim();

        var sheet = GetSheetSection(normalized, sheetHeading);
        if (sheet == null) return data;

        data.Name = NonEmpty(ParseSheetField(sheet, "Name"), data.Name);
        data.Type = ParseSheetField(sheet, "Type");
        data.Parent = StripWikilink(ParseSheetField(sheet, "Parent")); // Will be resolved to ID later
        data.TemplateId = NullIfEmpty(ParseSheetField(sheet, "TemplateId"));

        // Description (multi-line)
        var descNextSections = new[] { "Type:", "Images:", "Relationships:", "CustomProperties:", "Sections:" };
        data.Description = ParseMultiLineField(sheet, "Description", descNextSections);
        if (string.IsNullOrEmpty(data.Description))
            data.Description = ParseSheetField(sheet, "Description");

        data.Images = ParseImages(sheet, new[] { "CustomProperties:", "Sections:" });
        data.CustomProperties = ParseCustomProperties(sheet, new[] { "Sections:" });
        data.Sections = ParseSections(sheet, Array.Empty<string>());

        return data;
    }

    // ── Item Sheet ──────────────────────────────────────────────────

    private static ItemData ParseItemSheet(string content, string sheetHeading)
    {
        var normalized = content.Replace("\r\n", "\n");
        var data = new ItemData();

        var titleMatch = Regex.Match(normalized, @"^#\s+(.+)$", RegexOptions.Multiline);
        if (titleMatch.Success)
            data.Name = titleMatch.Groups[1].Value.Trim();

        var sheet = GetSheetSection(normalized, sheetHeading);
        if (sheet == null) return data;

        data.Name = NonEmpty(ParseSheetField(sheet, "Name"), data.Name);
        data.Type = ParseSheetField(sheet, "Type");
        data.Origin = ParseSheetField(sheet, "Origin");
        data.TemplateId = NullIfEmpty(ParseSheetField(sheet, "TemplateId"));

        var descNextSections = new[] { "Origin:", "Type:", "Images:", "CustomProperties:", "Sections:" };
        data.Description = ParseMultiLineField(sheet, "Description", descNextSections);
        if (string.IsNullOrEmpty(data.Description))
            data.Description = ParseSheetField(sheet, "Description");

        data.Images = ParseImages(sheet, new[] { "CustomProperties:", "Sections:" });
        data.CustomProperties = ParseCustomProperties(sheet, new[] { "Sections:" });
        data.Sections = ParseSections(sheet, Array.Empty<string>());

        return data;
    }

    // ── Lore Sheet ──────────────────────────────────────────────────

    private static LoreData ParseLoreSheet(string content, string sheetHeading)
    {
        var normalized = content.Replace("\r\n", "\n");
        var data = new LoreData();

        var titleMatch = Regex.Match(normalized, @"^#\s+(.+)$", RegexOptions.Multiline);
        if (titleMatch.Success)
            data.Name = titleMatch.Groups[1].Value.Trim();

        var sheet = GetSheetSection(normalized, sheetHeading);
        if (sheet == null) return data;

        data.Name = NonEmpty(ParseSheetField(sheet, "Name"), data.Name);
        data.Category = NonEmpty(ParseSheetField(sheet, "Category"), "Other");
        data.TemplateId = NullIfEmpty(ParseSheetField(sheet, "TemplateId"));

        var descNextSections = new[] { "Category:", "Images:", "CustomProperties:", "Sections:" };
        data.Description = ParseMultiLineField(sheet, "Description", descNextSections);
        if (string.IsNullOrEmpty(data.Description))
            data.Description = ParseSheetField(sheet, "Description");

        data.Images = ParseImages(sheet, new[] { "CustomProperties:", "Sections:" });
        data.CustomProperties = ParseCustomProperties(sheet, new[] { "Sections:" });
        data.Sections = ParseSections(sheet, Array.Empty<string>());

        return data;
    }

    // ── Wikilink Resolution ─────────────────────────────────────────

    private static Dictionary<string, string> BuildEntityNameLookup(
        List<CharacterData> characters,
        List<LocationData> locations,
        List<ItemData> items,
        List<LoreData> lore)
    {
        var lookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var c in characters)
        {
            var fullName = string.IsNullOrEmpty(c.Surname) ? c.Name : $"{c.Name} {c.Surname}";
            lookup.TryAdd(fullName, c.Id);
            if (!string.IsNullOrEmpty(c.Name))
                lookup.TryAdd(c.Name, c.Id);
        }

        foreach (var l in locations)
            if (!string.IsNullOrEmpty(l.Name))
                lookup.TryAdd(l.Name, l.Id);

        foreach (var i in items)
            if (!string.IsNullOrEmpty(i.Name))
                lookup.TryAdd(i.Name, i.Id);

        foreach (var l in lore)
            if (!string.IsNullOrEmpty(l.Name))
                lookup.TryAdd(l.Name, l.Id);

        return lookup;
    }

    private static void ResolveWikilinkReferences(
        List<CharacterData> characters,
        List<LocationData> locations,
        Dictionary<string, string> entityNameToId)
    {
        // Relationship targets stay as display names (standalone uses names, not IDs).
        // Only strip leftover wikilink brackets from chapter override relationships.
        foreach (var c in characters)
        {
            foreach (var co in c.ChapterOverrides)
            {
                if (co.Relationships != null)
                {
                    foreach (var rel in co.Relationships)
                    {
                        rel.Target = StripWikilink(rel.Target);
                    }
                }
            }
        }

        // Resolve location parent references (standalone uses entity IDs for parent)
        foreach (var l in locations)
        {
            if (!string.IsNullOrEmpty(l.Parent))
            {
                var name = StripWikilink(l.Parent);
                if (entityNameToId.TryGetValue(name, out var id))
                    l.Parent = id;
            }
        }
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

    private static string StripWikilink(string value)
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

    /// <summary>
    /// Remaps entity image paths from vault-relative to book-relative,
    /// and copies individually referenced images that weren't part of the bulk copy.
    /// </summary>
    private void RemapAndCopyEntityImages(
        IEnumerable<EntityImage> images,
        string vaultRoot,
        string sourceRootRelative,
        string sourceImageFolderName,
        string destImageFolderName,
        string destImageDir)
    {
        string[] imageExts = [".png", ".jpg", ".jpeg", ".gif", ".bmp", ".svg", ".webp"];

        foreach (var img in images)
        {
            if (string.IsNullOrEmpty(img.Path)) continue;

            var originalPath = img.Path;

            // Normalize slashes
            var imgPath = img.Path.Replace('\\', '/');

            // Strip the project subfolder prefix if present
            // e.g. "Novel/Images/pic.png" → "Images/pic.png"
            if (sourceRootRelative != "." && !string.IsNullOrEmpty(sourceRootRelative) &&
                imgPath.StartsWith(sourceRootRelative + "/", StringComparison.OrdinalIgnoreCase))
            {
                imgPath = imgPath[(sourceRootRelative.Length + 1)..];
            }

            // Remap the source image folder name to the destination folder name
            // e.g. if source uses "Bilder" but standalone uses "Images"
            if (!string.Equals(sourceImageFolderName, destImageFolderName, StringComparison.OrdinalIgnoreCase) &&
                imgPath.StartsWith(sourceImageFolderName + "/", StringComparison.OrdinalIgnoreCase))
            {
                imgPath = destImageFolderName + imgPath[sourceImageFolderName.Length..];
            }

            // If the path doesn't start with the image folder, it's a bare filename
            // (Obsidian resolves filenames globally). Search the already-copied
            // destination images directory for a matching filename.
            if (!imgPath.StartsWith(destImageFolderName + "/", StringComparison.OrdinalIgnoreCase))
            {
                var fileName = Path.GetFileName(imgPath);
                string? foundRelative = null;

                // Search the destination images directory recursively for the filename
                if (Directory.Exists(destImageDir))
                {
                    var matches = Directory.GetFiles(destImageDir, fileName, SearchOption.AllDirectories);
                    if (matches.Length > 0)
                    {
                        // Use the path relative to the parent of the images dir (book root)
                        var bookRoot = Path.GetDirectoryName(destImageDir)!;
                        foundRelative = Path.GetRelativePath(bookRoot, matches[0]).Replace('\\', '/');
                        LogLine($"[ImageRemap] '{originalPath}' → '{foundRelative}' (found in copied images)");
                    }
                }

                if (foundRelative != null)
                {
                    imgPath = foundRelative;
                }
                else
                {
                    // Fall back: try to find the file in the vault using the original path
                    var vaultFile = Path.Combine(vaultRoot, img.Path.Replace('/', Path.DirectorySeparatorChar));
                    LogLine($"[ImageRemap] '{originalPath}' → '{imgPath}' (not found in copied images, trying vault: {vaultFile} exists={File.Exists(vaultFile)})");
                    if (File.Exists(vaultFile))
                    {
                        var ext = Path.GetExtension(vaultFile).ToLowerInvariant();
                        if (imageExts.Contains(ext))
                        {
                            Directory.CreateDirectory(destImageDir);
                            var destFile = Path.Combine(destImageDir, Path.GetFileName(vaultFile));
                            if (!File.Exists(destFile))
                                File.Copy(vaultFile, destFile);
                            imgPath = destImageFolderName + "/" + Path.GetFileName(vaultFile);
                        }
                    }
                }
            }

            LogLine($"[ImageRemap] '{originalPath}' → '{imgPath}'");
            img.Path = imgPath;
        }
    }

    private static string ConvertMarkdownToHtml(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
            return "<html><head></head><body><p></p></body></html>";

        var pipeline = new MarkdownPipelineBuilder().UseEmphasisExtras().Build();
        var bodyHtml = Markdown.ToHtml(markdown, pipeline);

        // The AvRichTextBox only supports <p> with inline <span style="..."> for
        // bold/italic/underline/strikethrough. Convert Markdig output accordingly.
        bodyHtml = SanitizeHtmlForRichTextBox(bodyHtml);

        return $"<html><head></head><body>{bodyHtml}</body></html>";
    }

    /// <summary>
    /// Converts Markdig HTML output to the subset supported by AvRichTextBox:
    /// only &lt;p&gt; elements with &lt;span style="..."&gt; for formatting.
    /// </summary>
    private static string SanitizeHtmlForRichTextBox(string html)
    {
        // Convert <strong>...</strong> to <span style="font-weight:bold">...</span>
        html = Regex.Replace(html, @"<strong>(.*?)</strong>", @"<span style=""font-weight:bold"">$1</span>", RegexOptions.Singleline);

        // Convert <em>...</em> to <span style="font-style:italic">...</span>
        html = Regex.Replace(html, @"<em>(.*?)</em>", @"<span style=""font-style:italic"">$1</span>", RegexOptions.Singleline);

        // Convert <del>...</del> to <span style="text-decoration:line-through">...</span>
        html = Regex.Replace(html, @"<del>(.*?)</del>", @"<span style=""text-decoration:line-through"">$1</span>", RegexOptions.Singleline);

        // Convert list items to paragraphs: <li>...</li> → <p>...</p>
        // Prefix with bullet character for unordered lists
        html = Regex.Replace(html, @"<li>\s*<p>(.*?)</p>\s*</li>", "<p>\u2022 $1</p>", RegexOptions.Singleline);
        html = Regex.Replace(html, @"<li>(.*?)</li>", "<p>\u2022 $1</p>", RegexOptions.Singleline);

        // Strip list wrapper tags
        html = Regex.Replace(html, @"</?[uo]l[^>]*>", "", RegexOptions.IgnoreCase);

        // Convert headings to bold paragraphs
        html = Regex.Replace(html, @"<h[1-6][^>]*>(.*?)</h[1-6]>",
            @"<p><span style=""font-weight:bold"">$1</span></p>", RegexOptions.Singleline);

        // Convert <blockquote> content: unwrap the tag
        html = Regex.Replace(html, @"</?blockquote[^>]*>", "", RegexOptions.IgnoreCase);

        // Convert <br /> or <br> to empty paragraph boundary
        html = Regex.Replace(html, @"<br\s*/?>", "</p><p>", RegexOptions.IgnoreCase);

        // Strip any remaining unsupported tags (keep <p>, </p>, <span...>, </span>)
        html = Regex.Replace(html, @"<(?!/?p[ >/])(?!/?span[ >/])(?!/?br[ >/])[^>]+>", "", RegexOptions.IgnoreCase);

        // Wrap bare text inside <p> tags with <span> — AvRichTextBox only renders
        // <span>, <br>, and <img> inside paragraphs; bare text nodes are dropped.
        html = Regex.Replace(html, @"<p>(.*?)</p>", m =>
        {
            var inner = m.Groups[1].Value;
            if (string.IsNullOrWhiteSpace(inner))
                return "<p></p>";
            // If content already consists entirely of span/br/img tags, leave it
            var stripped = Regex.Replace(inner, @"<span\b[^>]*>.*?</span>|<br\s*/?>|<img\b[^>]*/?>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            if (string.IsNullOrWhiteSpace(stripped))
                return m.Value;
            // Wrap runs of bare text between tags in <span>
            var wrapped = Regex.Replace(inner, @"(?<=>|^)([^<]+)(?=<|$)", "<span>$1</span>");
            return $"<p>{wrapped}</p>";
        }, RegexOptions.Singleline);

        // Clean up empty paragraphs and whitespace between tags
        html = Regex.Replace(html, @"<p>\s*</p>", "<p></p>");

        return html.Trim();
    }

    private static string NonEmpty(string value, string fallback)
        => string.IsNullOrEmpty(value) ? fallback : value;

    private static string? NullIfEmpty(string value)
        => string.IsNullOrEmpty(value) ? null : value;

    private static ChapterStatus MapChapterStatus(string? status) => status?.ToLowerInvariant() switch
    {
        "first-draft" => ChapterStatus.FirstDraft,
        "revised" => ChapterStatus.Revised,
        "edited" => ChapterStatus.Edited,
        "final" => ChapterStatus.Final,
        _ => ChapterStatus.Outline
    };

    private static int CountWords(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        return text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(name.Where(c => !invalid.Contains(c)).ToArray());
        return sanitized.Trim();
    }

    private static void CopyImageFiles(string sourceDir, string destDir)
    {
        if (!Directory.Exists(sourceDir)) return;
        Directory.CreateDirectory(destDir);

        string[] imageExts = [".png", ".jpg", ".jpeg", ".gif", ".bmp", ".svg", ".webp"];
        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var ext = Path.GetExtension(file).ToLowerInvariant();
            if (imageExts.Contains(ext))
            {
                var destFile = Path.Combine(destDir, Path.GetFileName(file));
                if (!File.Exists(destFile))
                    File.Copy(file, destFile);
            }
        }

        // Recurse into subdirectories
        foreach (var subDir in Directory.GetDirectories(sourceDir))
        {
            var subName = Path.GetFileName(subDir);
            CopyImageFiles(subDir, Path.Combine(destDir, subName));
        }
    }

    private static async Task WriteEntityJsonAsync<T>(string path, T entity)
    {
        var json = JsonSerializer.Serialize(entity, JsonWriteOptions);
        await File.WriteAllTextAsync(path, json);
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

    private void ReportProgress(string step, int current, int total)
    {
        ProgressChanged?.Invoke(step, current, total);
    }

    private void LogLine(string message)
    {
        Log.Add(message);
    }

    // ── Internal Types ──────────────────────────────────────────────

    private sealed class ParsedChapter
    {
        public string? Guid { get; set; }
        public string Title { get; set; } = string.Empty;
        public int Order { get; set; }
        public string? Status { get; set; }
        public string? Act { get; set; }
        public string? Date { get; set; }
        public List<ParsedScene> Scenes { get; } = [];
    }

    private sealed class ParsedScene
    {
        public string Title { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
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

/// <summary>
/// Lightweight wrapper around a JsonElement for reading plugin settings.
/// </summary>
public sealed class PluginSettingsData
{
    private readonly JsonElement _root;

    public PluginSettingsData(JsonElement root) => _root = root;

    public string GetStringOrDefault(string key, string defaultValue = "")
    {
        if (_root.TryGetProperty(key, out var prop) && prop.ValueKind == JsonValueKind.String)
            return prop.GetString() ?? defaultValue;
        return defaultValue;
    }

    public JsonElement? GetObjectOrDefault(string key)
    {
        if (_root.TryGetProperty(key, out var prop) && prop.ValueKind == JsonValueKind.Object)
            return prop;
        return null;
    }

    public JsonElement? GetArrayOrDefault(string key)
    {
        if (_root.TryGetProperty(key, out var prop) && prop.ValueKind == JsonValueKind.Array)
            return prop;
        return null;
    }
}

public class PluginDetectionResult
{
    public string VaultRoot { get; set; } = string.Empty;
    public bool HasPluginData { get; set; }
    public List<PluginProjectInfo> Projects { get; } = [];
}

public class PluginProjectInfo
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
}

public class PluginImportResult
{
    public string ProjectPath { get; set; } = string.Empty;
    public Dictionary<string, List<string>> RelationshipPairs { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string? AutoReplacementLanguage { get; set; }
    public List<AutoReplacementPair> AutoReplacements { get; set; } = [];
}
