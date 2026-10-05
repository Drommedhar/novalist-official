using System.Text;
using Novalist.Core.Models;

namespace Novalist.Core.Services;

public partial class ExportService
{
    // ─── Machine-readable metadata (CSV / JSON) ──────────────────────

    /// <summary>
    /// Scene metadata, and for JSON the Codex too, in a format another tool can
    /// read. Every other export is prose or a document: an outline could not be
    /// pulled into a spreadsheet without retyping it.
    ///
    /// This deliberately does not compile the manuscript. A compile drops the
    /// scenes that stay out of the book, and those are exactly the rows a
    /// planning sheet needs - so parked and held-back scenes come through as
    /// rows carrying a flag rather than as absences.
    /// </summary>
    public async Task ExportDataAsync(ExportOptions options, string outputPath)
    {
        var export = await CompileMetadataAsync(options);
        var text = options.Format switch
        {
            ExportFormat.Csv => MetadataWriter.SceneCsv(export.Scenes),
            ExportFormat.CodexCsv => MetadataWriter.CodexCsv(export.Codex),
            ExportFormat.Opml => MetadataWriter.Opml(export),
            ExportFormat.WorldJson or ExportFormat.WorldHtml => await WorldTextAsync(options, export),
            _ => MetadataWriter.Json(export)
        };
        // A byte-order mark, and only here: Excel reads a plain UTF-8 CSV as
        // the local codepage and turns every accent into mojibake. JSON is read
        // by parsers that specify UTF-8, and a BOM breaks some of them.
        var encoding = options.Format is ExportFormat.Csv or ExportFormat.CodexCsv
            ? new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)
            : new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        await File.WriteAllTextAsync(outputPath, text, encoding);
    }

    /// <summary>
    /// The whole project, as JSON or as one browsable page. The scenes and the
    /// Codex come from the same compile every other data export uses, so two
    /// exports of the same book cannot come to disagree.
    /// </summary>
    private async Task<string> WorldTextAsync(ExportOptions options, MetadataExport export)
    {
        var project = _projectService.CurrentProject;
        var open = _projectService.ActiveBook;
        var archive = WorldArchive.Build(export, project, open);

        // Every other book of the project, read without opening it. A document
        // named for the project that carried one book of a trilogy was
        // two-thirds missing and said nothing about the fact.
        foreach (var other in project?.Books ?? [])
        {
            if (open != null && other.Id == open.Id) continue;
            WorldArchive.AddVolume(archive, other, await VolumeScenesAsync(other));
        }

        return options.Format == ExportFormat.WorldHtml
            ? WorldArchive.Html(archive)
            : WorldArchive.Json(archive);
    }

    /// <summary>
    /// One closed book's outline. Titles, order and synopsis only: the prose
    /// belongs to a manuscript export, and reading every scene of every book to
    /// build an outline would make the archive cost what a compile costs.
    /// </summary>
    private async Task<List<SceneMetadataRow>> VolumeScenesAsync(BookData book)
    {
        var manifest = await _projectService.LoadScenesManifestForAsync(book);
        if (manifest == null) return [];

        var rows = new List<SceneMetadataRow>();
        foreach (var chapter in book.Chapters.OrderBy(c => c.Order))
        {
            if (!manifest.Chapters.TryGetValue(chapter.Guid, out var scenes)) continue;
            foreach (var scene in scenes.Where(s => s.ArchivedAt == null).OrderBy(s => s.Order))
            {
                rows.Add(new SceneMetadataRow
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
                    Outcome = scene.Outcome ?? string.Empty,
                    Inactive = scene.Inactive,
                    ExcludedFromExport = scene.ExcludeFromExport
                });
            }
        }
        return rows;
    }

    /// <summary>
    /// A document compiled out of what the writer already recorded.
    ///
    /// Reads the plan rather than compiling the book, for the same reason the
    /// metadata export does: a report that hides the scenes somebody set aside
    /// cannot answer why the act is short.
    /// </summary>
    public async Task ExportReportAsync(ExportOptions options, string outputPath)
    {
        var scenes = ReportScenes(options);
        var title = string.IsNullOrWhiteSpace(options.Title) ? "Report" : options.Title;
        var text = options.Format == ExportFormat.PovReport
            ? ReportBuilder.PovBreakdown(scenes, title)
            : ReportBuilder.Synopsis(scenes, title);
        await File.WriteAllTextAsync(outputPath, text, new UTF8Encoding(false));
    }

    /// <summary>The scenes a report covers, in reading order.</summary>
    private List<ReportScene> ReportScenes(ExportOptions options)
    {
        var scenes = new List<ReportScene>();
        var number = 0;
        foreach (var chapter in _projectService.GetChaptersOrdered()
                     .Where(c => options.SelectedChapterGuids.Contains(c.Guid))
                     .OrderBy(c => c.Order))
        {
            number++;
            foreach (var scene in _projectService.GetScenesForChapter(chapter.Guid)
                         .Where(s => options.IncludedStages == null
                             || options.IncludedStages.Count == 0
                             || options.IncludedStages.Contains(s.Stage ?? string.Empty)))
            {
                scenes.Add(new ReportScene
                {
                    Chapter = chapter.Title,
                    ChapterNumber = number,
                    Title = scene.Title,
                    Synopsis = scene.Synopsis ?? string.Empty,
                    Pov = scene.AnalysisOverrides?.Pov ?? string.Empty,
                    Words = scene.WordCount
                });
            }
        }
        return scenes;
    }

    /// <summary>The metadata behind both machine-readable formats.</summary>
    internal async Task<MetadataExport> CompileMetadataAsync(ExportOptions options)
    {
        var export = new MetadataExport
        {
            Title = options.Title ?? string.Empty,
            Author = options.Author ?? string.Empty
        };

        var plotlineNames = (_projectService.ActiveBook?.Plotlines ?? [])
            .ToDictionary(p => p.Id, p => p.Name, StringComparer.OrdinalIgnoreCase);
        var entityNames = await EntityNamesByIdAsync();

        foreach (var chapter in _projectService.GetChaptersOrdered()
                     .Where(c => options.SelectedChapterGuids.Contains(c.Guid))
                     .OrderBy(c => c.Order))
        {
            foreach (var scene in _projectService.GetScenesForChapter(chapter.Guid)
                         // A stage filter is something the writer asked for; the
                         // in-the-book filters are not, and belong in a column.
                         .Where(s => options.IncludedStages == null
                             || options.IncludedStages.Count == 0
                             || options.IncludedStages.Contains(s.Stage ?? string.Empty)))
            {
                var overrides = scene.AnalysisOverrides;
                export.Scenes.Add(new SceneMetadataRow
                {
                    Chapter = chapter.Title,
                    ChapterOrder = chapter.Order,
                    Scene = scene.Title,
                    SceneOrder = scene.Order,
                    Stage = scene.Stage ?? string.Empty,
                    Pov = overrides?.Pov ?? string.Empty,
                    Words = scene.WordCount,
                    WordTarget = scene.WordTarget ?? 0,
                    Date = scene.Date,
                    Synopsis = scene.Synopsis ?? string.Empty,
                    Goal = scene.Goal ?? string.Empty,
                    Conflict = overrides?.Conflict ?? string.Empty,
                    Outcome = scene.Outcome ?? string.Empty,
                    Tags = Join(overrides?.Tags),
                    // Names rather than ids: a spreadsheet a person reads is
                    // the whole point, and a column of GUIDs is not readable.
                    Plotlines = Join((scene.PlotlineIds ?? []).Select(
                        id => plotlineNames.TryGetValue(id, out var name) ? name : id)),
                    Cast = Join((scene.Cast ?? []).Select(
                        id => entityNames.TryGetValue(id, out var name) ? name : id)),
                    Inactive = scene.Inactive,
                    ExcludedFromExport = scene.ExcludeFromExport
                });
            }
        }

        // The scene sheet has no room for it: one sheet cannot hold a scene list
        // and a character list without one of them being wrong. The Codex sheet
        // is the other half, and JSON carries both.
        if (options.Format is ExportFormat.Json or ExportFormat.CodexCsv
                or ExportFormat.WorldJson or ExportFormat.WorldHtml
            && _entityService != null)
        {
            var codex = await CompileCodexAsync(options);
            // An entry the writer marked as not for readers does not appear at
            // all. Listing the name and withholding the fields would announce
            // that there is something to find, which is most of the spoiler.
            bool Shown(Models.IEntityData e) => !options.ForReaders || !e.ReaderHidden;

            // The same field builders the Markdown and PDF codex exports use,
            // so a JSON entry carries exactly what the document one prints.
            foreach (var c in codex.Characters.Where(Shown))
                export.Codex.Add(EntityRow(CodexCharacterKind, c.DisplayName,
                    CharacterFields(c, options), c.Sections, c.Relationships, options));
            foreach (var l in codex.Locations.Where(Shown))
                export.Codex.Add(EntityRow(CodexLocationKind, l.Name,
                    GenericFields(l.Type, l.Description, l.CustomProperties, options),
                    l.Sections, l.Relationships, options));
            foreach (var i in codex.Items.Where(Shown))
                export.Codex.Add(EntityRow(CodexItemKind, i.Name,
                    GenericFields(i.Type, i.Description, i.CustomProperties, options),
                    i.Sections, i.Relationships, options));
            foreach (var l in codex.Lore.Where(Shown))
                export.Codex.Add(EntityRow(CodexLoreKind, l.Name,
                    GenericFields(l.Category, l.Description, l.CustomProperties, options),
                    l.Sections, l.Relationships, options));
        }

        return export;
    }

    /// <summary>Every Codex name a scene can point at, keyed by id.</summary>
    private async Task<Dictionary<string, string>> EntityNamesByIdAsync()
    {
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (_entityService == null) return names;
        foreach (var c in await _entityService.LoadCharactersAsync()) names[c.Id] = c.DisplayName;
        foreach (var l in await _entityService.LoadLocationsAsync()) names[l.Id] = l.Name;
        foreach (var i in await _entityService.LoadItemsAsync()) names[i.Id] = i.Name;
        foreach (var l in await _entityService.LoadLoreAsync()) names[l.Id] = l.Name;
        return names;
    }

    private static EntityMetadataRow EntityRow(
        string kind, string name,
        IEnumerable<KeyValuePair<string, string>> fields, List<Models.EntitySection>? sections,
        List<Models.EntityRelationship>? relationships, ExportOptions options)
    {
        var row = new EntityMetadataRow { Kind = kind, Name = name };

        // The same part switches the Markdown and PDF codex exports honour, so
        // "names and nothing else" means the same thing in every format.
        if (options.IncludesPart("fields"))
            foreach (var kv in fields)
                row.Properties[kv.Key] = kv.Value;

        if (sections != null)
            foreach (var section in sections
                .Where(s => options.IncludesSection(s.Title))
                // A section the writer marked as not for readers is left out
                // whole: half of a twist is still the twist.
                .Where(s => !options.ForReaders || !s.ReaderHidden))
                row.Sections[section.Title] = section.Content;

        if (options.IncludesPart("relationships") && relationships != null)
            foreach (var rel in relationships)
                row.Relationships.Add(string.IsNullOrWhiteSpace(rel.Role)
                    ? rel.Target
                    : $"{rel.Role}: {rel.Target}");

        return row;
    }

    /// <summary>A list in one cell, the way a reader of a sheet expects it.</summary>
    private static string Join(IEnumerable<string>? values)
        => values == null ? string.Empty : string.Join("; ", values.Where(v => !string.IsNullOrWhiteSpace(v)));

    /// <summary>
    /// Export the project's timeline as a chronological outline (Markdown).
    /// Groups events by their linked chapter when present; otherwise lists them
    /// under "Unscheduled events" in <see cref="TimelineManualEvent.Order"/>.
    /// </summary>
    public async Task ExportTimelineOutlineAsync(string outputPath)
    {
        var timeline = _projectService.ProjectSettings?.Timeline ?? new TimelineData();
        var chapters = _projectService.GetChaptersOrdered().ToList();
        var categories = timeline.Categories.ToDictionary(c => c.Id, c => c.Name, StringComparer.OrdinalIgnoreCase);

        var sb = new StringBuilder();
        sb.AppendLine("# Story Outline");
        sb.AppendLine();

        var eventsByChapter = timeline.ManualEvents
            .GroupBy(ev => string.IsNullOrWhiteSpace(ev.LinkedChapterGuid) ? string.Empty : ev.LinkedChapterGuid)
            .ToDictionary(g => g.Key, g => g.OrderBy(e => e.Order).ToList());

        foreach (var chapter in chapters)
        {
            sb.Append("## ").Append(chapter.Order).Append(". ").AppendLine(chapter.Title);
            if (!string.IsNullOrWhiteSpace(chapter.Act))
                sb.Append("_Act: ").Append(chapter.Act).AppendLine("_");
            if (!string.IsNullOrWhiteSpace(chapter.Date))
                sb.Append("_Date: ").Append(chapter.Date).AppendLine("_");

            if (eventsByChapter.TryGetValue(chapter.Guid, out var chapterEvents))
            {
                foreach (var ev in chapterEvents)
                    AppendEvent(sb, ev, categories);
            }

            var scenes = _projectService.GetScenesForChapter(chapter.Guid);
            foreach (var scene in scenes)
            {
                sb.Append("- **").Append(scene.Title).Append("**");
                if (!string.IsNullOrWhiteSpace(scene.Date))
                    sb.Append(" — ").Append(scene.Date);
                if (!string.IsNullOrWhiteSpace(scene.Synopsis))
                    sb.Append(" — ").Append(scene.Synopsis.Replace('\n', ' '));
                sb.AppendLine();
            }
            sb.AppendLine();
        }

        if (eventsByChapter.TryGetValue(string.Empty, out var unscheduled) && unscheduled.Count > 0)
        {
            sb.AppendLine("## Unscheduled events");
            foreach (var ev in unscheduled)
                AppendEvent(sb, ev, categories);
            sb.AppendLine();
        }

        await File.WriteAllTextAsync(outputPath, sb.ToString(), Encoding.UTF8);
    }

    private static void AppendEvent(StringBuilder sb, TimelineManualEvent ev, IReadOnlyDictionary<string, string> categories)
    {
        sb.Append("- ");
        if (!string.IsNullOrWhiteSpace(ev.Date)) sb.Append('[').Append(ev.Date).Append("] ");
        sb.Append(ev.Title);
        if (!string.IsNullOrWhiteSpace(ev.CategoryId) && categories.TryGetValue(ev.CategoryId, out var cat))
            sb.Append(" _(").Append(cat).Append(")_");
        if (!string.IsNullOrWhiteSpace(ev.Description))
            sb.Append(" — ").Append(ev.Description.Replace('\n', ' '));
        sb.AppendLine();
    }
}
