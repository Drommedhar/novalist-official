using Novalist.Backend.Extensions;
using Novalist.Core.Services;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

/// <summary>
/// Obsidian plugin-vault import: detect plugin projects in a folder, then run
/// the full import into a standalone project. Mirrors ImportPluginDialog:
/// the import log is written into the new project, and learned relationship
/// pairs / auto-replacements are merged into app settings.
/// </summary>
public sealed class ImportRpc(Workspace workspace)
{
    /// <summary>Test seam for the import-log write (real file IO otherwise).</summary>
    internal Func<string, string[], Task>? LogWriterOverride { get; set; }

    [JsonRpcMethod("import/detect")]
    public async Task<ImportDetectionDto> DetectAsync(string vaultRoot)
    {
        var result = await PluginImportService.DetectPluginProjectAsync(vaultRoot);
        return new ImportDetectionDto(
            result.HasPluginData,
            result.Projects.Select(p => new ImportProjectDto(p.Name, p.Path)).ToArray());
    }

    /// <summary>
    /// What a folder of Markdown files holds, without importing anything.
    ///
    /// Novalist imported one thing: a vault made by the old Obsidian plugin,
    /// with its own metadata files. A folder of ordinary notes - what a vault
    /// is once the plugin is gone, and what every other tool exports - had no
    /// way in.
    /// </summary>
    [JsonRpcMethod("import/scanVault")]
    public VaultScanDto ScanVault(string vaultRoot)
    {
        var notes = MarkdownVaultImport.Scan(vaultRoot);
        return new VaultScanDto(
            notes.Count,
            // Named rather than counted: a writer about to import four hundred
            // notes should see what the first of them are.
            [.. notes.Take(20).Select(n => new VaultNoteDto(n.RelativePath, n.Title, [.. n.Tags]))],
            [.. notes.SelectMany(n => n.Tags)
                .Distinct(StringComparer.CurrentCultureIgnoreCase)
                .OrderBy(t => t, StringComparer.CurrentCultureIgnoreCase)]);
    }

    /// <summary>
    /// Brings a folder of Markdown files in as research notes.
    ///
    /// Every note lands as research rather than being sorted into the Codex. A
    /// note about a character and a note about a battle look identical, and an
    /// import that files half of them wrongly is worse than one that files all
    /// of them somewhere the writer can move them from.
    /// </summary>
    [JsonRpcMethod("import/vault")]
    public async Task<int> ImportVaultAsync(string vaultRoot, string? tag = null)
    {
        var research = new ResearchService(workspace.Projects, workspace.FileService);
        var existing = research.GetAll()
            .Select(r => r.Properties?.GetValueOrDefault(SourceProperty))
            .Where(p => !string.IsNullOrEmpty(p))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var extra = (tag ?? string.Empty).Trim();
        var imported = 0;

        foreach (var note in MarkdownVaultImport.Scan(vaultRoot))
        {
            // Where it came from, so importing the same folder twice updates
            // nothing and duplicates nothing.
            if (existing.Contains(note.RelativePath)) continue;

            var item = new Core.Models.ResearchItem
            {
                Title = note.Title,
                Content = note.Body,
                Type = Core.Models.ResearchItemType.Note,
                Tags = [.. note.Tags, .. extra.Length > 0 ? new[] { extra } : []],
                Properties = new Dictionary<string, string> { [SourceProperty] = note.RelativePath }
            };
            await research.SaveAsync(item);
            imported++;
        }

        Log.Info($"import/vault imported={imported}.");
        return imported;
    }

    /// <summary>Where an imported note came from, so a second run skips it.</summary>
    private const string SourceProperty = "importedFrom";

    [JsonRpcMethod("import/run")]
    public async Task<ImportRunDto> RunAsync(
        string vaultRoot, string projectPath, string outputDirectory, string projectName, string bookName)
    {
        var service = new PluginImportService();
        var result = await service.ImportAsync(vaultRoot, projectPath, outputDirectory, projectName, bookName);

        if (service.Log.Count > 0 && !string.IsNullOrEmpty(result.ProjectPath))
        {
            var writeLog = LogWriterOverride
                ?? ((path, lines) => File.WriteAllLinesAsync(path, lines));
            try
            {
                await writeLog(Path.Combine(result.ProjectPath, "import-log.txt"), [.. service.Log]);
            }
            catch (IOException)
            {
                // The import itself succeeded; a missing log is not fatal.
            }
        }

        var settings = workspace.Settings.Settings;
        var changed = false;
        foreach (var (role, inverses) in result.RelationshipPairs)
        {
            foreach (var inverse in inverses)
                changed |= settings.LearnRelationshipPair(role, inverse);
        }
        if (result.AutoReplacements.Count > 0 && settings.AutoReplacements.Count == 0)
        {
            settings.AutoReplacements = result.AutoReplacements;
            changed = true;
        }
        if (!string.IsNullOrEmpty(result.AutoReplacementLanguage))
        {
            settings.AutoReplacementLanguage = result.AutoReplacementLanguage;
            changed = true;
        }
        if (changed)
            await workspace.Settings.SaveAsync();

        return new ImportRunDto(result.ProjectPath);
    }
}

public sealed record ImportDetectionDto(bool HasPluginData, IReadOnlyList<ImportProjectDto> Projects);

public sealed record ImportProjectDto(string Name, string Path);

public sealed record ImportRunDto(string ProjectPath);

/// <summary>One Markdown file found in a folder.</summary>
public sealed record VaultNoteDto(string RelativePath, string Title, string[] Tags);

/// <summary>What a folder of Markdown files holds.</summary>
public sealed record VaultScanDto(int Total, VaultNoteDto[] FirstFew, string[] Tags);
