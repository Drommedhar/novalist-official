using Novalist.Core.Models;
using Novalist.Core.Services;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

/// <summary>
/// The story structure the book is written against, and where the manuscript
/// actually puts each of its beats.
/// </summary>
public sealed class StructureRpc
{
    private readonly Workspace _workspace;

    public StructureRpc(Workspace workspace)
    {
        _workspace = workspace;
    }

    private StoryStructureService Service => new(_workspace.Projects);

    /// <summary>The chosen structure's id, or empty for none.</summary>
    [JsonRpcMethod("structure/get")]
    public string Get() => _workspace.Projects.ActiveBook?.StructureTemplateId ?? string.Empty;

    [JsonRpcMethod("structure/set")]
    public async Task<StructureBeatDto[]> SetAsync(string? templateId)
    {
        await Service.SetTemplateAsync(templateId);
        return Beats();
    }

    /// <summary>Every beat with the scene bound to it and how far off its
    /// intended position that scene sits.</summary>
    [JsonRpcMethod("structure/beats")]
    public StructureBeatDto[] Beats()
        => [.. Service.Beats().Select(b => new StructureBeatDto(
            b.Key, b.Title, b.Description, b.TargetPercent,
            b.SceneId, b.SceneTitle, b.ChapterGuid,
            b.ActualPercent, b.IsFilled, b.DriftPercent))];

    [JsonRpcMethod("structure/bindScene")]
    public async Task<StructureBeatDto[]> BindSceneAsync(
        string chapterGuid, string sceneId, string? beatKey)
    {
        await Service.SetSceneBeatAsync(chapterGuid, sceneId, beatKey);
        return Beats();
    }

    /// <summary>Creates a placeholder scene for every unfilled beat, and returns
    /// the fresh project state so the binder shows them.</summary>
    [JsonRpcMethod("structure/fillGaps")]
    public async Task<FillGapsResultDto> FillGapsAsync()
    {
        var created = await Service.FillGapsAsync();
        return new FillGapsResultDto(created, Beats(), _workspace.BuildState());
    }

    /// <summary>The structures on offer, with the beats each defines.</summary>
    [JsonRpcMethod("structure/templates")]
    public StructureTemplateBeatsDto[] Templates()
    {
        var builtIn = StoryStructureTemplates.All
            .Select(t => t.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return [.. Service.Available().Select(t => new StructureTemplateBeatsDto(
            t.Id, t.DisplayName, t.Description, t.Beats.Count,
            !builtIn.Contains(t.Id) || IsOverridden(t.Id)))];
    }

    /// <summary>Whether a built-in id has been replaced by the writer's own.</summary>
    private bool IsOverridden(string id)
        => (_workspace.Projects.CurrentProject?.CustomStructures ?? [])
            .Any(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>One structure in full, for editing or exporting.</summary>
    [JsonRpcMethod("structure/template")]
    public StructureDefinitionDto? Template(string id)
    {
        var found = Service.Find(id);
        return found == null ? null : ToDto(found);
    }

    /// <summary>
    /// Saves a structure the writer authored. A beat with no title cannot be
    /// bound to anything, so it is dropped rather than stored as a blank row.
    /// </summary>
    [JsonRpcMethod("structure/saveTemplate")]
    public async Task<StructureTemplateBeatsDto[]> SaveTemplateAsync(StructureDefinitionDto template)
    {
        var project = _workspace.Projects.CurrentProject
            ?? throw new InvalidOperationException("No project open.");

        var id = (template.Id ?? string.Empty).Trim();
        if (id.Length == 0) id = $"custom-{Guid.NewGuid():N}";
        var name = (template.DisplayName ?? string.Empty).Trim();
        if (name.Length == 0) throw new InvalidOperationException("A structure needs a name.");

        var saved = new StoryStructureTemplate
        {
            Id = id,
            DisplayName = name,
            Description = (template.Description ?? string.Empty).Trim(),
            Beats = [.. (template.Beats ?? [])
                .Where(b => !string.IsNullOrWhiteSpace(b.Title))
                .Select(b => new StoryStructureBeat
                {
                    Key = (b.Key ?? string.Empty).Trim(),
                    Title = b.Title!.Trim(),
                    Description = (b.Description ?? string.Empty).Trim(),
                    // A beat outside the manuscript cannot be drifted from.
                    TargetPercent = Math.Clamp(b.TargetPercent, 0, 100),
                    CategoryId = string.IsNullOrWhiteSpace(b.CategoryId) ? "plot" : b.CategoryId!.Trim()
                })]
        };

        var index = project.CustomStructures.FindIndex(
            t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase));
        if (index >= 0) project.CustomStructures[index] = saved;
        else project.CustomStructures.Add(saved);

        await _workspace.Projects.SaveProjectAsync();
        return Templates();
    }

    /// <summary>
    /// Removes a structure the writer authored. A book written against it stops
    /// pointing at something that no longer exists.
    /// </summary>
    [JsonRpcMethod("structure/deleteTemplate")]
    public async Task<StructureTemplateBeatsDto[]> DeleteTemplateAsync(string id)
    {
        var project = _workspace.Projects.CurrentProject
            ?? throw new InvalidOperationException("No project open.");

        if (project.CustomStructures.RemoveAll(
                t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase)) > 0)
        {
            foreach (var book in project.Books)
                if (string.Equals(book.StructureTemplateId, id, StringComparison.OrdinalIgnoreCase)
                    && StoryStructureTemplates.GetById(id) == null)
                    book.StructureTemplateId = string.Empty;
            await _workspace.Projects.SaveProjectAsync();
        }

        return Templates();
    }

    /// <summary>
    /// A structure as shareable JSON. This is the exchange format: a writer can
    /// send a method to somebody else without either of them needing Novalist
    /// to have heard of it.
    /// </summary>
    [JsonRpcMethod("structure/exportTemplate")]
    public async Task ExportTemplateAsync(string id, string outputPath)
    {
        var found = Service.Find(id)
            ?? throw new InvalidOperationException($"Unknown structure '{id}'.");
        await File.WriteAllTextAsync(outputPath, System.Text.Json.JsonSerializer.Serialize(found,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>Reads a shared structure back in, under a fresh id if it clashes.</summary>
    [JsonRpcMethod("structure/importTemplate")]
    public async Task<StructureTemplateBeatsDto[]> ImportTemplateAsync(string path)
    {
        StoryStructureTemplate? parsed;
        try
        {
            parsed = System.Text.Json.JsonSerializer.Deserialize<StoryStructureTemplate>(
                await File.ReadAllTextAsync(path));
        }
        // A directory, or a file the writer cannot read, is the same answer as
        // a missing one: nothing usable came back.
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException("That file could not be read.");
        }
        catch (System.Text.Json.JsonException)
        {
            throw new InvalidOperationException("That is not a structure file.");
        }
        if (parsed == null || string.IsNullOrWhiteSpace(parsed.DisplayName))
            throw new InvalidOperationException("That is not a structure file.");

        // Importing must never silently replace a structure already in use, so
        // a clashing id becomes a new one.
        var id = parsed.Id;
        if (Service.Find(id) != null) id = $"custom-{Guid.NewGuid():N}";

        return await SaveTemplateAsync(new StructureDefinitionDto(
            id, parsed.DisplayName, parsed.Description,
            [.. parsed.Beats.Select(b => new StructureBeatDefDto(
                b.Key, b.Title, b.Description, b.TargetPercent, b.CategoryId))]));
    }

    private static StructureDefinitionDto ToDto(StoryStructureTemplate t) => new(
        t.Id, t.DisplayName, t.Description,
        [.. t.Beats.Select(b => new StructureBeatDefDto(
            b.Key, b.Title, b.Description, b.TargetPercent, b.CategoryId))]);
}

/// <summary>One beat as the editor sees it.</summary>
public sealed record StructureBeatDefDto(
    string? Key, string? Title, string? Description, int TargetPercent, string? CategoryId);

/// <summary>A structure in full, for editing, import and export.</summary>
public sealed record StructureDefinitionDto(
    string? Id, string? DisplayName, string? Description, StructureBeatDefDto[]? Beats);

/// <summary>
/// One beat and where the manuscript puts it. <c>ActualPercent</c> is -1 when
/// nothing is bound, which is not the same as a beat at the very start.
/// </summary>
public sealed record StructureBeatDto(
    string Key,
    string Title,
    string Description,
    int TargetPercent,
    string? SceneId,
    string? SceneTitle,
    string? ChapterGuid,
    int ActualPercent,
    bool IsFilled,
    int DriftPercent);

public sealed record StructureTemplateBeatsDto(
    string Id, string DisplayName, string Description, int BeatCount, bool IsCustom);

public sealed record FillGapsResultDto(
    int Created, StructureBeatDto[] Beats, ProjectStateDto State);
