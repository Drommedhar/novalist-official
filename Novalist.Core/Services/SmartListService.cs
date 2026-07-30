using Novalist.Core.Models;
using Novalist.Core.Utilities;

namespace Novalist.Core.Services;

public sealed class SmartListService : ISmartListService
{
    private readonly IProjectService _projectService;
    private readonly IEntityService _entityService;

    public SmartListService(IProjectService projectService, IEntityService entityService)
    {
        _projectService = projectService;
        _entityService = entityService;
    }

    public IReadOnlyList<SmartList> GetAll()
    {
        return _projectService.CurrentProject?.SmartLists ?? (IReadOnlyList<SmartList>)Array.Empty<SmartList>();
    }

    public async Task SaveAsync(SmartList list)
    {
        var project = _projectService.CurrentProject;
        if (project == null) return;

        var existing = project.SmartLists.FindIndex(l => string.Equals(l.Id, list.Id, StringComparison.OrdinalIgnoreCase));
        if (existing >= 0) project.SmartLists[existing] = list;
        else project.SmartLists.Add(list);

        await _projectService.SaveProjectAsync().ConfigureAwait(false);
    }

    public async Task DeleteAsync(string listId)
    {
        var project = _projectService.CurrentProject;
        if (project == null) return;

        project.SmartLists.RemoveAll(l => string.Equals(l.Id, listId, StringComparison.OrdinalIgnoreCase));
        await _projectService.SaveProjectAsync().ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<(ChapterData Chapter, SceneData Scene)>> EvaluateAsync(SmartList list)
    {
        var result = new List<(ChapterData, SceneData)>();
        var chapters = _projectService.GetChaptersOrdered();
        var rules = list.EffectiveRules();

        // Cache characters once for POV resolution; cheap.
        var characters = await _entityService.LoadCharactersAsync().ConfigureAwait(false);

        foreach (var chapter in chapters)
        {
            var scenes = _projectService.GetScenesForChapter(chapter.Guid);
            foreach (var scene in scenes)
            {
                if (await MatchesAsync(list, rules, chapter, scene, characters).ConfigureAwait(false))
                    result.Add((chapter, scene));
            }
        }

        return result;
    }

    /// <summary>
    /// A list with no rules matches everything, which is what an unfiltered
    /// collection of the whole book should be rather than an empty one.
    /// </summary>
    private async Task<bool> MatchesAsync(
        SmartList list,
        IReadOnlyList<SmartListRule> rules,
        ChapterData chapter,
        SceneData scene,
        IReadOnlyList<CharacterData> characters)
    {
        if (rules.Count == 0) return true;

        foreach (var rule in rules)
        {
            var holds = await RuleHoldsAsync(rule, chapter, scene, characters).ConfigureAwait(false);
            if (list.Match == SmartListMatch.Any && holds) return true;
            if (list.Match == SmartListMatch.All && !holds) return false;
        }
        return list.Match == SmartListMatch.All;
    }

    private async Task<bool> RuleHoldsAsync(
        SmartListRule rule,
        ChapterData chapter,
        SceneData scene,
        IReadOnlyList<CharacterData> characters)
    {
        // Membership fields are lists rather than single values, so they answer
        // "is one of these" instead of being compared as text.
        if (rule.Field == "tag")
            return Holds(rule, scene.AnalysisOverrides?.Tags ?? []);
        if (rule.Field == "plotline")
            return Holds(rule, scene.PlotlineIds ?? []);
        // Who and what the writer said is in this scene. A saved list that can
        // only read the prose cannot answer "every scene Mira is in", which is
        // the question a writer following one thread actually asks.
        if (rule.Field == "cast")
            return Holds(rule, scene.Cast ?? []);

        var value = rule.Field switch
        {
            "chapterStatus" => chapter.Status.ToString(),
            "act" => chapter.Act,
            "title" => scene.Title,
            "synopsis" => scene.Synopsis ?? string.Empty,
            "notes" => scene.Notes ?? string.Empty,
            "stage" => scene.Stage ?? string.Empty,
            "beat" => scene.BeatKey ?? string.Empty,
            // The two halves of the scene diagnostic. "goal is not set" and
            // "outcome is not set" are the two lists worth having: a scene
            // nobody wanted anything in, and a scene nothing came of.
            "goal" => scene.Goal ?? string.Empty,
            "outcome" => scene.Outcome ?? string.Empty,
            "inactive" => scene.Inactive ? "true" : string.Empty,
            "focus" => scene.FocusEntityId ?? string.Empty,
            "words" => scene.WordCount.ToString(),
            "target" => scene.WordTarget?.ToString() ?? string.Empty,
            "pov" => await ResolvePovAsync(chapter, scene, characters).ConfigureAwait(false),
            _ => rule.Field.StartsWith("prop:", StringComparison.Ordinal)
                && scene.Properties != null
                && scene.Properties.TryGetValue(rule.Field[5..], out var prop)
                    ? prop
                    : string.Empty
        };

        return Holds(rule, value);
    }

    /// <summary>The POV the writer set, or the one the prose gives away.</summary>
    private async Task<string> ResolvePovAsync(
        ChapterData chapter, SceneData scene, IReadOnlyList<CharacterData> characters)
    {
        var pov = scene.AnalysisOverrides?.Pov;
        if (!string.IsNullOrEmpty(pov)) return pov;

        var html = await _projectService.ReadSceneContentAsync(chapter, scene).ConfigureAwait(false);
        var plain = System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", " ");
        return PovDetector.Detect(plain, characters) ?? string.Empty;
    }

    private static bool Holds(SmartListRule rule, string value) => rule.Op switch
    {
        SmartListOperator.IsSet => !string.IsNullOrWhiteSpace(value),
        SmartListOperator.IsNotSet => string.IsNullOrWhiteSpace(value),
        SmartListOperator.Is => string.Equals(value, rule.Value, StringComparison.OrdinalIgnoreCase),
        SmartListOperator.Contains => value.Contains(rule.Value, StringComparison.OrdinalIgnoreCase),
        // A comparison against something that is not a number is not true of
        // anything, rather than quietly comparing as text.
        SmartListOperator.GreaterThan => Numeric(value, rule.Value, (a, b) => a > b),
        _ => Numeric(value, rule.Value, (a, b) => a < b)
    };

    private static bool Holds(SmartListRule rule, IReadOnlyList<string> values) => rule.Op switch
    {
        SmartListOperator.IsSet => values.Count > 0,
        SmartListOperator.IsNotSet => values.Count == 0,
        _ => values.Any(v => Holds(rule, v))
    };

    private static bool Numeric(string value, string other, Func<double, double, bool> compare)
        => double.TryParse(value, out var a)
           && double.TryParse(other, out var b)
           && compare(a, b);
}
