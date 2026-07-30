using Novalist.Core.Models;

namespace Novalist.Core.Services;

/// <summary>
/// Gathers the book into the shape another tool can read.
///
/// The writers in <c>DataExport.cs</c> - the CSV quoting, the scene sheet, the
/// JSON document - were all built and nothing ever filled a
/// <see cref="MetadataExport"/> to hand them, so no part of the app could
/// produce one. This is the missing half: everything the project knows about a
/// scene or an entry, flattened, with ids resolved to the names a spreadsheet
/// reader expects to see.
/// </summary>
public sealed class MetadataCollector
{
    private readonly IProjectService _projects;
    private readonly IEntityService _entities;
    private readonly IPlotlineService? _plotlines;

    public MetadataCollector(
        IProjectService projects, IEntityService entities, IPlotlineService? plotlines = null)
    {
        _projects = projects;
        _entities = entities;
        _plotlines = plotlines;
    }

    public async Task<MetadataExport> CollectAsync()
    {
        var export = new MetadataExport
        {
            Title = _projects.ActiveBook?.Name ?? string.Empty,
            Author = _projects.ProjectSettings.Author
        };

        // Ids mean nothing in a spreadsheet, so both are resolved to names here
        // rather than left for whoever opens the file.
        var entityNames = await EntityNamesAsync();
        var plotlineNames = (_plotlines?.GetPlotlines() ?? [])
            .ToDictionary(p => p.Id, p => p.Name, StringComparer.Ordinal);

        foreach (var chapter in _projects.GetChaptersOrdered())
            foreach (var scene in _projects.GetScenesForChapter(chapter.Guid))
                export.Scenes.Add(SceneRow(chapter, scene, entityNames, plotlineNames));

        export.Codex.AddRange(await CodexRowsAsync());
        return export;
    }

    private static SceneMetadataRow SceneRow(
        ChapterData chapter, SceneData scene,
        IReadOnlyDictionary<string, string> entityNames,
        IReadOnlyDictionary<string, string> plotlineNames)
        => new()
        {
            Chapter = chapter.Title,
            ChapterOrder = chapter.Order,
            Scene = scene.Title,
            SceneOrder = scene.Order,
            Stage = scene.Stage ?? string.Empty,
            Pov = scene.AnalysisOverrides?.Pov ?? string.Empty,
            Words = scene.WordCount,
            WordTarget = scene.WordTarget ?? 0,
            Date = scene.Date,
            Synopsis = scene.Synopsis ?? string.Empty,
            Goal = scene.Goal ?? string.Empty,
            Conflict = scene.AnalysisOverrides?.Conflict ?? string.Empty,
            Outcome = scene.Outcome ?? string.Empty,
            Tags = Join(scene.AnalysisOverrides?.Tags),
            Plotlines = Join((scene.PlotlineIds ?? []).Select(id => Name(plotlineNames, id))),
            Cast = Join((scene.Cast ?? []).Select(id => Name(entityNames, id))),
            Inactive = scene.Inactive,
            ExcludedFromExport = scene.ExcludeFromExport
        };

    /// <summary>
    /// An entry deleted from the Codex leaves its id behind on the scenes that
    /// referenced it. Writing the id is better than writing nothing: it says
    /// something is there and is no longer resolvable.
    /// </summary>
    private static string Name(IReadOnlyDictionary<string, string> names, string id)
        => names.TryGetValue(id, out var name) && !string.IsNullOrWhiteSpace(name) ? name : id;

    private static string Join(IEnumerable<string>? values)
        => values == null ? string.Empty : string.Join(", ", values.Where(v => !string.IsNullOrWhiteSpace(v)));

    private async Task<Dictionary<string, string>> EntityNamesAsync()
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var c in await _entities.LoadCharactersAsync()) names[c.Id] = c.DisplayName;
        foreach (var l in await _entities.LoadLocationsAsync()) names[l.Id] = l.Name;
        foreach (var i in await _entities.LoadItemsAsync()) names[i.Id] = i.Name;
        foreach (var l in await _entities.LoadLoreAsync()) names[l.Id] = l.Name;
        foreach (var type in _entities.GetCustomEntityTypes())
            foreach (var e in await _entities.LoadCustomEntitiesAsync(type.TypeKey))
                names[e.Id] = e.Name;
        return names;
    }

    private async Task<List<EntityMetadataRow>> CodexRowsAsync()
    {
        var rows = new List<EntityMetadataRow>();

        foreach (var c in await _entities.LoadCharactersAsync())
        {
            var row = Row("Character", c.DisplayName, c.CustomProperties, c.Sections, c.Relationships);
            Put(row, "Role", c.Role);
            Put(row, "Age", c.Age);
            Put(row, "Gender", c.Gender);
            Put(row, "Group", c.Group);
            Put(row, "Eyes", c.EyeColor);
            Put(row, "Hair", c.HairColor);
            Put(row, "Height", c.Height);
            Put(row, "Build", c.Build);
            Put(row, "Skin", c.SkinTone);
            Put(row, "Notable", c.DistinguishingFeatures);
            rows.Add(row);
        }

        foreach (var l in await _entities.LoadLocationsAsync())
            rows.Add(Generic("Location", l.Name, l.Type, l.Description, l.CustomProperties, l.Sections, l.Relationships));
        foreach (var i in await _entities.LoadItemsAsync())
            rows.Add(Generic("Item", i.Name, i.Type, i.Description, i.CustomProperties, i.Sections, i.Relationships));
        foreach (var l in await _entities.LoadLoreAsync())
            // Lore names its kind "Category" rather than "Type"; the column says what
            // the entry itself calls it.
            rows.Add(Generic("Lore", l.Name, l.Category, l.Description, l.CustomProperties, l.Sections, l.Relationships,
                typeLabel: "Category"));

        // The writer's own types are entries like any other, and a file that
        // silently dropped them would be wrong rather than short.
        foreach (var type in _entities.GetCustomEntityTypes())
            foreach (var e in await _entities.LoadCustomEntitiesAsync(type.TypeKey))
                rows.Add(Generic(
                    type.DisplayName, e.Name, string.Empty, string.Empty,
                    e.CustomProperties, e.Sections, e.Relationships));

        return rows;
    }

    private static EntityMetadataRow Generic(
        string kind, string name, string type, string description,
        Dictionary<string, string>? properties, List<EntitySection>? sections,
        List<EntityRelationship>? relationships, string typeLabel = "Type")
    {
        var row = Row(kind, name, properties, sections, relationships);
        Put(row, typeLabel, type);
        Put(row, "Description", description);
        return row;
    }

    private static EntityMetadataRow Row(
        string kind, string name, Dictionary<string, string>? properties,
        List<EntitySection>? sections, List<EntityRelationship>? relationships)
    {
        var row = new EntityMetadataRow { Kind = kind, Name = name };

        if (properties != null)
            foreach (var kv in properties)
                Put(row, kv.Key, kv.Value);

        if (sections != null)
            foreach (var section in sections)
                if (!string.IsNullOrWhiteSpace(section.Content))
                    row.Sections[section.Title] = section.Content;

        if (relationships != null)
            row.Relationships.AddRange(relationships
                .Where(r => !string.IsNullOrWhiteSpace(r.Role) || !string.IsNullOrWhiteSpace(r.Target))
                .Select(r => $"{r.Role}: {r.Target}".Trim()));

        return row;
    }

    /// <summary>An empty field is left out rather than written as a blank.</summary>
    private static void Put(EntityMetadataRow row, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(label) && !string.IsNullOrWhiteSpace(value))
            row.Properties[label] = value;
    }
}
