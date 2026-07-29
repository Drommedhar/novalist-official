using Novalist.Core.Models;

namespace Novalist.Core.Services;

/// <summary>
/// The project's tags, across everything that carries one.
///
/// Scenes, Codex entries and research notes each kept their own list and none
/// of them could be counted, coloured, renamed or merged. A tag is only useful
/// if it is the same tag everywhere, which means one place that knows what
/// exists and can change it in every holder at once.
/// </summary>
public sealed class TagService
{
    private readonly IProjectService _projectService;
    private readonly IEntityService _entityService;

    public TagService(IProjectService projectService, IEntityService entityService)
    {
        _projectService = projectService;
        _entityService = entityService;
    }

    private ProjectMetadata Project =>
        _projectService.CurrentProject
        ?? throw new InvalidOperationException("No project loaded.");

    /// <summary>
    /// Every tag in use or in the vocabulary, with its colour and how many of
    /// each kind of thing carries it. Sorted by name, because the manager is
    /// read as a list of names rather than a chart of counts.
    /// </summary>
    public async Task<IReadOnlyList<TagUsage>> ListAsync()
    {
        var scenes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var entities = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var research = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        static void Count(Dictionary<string, int> into, IEnumerable<string>? tags)
        {
            foreach (var tag in Clean(tags))
                into[tag] = into.TryGetValue(tag, out var n) ? n + 1 : 1;
        }

        foreach (var scene in AllScenes()) Count(scenes, scene.AnalysisOverrides?.Tags);
        foreach (var item in Project.ResearchItems) Count(research, item.Tags);
        foreach (var entity in await AllEntitiesAsync()) Count(entities, entity.Tags);

        var colors = Project.Tags.ToDictionary(
            t => t.Name, t => t.Color, StringComparer.OrdinalIgnoreCase);

        var names = scenes.Keys
            .Concat(entities.Keys)
            .Concat(research.Keys)
            .Concat(colors.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase);

        return [.. names.Select(name => new TagUsage(
            name,
            colors.TryGetValue(name, out var color) ? color : string.Empty,
            scenes.GetValueOrDefault(name),
            entities.GetValueOrDefault(name),
            research.GetValueOrDefault(name)))];
    }

    /// <summary>
    /// Gives a tag a colour. The tag need not be in use yet - colouring one
    /// ahead of using it is how a vocabulary gets planned.
    /// </summary>
    public async Task SetColorAsync(string name, string color)
    {
        var clean = (name ?? string.Empty).Trim();
        if (clean.Length == 0) return;

        var existing = Project.Tags.FirstOrDefault(
            t => string.Equals(t.Name, clean, StringComparison.OrdinalIgnoreCase));
        if (existing == null)
        {
            Project.Tags.Add(new ProjectTag { Name = clean, Color = color ?? string.Empty });
        }
        else
        {
            existing.Color = color ?? string.Empty;
        }
        await _projectService.SaveProjectAsync();
    }

    /// <summary>
    /// Renames a tag everywhere it is used. Renaming onto a tag that already
    /// exists is a merge - the two become one, and nothing carries it twice.
    /// Returns how many things changed.
    /// </summary>
    public async Task<int> RenameAsync(string from, string to)
    {
        var source = (from ?? string.Empty).Trim();
        var target = (to ?? string.Empty).Trim();
        if (source.Length == 0 || target.Length == 0) return 0;
        if (string.Equals(source, target, StringComparison.OrdinalIgnoreCase)) return 0;

        var changed = await RewriteAsync(source, target);

        // The vocabulary follows: the old entry goes, and the new one keeps
        // whatever colour it already had rather than inheriting the old one.
        var old = Project.Tags.FirstOrDefault(
            t => string.Equals(t.Name, source, StringComparison.OrdinalIgnoreCase));
        if (old != null)
        {
            var existing = Project.Tags.FirstOrDefault(
                t => string.Equals(t.Name, target, StringComparison.OrdinalIgnoreCase));
            if (existing == null) old.Name = target;
            else Project.Tags.Remove(old);
        }

        await _projectService.SaveProjectAsync();
        return changed;
    }

    /// <summary>Removes a tag from everything that carries it. Returns the count.</summary>
    public async Task<int> DeleteAsync(string name)
    {
        var clean = (name ?? string.Empty).Trim();
        if (clean.Length == 0) return 0;

        var changed = await RewriteAsync(clean, null);
        Project.Tags.RemoveAll(
            t => string.Equals(t.Name, clean, StringComparison.OrdinalIgnoreCase));
        await _projectService.SaveProjectAsync();
        return changed;
    }

    /// <summary>
    /// Replaces one tag with another everywhere, or removes it when the target
    /// is null. One walk, so a rename and a delete cannot drift apart.
    /// </summary>
    private async Task<int> RewriteAsync(string source, string? target)
    {
        var changed = 0;

        foreach (var scene in AllScenes())
        {
            var tags = scene.AnalysisOverrides?.Tags;
            if (tags == null || !Has(tags, source)) continue;
            scene.AnalysisOverrides!.Tags = Replace(tags, source, target);
            changed++;
        }
        if (changed > 0) await _projectService.SaveScenesAsync();

        var researchChanged = false;
        foreach (var item in Project.ResearchItems)
        {
            if (!Has(item.Tags, source)) continue;
            item.Tags = Replace(item.Tags, source, target);
            researchChanged = true;
            changed++;
        }
        if (researchChanged) await _projectService.SaveProjectAsync();

        changed += await RewriteEntitiesAsync(source, target);
        return changed;
    }

    /// <summary>
    /// Rewrites the tag on every Codex entry. Each kind is walked with its own
    /// save method rather than through a shared one with a fallback arm: an
    /// entry kind nobody wrote a save for would silently keep the old tag.
    /// </summary>
    private async Task<int> RewriteEntitiesAsync(string source, string? target)
    {
        var changed = 0;
        changed += await RewriteEachAsync(
            await _entityService.LoadCharactersAsync(), _entityService.SaveCharacterAsync, source, target);
        changed += await RewriteEachAsync(
            await _entityService.LoadLocationsAsync(), _entityService.SaveLocationAsync, source, target);
        changed += await RewriteEachAsync(
            await _entityService.LoadItemsAsync(), _entityService.SaveItemAsync, source, target);
        changed += await RewriteEachAsync(
            await _entityService.LoadLoreAsync(), _entityService.SaveLoreAsync, source, target);
        foreach (var type in _entityService.GetCustomEntityTypes())
        {
            changed += await RewriteEachAsync(
                await _entityService.LoadCustomEntitiesAsync(type.TypeKey),
                _entityService.SaveCustomEntityAsync, source, target);
        }
        return changed;
    }

    private static async Task<int> RewriteEachAsync<T>(
        List<T> entities, Func<T, Task> save, string source, string? target)
        where T : IEntityData
    {
        var changed = 0;
        foreach (var entity in entities)
        {
            if (!Has(entity.Tags, source)) continue;
            entity.Tags = Replace(entity.Tags, source, target);
            await save(entity);
            changed++;
        }
        return changed;
    }

    private static bool Has(IEnumerable<string>? tags, string name)
        => tags != null && tags.Any(t => string.Equals(t?.Trim(), name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The list with <paramref name="source"/> replaced or removed, deduped so
    /// a merge onto a tag something already had does not leave it twice.
    /// </summary>
    private static List<string> Replace(List<string> tags, string source, string? target)
    {
        var result = new List<string>();
        foreach (var tag in tags)
        {
            var value = string.Equals(tag?.Trim(), source, StringComparison.OrdinalIgnoreCase)
                ? target
                : tag;
            if (string.IsNullOrWhiteSpace(value)) continue;
            if (result.Any(t => string.Equals(t, value, StringComparison.OrdinalIgnoreCase))) continue;
            result.Add(value);
        }
        return result;
    }

    private static IEnumerable<string> Clean(IEnumerable<string>? tags)
        => (tags ?? []).Select(t => (t ?? string.Empty).Trim()).Where(t => t.Length > 0);

    private IEnumerable<SceneData> AllScenes()
        => _projectService.GetChaptersOrdered()
            .SelectMany(chapter => _projectService.GetScenesForChapter(chapter.Guid));

    private async Task<List<IEntityData>> AllEntitiesAsync()
    {
        var all = new List<IEntityData>();
        all.AddRange(await _entityService.LoadCharactersAsync());
        all.AddRange(await _entityService.LoadLocationsAsync());
        all.AddRange(await _entityService.LoadItemsAsync());
        all.AddRange(await _entityService.LoadLoreAsync());
        foreach (var type in _entityService.GetCustomEntityTypes())
            all.AddRange(await _entityService.LoadCustomEntitiesAsync(type.TypeKey));
        return all;
    }
}
