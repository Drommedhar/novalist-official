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
