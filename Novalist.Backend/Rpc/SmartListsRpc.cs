using Novalist.Core.Models;
using Novalist.Core.Services;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

/// <summary>Saved scene queries (smart lists): CRUD and evaluation.</summary>
public sealed class SmartListsRpc
{
    private readonly SmartListService _smartLists;
    private readonly Workspace _workspace;

    public SmartListsRpc(Workspace workspace)
    {
        _workspace = workspace;
        _smartLists = new SmartListService(workspace.Projects, new EntityService(workspace.Projects));
    }

    [JsonRpcMethod("smartLists/list")]
    public SmartListDto[] List() => [.. _smartLists.GetAll().Select(ToDto)];

    [JsonRpcMethod("smartLists/save")]
    public async Task<SmartListDto[]> SaveAsync(
        string? id, string name, string match, SmartListRuleDto[] rules)
    {
        var existing = id == null ? null : _smartLists.GetAll().FirstOrDefault(l => l.Id == id);
        var list = existing ?? new SmartList();
        list.Name = name;
        list.Match = Enum.TryParse<SmartListMatch>(match, true, out var parsed)
            ? parsed
            : SmartListMatch.All;
        list.Rules = [.. (rules ?? [])
            .Where(r => !string.IsNullOrWhiteSpace(r.Field))
            .Select(r => new SmartListRule
            {
                Field = r.Field.Trim(),
                Op = Enum.TryParse<SmartListOperator>(r.Op, true, out var op)
                    ? op
                    : SmartListOperator.Contains,
                Value = (r.Value ?? string.Empty).Trim()
            })];
        // The pre-rules fields are cleared once a list has been re-saved, so
        // the two cannot describe different queries.
        list.ChapterStatus = null;
        list.PovContains = null;
        list.Tag = null;
        list.PlotlineId = null;
        await _smartLists.SaveAsync(list);
        return List();
    }

    [JsonRpcMethod("smartLists/delete")]
    public async Task<SmartListDto[]> DeleteAsync(string id)
    {
        await _smartLists.DeleteAsync(id);
        return List();
    }

    [JsonRpcMethod("smartLists/evaluate")]
    public async Task<SmartListMatchDto[]> EvaluateAsync(string id)
    {
        var list = _smartLists.GetAll().FirstOrDefault(l => l.Id == id)
            ?? throw new InvalidOperationException("Unknown smart list.");
        var matches = await _smartLists.EvaluateAsync(list);
        return [.. matches.Select(m =>
            new SmartListMatchDto(m.Chapter.Guid, m.Chapter.Title, m.Scene.Id, m.Scene.Title))];
    }

    /// <summary>
    /// The fields a rule can test, with the values worth offering for each.
    /// Built here rather than in the renderer because only the backend knows
    /// this book's plotlines, stages, tags and the writer's own scene fields.
    /// </summary>
    [JsonRpcMethod("smartLists/fields")]
    public SmartListFieldDto[] Fields()
    {
        var book = _workspace.Projects.ActiveBook;
        var scenes = _workspace.Projects.ScenesManifest?.Chapters.SelectMany(c => c.Value).ToList()
            ?? [];

        var tags = scenes
            .SelectMany(s => s.AnalysisOverrides?.Tags ?? [])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var fields = new List<SmartListFieldDto>
        {
            new("chapterStatus", "chapterStatus", "choice",
                ["Outline", "FirstDraft", "Revised", "Edited", "Final"]),
            new("act", "act", "text",
                [.. (book?.Chapters ?? [])
                    .Select(c => c.Act)
                    .Where(a => !string.IsNullOrWhiteSpace(a))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(a => a, StringComparer.OrdinalIgnoreCase)]),
            new("pov", "pov", "text", []),
            new("tag", "tag", "choice", tags),
            new("plotline", "plotline", "choice",
                [.. (book?.Plotlines ?? []).Select(p => p.Id)]),
            new("stage", "stage", "choice",
                [.. new SceneStageService(_workspace.Projects).Stages().Select(s => s.Key)]),
            new("title", "title", "text", []),
            new("synopsis", "synopsis", "text", []),
            new("notes", "notes", "text", []),
            new("beat", "beat", "text", []),
            new("words", "words", "number", []),
            new("target", "target", "number", [])
        };

        // The writer's own scene fields, so a saved list can ask about them
        // the same way it asks about a built-in one.
        foreach (var property in new ManuscriptPropertyService(_workspace.Projects)
                     .Definitions(ManuscriptPropertyScope.Scene))
        {
            fields.Add(new SmartListFieldDto(
                $"prop:{property.Key}",
                property.Label,
                property.Type switch
                {
                    CustomPropertyType.Int => "number",
                    CustomPropertyType.Enum => "choice",
                    CustomPropertyType.Bool => "choice",
                    _ => "text"
                },
                property.Type == CustomPropertyType.Bool
                    ? ["true"]
                    : property.EnumOptions?.ToArray() ?? []));
        }

        return [.. fields];
    }

    private static SmartListDto ToDto(SmartList l) => new(
        l.Id,
        l.Name,
        l.Match.ToString(),
        [.. l.EffectiveRules().Select(r => new SmartListRuleDto(r.Field, r.Op.ToString(), r.Value))]);
}

public sealed record SmartListRuleDto(string Field, string Op, string? Value);

public sealed record SmartListDto(
    string Id, string Name, string Match, SmartListRuleDto[] Rules);

/// <summary>One field a rule can test, and what it is worth offering for it.</summary>
public sealed record SmartListFieldDto(
    string Field, string Label, string Kind, string[] Options);

public sealed record SmartListMatchDto(
    string ChapterGuid, string ChapterTitle, string SceneId, string SceneTitle);
