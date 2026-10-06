using System.Diagnostics;
using Novalist.Backend.Extensions;
using Novalist.Core.Models;
using Novalist.Core.Services;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

public sealed partial class ManuscriptImportRpc
{
    /// <summary>The path's existence shape, never the path or its name.</summary>
    private static string SourceShape(string path)
        => Directory.Exists(path) ? "directory" : File.Exists(path) ? "file" : "missing";

    /// <summary>
    /// A known input extension is useful when a Linux file picker supplies the
    /// binder file instead of its package folder. Unknown extensions are
    /// collapsed so a writer-created suffix cannot become diagnostic content.
    /// </summary>
    private static string SourceExtension(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension == ScrivenerReader.BinderExtension
            || extension == ScrivenerReader.ProjectExtension
            || ManuscriptReader.SupportedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            return extension;
        return extension.Length == 0 ? "none" : "other";
    }

    /// <summary>The writer's choices, by binder key. Null when they have made
    /// none, which is what leaves the rules in charge.</summary>
    private static Dictionary<string, ScrivenerDestination>? MappingFrom(ImportMappingDto[]? mapping)
    {
        if (mapping == null || mapping.Length == 0) return null;

        var chosen = new Dictionary<string, ScrivenerDestination>(StringComparer.Ordinal);
        foreach (var row in mapping)
        {
            if (string.IsNullOrWhiteSpace(row.Key)) continue;
            if (DestinationFrom(row.Destination) is { } destination) chosen[row.Key] = destination;
        }

        return chosen.Count > 0 ? chosen : null;
    }

    /// <summary>A destination name from the dialog. Unknown names are ignored
    /// rather than throwing, so a stale renderer degrades to the rules.</summary>
    private static ScrivenerDestination? DestinationFrom(string name)
        => name?.Trim().ToLowerInvariant() switch
        {
            "manuscript" => ScrivenerDestination.Manuscript,
            "draft" => ScrivenerDestination.Draft,
            "book" => ScrivenerDestination.Book,
            "characters" => ScrivenerDestination.Characters,
            "places" => ScrivenerDestination.Places,
            "research" => ScrivenerDestination.Research,
            "skip" => ScrivenerDestination.Skip,
            _ => null
        };

    private static string NameOf(ScrivenerDestination destination)
        => destination switch
        {
            ScrivenerDestination.Manuscript => "manuscript",
            ScrivenerDestination.Draft => "draft",
            ScrivenerDestination.Book => "book",
            ScrivenerDestination.Characters => "characters",
            ScrivenerDestination.Places => "places",
            ScrivenerDestination.Skip => "skip",
            _ => "research"
        };

    /// <summary>
    /// The Scrivener binder as parts, chapters and scenes, plus the Codex
    /// entries and research it will create and what it will leave behind.
    /// </summary>
    private static ImportPlanDto ScrivenerPlan(
        ScrivenerProject project,
        IReadOnlyList<ScrivenerBinderRow> outline,
        IReadOnlyDictionary<string, ScrivenerDestination>? chosen)
    {
        var chapters = GroupChapters(project)
            .Select(g => new ImportChapterDto(
                g.Title,
                g.PartTitle,
                [.. g.Scenes.Select(sc => new ImportSceneDto(sc.Title, WordsIn(sc.Text)))]))
            .ToArray();

        var targets = GroupTargets(project)
            .Select(t => new ImportTargetDto(
                t.Kind switch
                {
                    ScrivenerTargetKind.Draft => "draft",
                    ScrivenerTargetKind.Book => "book",
                    _ => "manuscript"
                },
                t.Title,
                t.Chapters.Count,
                t.Chapters.Sum(c => c.Scenes.Count),
                t.Chapters.Sum(c => c.Scenes.Sum(sc => WordsIn(sc.Text)))))
            .ToArray();

        // The rows carry where the import is actually going to send them, so the
        // dialog never has to reconcile two ideas of the same row.
        var rows = outline
            .Select(r => new ImportMappingRowDto(
                r.Key,
                r.Title,
                r.Depth,
                NameOf(chosen != null && chosen.TryGetValue(r.Key, out var pick)
                    ? pick
                    : r.Destination),
                r.Documents,
                r.HasChildren))
            .ToArray();

        return new ImportPlanDto(
            project.Version.Length > 0 ? $"scrivener{project.Version}" : string.Empty,
            chapters.Length,
            project.Scenes.Count,
            project.Scenes.Sum(sc => WordsIn(sc.Text)),
            chapters,
            [.. project.Losses],
            chapters.Select(c => c.PartTitle).Where(p => p.Length > 0).Distinct(StringComparer.Ordinal).Count(),
            project.Entities.Count(e => e.Kind == ScrivenerEntityKind.Character),
            project.Entities.Count(e => e.Kind == ScrivenerEntityKind.Location),
            project.Research.Count,
            rows,
            targets);
    }

    /// <summary>
    /// The draft's documents in binder order, grouped by the chapter folder
    /// they sat in.
    ///
    /// Grouped on the folder's binder identity rather than its title: the stock
    /// Scrivener novel template names every chapter "Chapter", so grouping by
    /// title turned a four-chapter book into one chapter of four scenes.
    /// </summary>
    private static List<ScrivenerChapterGroup> GroupChapters(ScrivenerProject project)
        => [.. GroupTargets(project).SelectMany(t => t.Chapters)];

    /// <summary>
    /// The draft's documents grouped by the book or draft they were sent to, and
    /// by their chapter folder inside it.
    ///
    /// One pass rather than two so binder order survives both groupings: the
    /// targets come out in the order their folders appear in the binder, and so
    /// do the chapters within each.
    /// </summary>
    private static List<ScrivenerTargetGroup> GroupTargets(ScrivenerProject project)
    {
        var order = new List<string>();
        var targets = new Dictionary<string, ScrivenerTargetGroup>(StringComparer.Ordinal);
        var chapters = new Dictionary<string, ScrivenerChapterGroup>(StringComparer.Ordinal);

        foreach (var scene in project.Scenes)
        {
            var targetKey = $"{scene.TargetKind}|{scene.TargetKey}";
            if (!targets.TryGetValue(targetKey, out var target))
            {
                target = new ScrivenerTargetGroup(scene.TargetKind, scene.TargetKey, scene.TargetTitle);
                targets[targetKey] = target;
                order.Add(targetKey);
            }

            // Scoped by target as well as by chapter. Chapter keys are binder
            // identities and so belong to one target - except the chapter loose
            // documents land in, which is not a binder item at all. Keyed on the
            // chapter alone, every draft that held loose documents shared one
            // chapter with the first draft that had any, and was created empty.
            var chapterKey = $"{targetKey}|{scene.ChapterKey}";
            if (!chapters.TryGetValue(chapterKey, out var chapter))
            {
                chapter = new ScrivenerChapterGroup(
                    scene.ChapterKey, scene.ChapterTitle, scene.PartKey, scene.PartTitle);
                chapters[chapterKey] = chapter;
                target.Chapters.Add(chapter);
            }

            chapter.Scenes.Add(scene);
        }

        return [.. order.Select(k => targets[k])];
    }

    private sealed record ScrivenerChapterGroup(
        string Key, string Title, string PartKey, string PartTitle)
    {
        public List<ScrivenerScene> Scenes { get; } = [];
    }

    private sealed record ScrivenerTargetGroup(
        ScrivenerTargetKind Kind, string Key, string Title)
    {
        public List<ScrivenerChapterGroup> Chapters { get; } = [];
    }

    private static int WordsIn(string text) => Workspace.CountWords(text);
}
