using System.Globalization;
using Novalist.Core.Models;

namespace Novalist.Core.Services;

/// <summary>
/// Fields the writer added to every scene or every chapter, and the values on
/// each one.
///
/// Definitions live on the book rather than in app settings, for the same
/// reason scene stages do: the fields worth tracking in a thriller and in a
/// short-story collection are rarely the same list.
/// </summary>
public sealed class ManuscriptPropertyService
{
    private readonly IProjectService _projectService;

    public ManuscriptPropertyService(IProjectService projectService)
    {
        _projectService = projectService;
    }

    /// <summary>Every defined property, optionally narrowed to one scope.</summary>
    public IReadOnlyList<ManuscriptPropertyDefinition> Definitions(
        ManuscriptPropertyScope? scope = null)
    {
        var all = _projectService.ActiveBook?.ManuscriptProperties ?? [];
        return scope == null ? [.. all] : [.. all.Where(d => d.Scope == scope)];
    }

    /// <summary>
    /// Replaces the definition list. A blank key or label is dropped, and so is
    /// a duplicate key within a scope - two fields sharing a key would make a
    /// value ambiguous. The same key may exist once per scope, since a scene
    /// "mood" and a chapter "mood" are different questions.
    /// </summary>
    public async Task<IReadOnlyList<ManuscriptPropertyDefinition>> SetDefinitionsAsync(
        IEnumerable<ManuscriptPropertyDefinition> definitions)
    {
        var book = _projectService.ActiveBook
            ?? throw new InvalidOperationException("No active book.");

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var clean = new List<ManuscriptPropertyDefinition>();
        foreach (var definition in definitions)
        {
            var key = (definition.Key ?? string.Empty).Trim();
            var label = (definition.Label ?? string.Empty).Trim();
            if (key.Length == 0 || label.Length == 0) continue;
            if (!seen.Add($"{definition.Scope}:{key}")) continue;

            var options = definition.EnumOptions?
                .Select(o => (o ?? string.Empty).Trim())
                .Where(o => o.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            clean.Add(new ManuscriptPropertyDefinition
            {
                Key = key,
                Label = label,
                // An enum with nothing to choose from is a text field that
                // refuses every value, so it falls back rather than trapping.
                Type = definition.Type == CustomPropertyType.Enum && (options?.Count ?? 0) == 0
                    ? CustomPropertyType.String
                    : Supported(definition.Type),
                EnumOptions = options is { Count: > 0 } ? options : null,
                Scope = definition.Scope,
                ShowInOutliner = definition.ShowInOutliner
            });
        }

        book.ManuscriptProperties = clean;
        await _projectService.SaveProjectAsync();
        await PruneValuesAsync(clean);
        return clean;
    }

    /// <summary>
    /// Only the types that mean something on a manuscript object. Entity
    /// references and timespans belong to Codex entries, where there is an
    /// entity to point at and a birth date to measure from.
    /// </summary>
    private static CustomPropertyType Supported(CustomPropertyType type) => type switch
    {
        CustomPropertyType.Int => CustomPropertyType.Int,
        CustomPropertyType.Bool => CustomPropertyType.Bool,
        CustomPropertyType.Date => CustomPropertyType.Date,
        CustomPropertyType.Enum => CustomPropertyType.Enum,
        _ => CustomPropertyType.String
    };

    /// <summary>Values on one scene, keyed by property key.</summary>
    public IReadOnlyDictionary<string, string> SceneValues(string sceneId)
        => FindScene(sceneId)?.Properties ?? new Dictionary<string, string>();

    /// <summary>Values on one chapter, keyed by property key.</summary>
    public IReadOnlyDictionary<string, string> ChapterValues(string chapterGuid)
        => FindChapter(chapterGuid)?.Properties ?? new Dictionary<string, string>();

    /// <summary>
    /// Sets one value on a scene. A blank value removes it rather than storing
    /// an empty string, so "not set" and "set to nothing" stay one state.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, string>> SetSceneValueAsync(
        string sceneId, string key, string? value)
    {
        var scene = FindScene(sceneId) ?? throw new InvalidOperationException($"Unknown scene '{sceneId}'.");
        scene.Properties = Apply(scene.Properties, ManuscriptPropertyScope.Scene, key, value);
        await _projectService.SaveScenesAsync();
        return scene.Properties ?? new Dictionary<string, string>();
    }

    /// <summary>Sets one value on a chapter.</summary>
    public async Task<IReadOnlyDictionary<string, string>> SetChapterValueAsync(
        string chapterGuid, string key, string? value)
    {
        var chapter = FindChapter(chapterGuid)
            ?? throw new InvalidOperationException($"Unknown chapter '{chapterGuid}'.");
        chapter.Properties = Apply(chapter.Properties, ManuscriptPropertyScope.Chapter, key, value);
        await _projectService.SaveProjectAsync();
        return chapter.Properties ?? new Dictionary<string, string>();
    }

    private Dictionary<string, string>? Apply(
        Dictionary<string, string>? values, ManuscriptPropertyScope scope, string key, string? value)
    {
        var definition = Definitions(scope).FirstOrDefault(
            d => string.Equals(d.Key, key, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Unknown property '{key}'.");

        var normalised = Normalise(definition, value);
        var next = values ?? [];
        if (normalised == null) next.Remove(definition.Key);
        else next[definition.Key] = normalised;
        return next.Count > 0 ? next : null;
    }

    /// <summary>
    /// The value as it will be stored, or null to clear it. A value the type
    /// cannot hold is refused rather than stored: a tension column that has to
    /// cope with "quite high" is not a number column any more.
    /// </summary>
    public static string? Normalise(ManuscriptPropertyDefinition definition, string? value)
    {
        var raw = (value ?? string.Empty).Trim();
        if (raw.Length == 0) return null;

        switch (definition.Type)
        {
            case CustomPropertyType.Int:
                return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
                    ? n.ToString(CultureInfo.InvariantCulture)
                    : null;
            case CustomPropertyType.Bool:
                // Anything that is not plainly true reads as false, so a
                // checkbox never round-trips into a third state.
                return bool.TryParse(raw, out var b) && b ? "true" : null;
            case CustomPropertyType.Date:
                return DateTime.TryParse(raw, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var date)
                    ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                    : null;
            case CustomPropertyType.Enum:
                return definition.EnumOptions?.FirstOrDefault(
                    o => string.Equals(o, raw, StringComparison.OrdinalIgnoreCase));
            default:
                return raw;
        }
    }

    /// <summary>
    /// Drops stored values whose definition is gone. Left behind they would be
    /// invisible in every surface yet still travel with the project, and would
    /// come back to life under a later field that happened to reuse the key.
    /// </summary>
    private async Task PruneValuesAsync(IReadOnlyList<ManuscriptPropertyDefinition> definitions)
    {
        var sceneKeys = definitions
            .Where(d => d.Scope == ManuscriptPropertyScope.Scene)
            .Select(d => d.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var chapterKeys = definitions
            .Where(d => d.Scope == ManuscriptPropertyScope.Chapter)
            .Select(d => d.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var scenesTouched = false;
        foreach (var scene in AllScenes())
        {
            if (scene.Properties == null) continue;
            foreach (var key in scene.Properties.Keys.Where(k => !sceneKeys.Contains(k)).ToList())
            {
                scene.Properties.Remove(key);
                scenesTouched = true;
            }
            if (scene.Properties.Count == 0) scene.Properties = null;
        }

        var chaptersTouched = false;
        foreach (var chapter in _projectService.ActiveBook?.Chapters ?? [])
        {
            if (chapter.Properties == null) continue;
            foreach (var key in chapter.Properties.Keys.Where(k => !chapterKeys.Contains(k)).ToList())
            {
                chapter.Properties.Remove(key);
                chaptersTouched = true;
            }
            if (chapter.Properties.Count == 0) chapter.Properties = null;
        }

        if (scenesTouched) await _projectService.SaveScenesAsync();
        if (chaptersTouched) await _projectService.SaveProjectAsync();
    }

    private IEnumerable<SceneData> AllScenes()
    {
        var manifest = _projectService.ScenesManifest;
        if (manifest == null) yield break;
        foreach (var scene in manifest.Chapters.SelectMany(c => c.Value)) yield return scene;
        foreach (var scene in manifest.Archived) yield return scene;
    }

    private SceneData? FindScene(string sceneId)
        => AllScenes().FirstOrDefault(s => s.Id == sceneId);

    private ChapterData? FindChapter(string chapterGuid)
        => _projectService.ActiveBook?.Chapters.FirstOrDefault(c => c.Guid == chapterGuid);
}
