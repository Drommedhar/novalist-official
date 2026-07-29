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
    private readonly IResearchService? _research;

    public ManuscriptPropertyService(
        IProjectService projectService, IResearchService? research = null)
    {
        _projectService = projectService;
        _research = research;
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

    /// <summary>Values on one plotline, keyed by property key.</summary>
    public IReadOnlyDictionary<string, string> PlotlineValues(string plotlineId)
        => FindPlotline(plotlineId)?.Properties ?? new Dictionary<string, string>();

    /// <summary>Values on one manual timeline event.</summary>
    public IReadOnlyDictionary<string, string> EventValues(string eventId)
        => FindEvent(eventId)?.Properties ?? new Dictionary<string, string>();

    /// <summary>Values on one research item.</summary>
    public IReadOnlyDictionary<string, string> ResearchValues(string itemId)
        => FindResearch(itemId)?.Properties ?? new Dictionary<string, string>();

    /// <summary>Sets one value on a plotline.</summary>
    public async Task<IReadOnlyDictionary<string, string>> SetPlotlineValueAsync(
        string plotlineId, string key, string? value)
    {
        var plotline = FindPlotline(plotlineId)
            ?? throw new InvalidOperationException($"Unknown plotline '{plotlineId}'.");
        plotline.Properties = Apply(
            plotline.Properties, ManuscriptPropertyScope.Plotline, key, value);
        await _projectService.SaveProjectAsync();
        return plotline.Properties ?? new Dictionary<string, string>();
    }

    /// <summary>Sets one value on a manual timeline event.</summary>
    public async Task<IReadOnlyDictionary<string, string>> SetEventValueAsync(
        string eventId, string key, string? value)
    {
        var story = FindEvent(eventId)
            ?? throw new InvalidOperationException($"Unknown event '{eventId}'.");
        story.Properties = Apply(story.Properties, ManuscriptPropertyScope.Event, key, value);
        await _projectService.SaveProjectSettingsAsync();
        return story.Properties ?? new Dictionary<string, string>();
    }

    /// <summary>Sets one value on a research item.</summary>
    public async Task<IReadOnlyDictionary<string, string>> SetResearchValueAsync(
        string itemId, string key, string? value)
    {
        var item = FindResearch(itemId)
            ?? throw new InvalidOperationException($"Unknown research item '{itemId}'.");
        item.Properties = Apply(item.Properties, ManuscriptPropertyScope.Research, key, value);
        // Research items are saved one at a time, and only through the service
        // that owns their file.
        if (_research != null) await _research.SaveAsync(item);
        return item.Properties ?? new Dictionary<string, string>();
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
        var sceneKeys = KeysFor(definitions, ManuscriptPropertyScope.Scene);
        var chapterKeys = KeysFor(definitions, ManuscriptPropertyScope.Chapter);

        // Plotlines live in the project file with the chapters, events in the
        // project settings, and research one file per item - so each kind is
        // pruned in place and only the files that changed are written.
        var plotlinesTouched = PruneEach(
            _projectService.ActiveBook?.Plotlines ?? [],
            p => p.Properties, (p, v) => p.Properties = v,
            KeysFor(definitions, ManuscriptPropertyScope.Plotline));

        if (PruneEach(
                _projectService.ProjectSettings?.Timeline?.ManualEvents ?? [],
                e => e.Properties, (e, v) => e.Properties = v,
                KeysFor(definitions, ManuscriptPropertyScope.Event)))
            await _projectService.SaveProjectSettingsAsync();

        if (_research != null)
        {
            var researchKeys = KeysFor(definitions, ManuscriptPropertyScope.Research);
            foreach (var item in _research.GetAll())
            {
                if (!PruneOne(item.Properties, researchKeys, out var pruned)) continue;
                item.Properties = pruned;
                await _research.SaveAsync(item);
            }
        }

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

        // Plotlines sit in the same file as the chapters, so one write covers both.
        var chaptersTouched = plotlinesTouched;
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

    private static HashSet<string> KeysFor(
        IReadOnlyList<ManuscriptPropertyDefinition> definitions, ManuscriptPropertyScope scope)
        => definitions.Where(d => d.Scope == scope).Select(d => d.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Drops values with no definition from every item in a list. Returns
    /// whether anything changed, so the caller only writes the file if it did.
    /// </summary>
    private static bool PruneEach<T>(
        IEnumerable<T> items,
        Func<T, Dictionary<string, string>?> read,
        Action<T, Dictionary<string, string>?> write,
        HashSet<string> keep)
    {
        var touched = false;
        foreach (var item in items)
        {
            if (!PruneOne(read(item), keep, out var pruned)) continue;
            write(item, pruned);
            touched = true;
        }
        return touched;
    }

    private static bool PruneOne(
        Dictionary<string, string>? values, HashSet<string> keep,
        out Dictionary<string, string>? pruned)
    {
        pruned = values;
        if (values == null) return false;

        var gone = values.Keys.Where(k => !keep.Contains(k)).ToList();
        if (gone.Count == 0) return false;

        foreach (var key in gone) values.Remove(key);
        pruned = values.Count > 0 ? values : null;
        return true;
    }

    private PlotlineData? FindPlotline(string id)
        => _projectService.ActiveBook?.Plotlines.FirstOrDefault(p => p.Id == id);

    private TimelineManualEvent? FindEvent(string id)
        => _projectService.ProjectSettings?.Timeline?.ManualEvents.FirstOrDefault(e => e.Id == id);

    private ResearchItem? FindResearch(string id)
        => _research?.GetAll().FirstOrDefault(i => i.Id == id);

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
