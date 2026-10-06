using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Sdk.Models;

namespace Novalist.Backend;

public sealed partial class Workspace
{
    public ProjectStateDto BuildState()
    {
        var project = Projects.CurrentProject;
        var book = Projects.ActiveBook;
        if (project == null || book == null)
        {
            return new ProjectStateDto(false, null, null, null, Array.Empty<BookDto>(), Array.Empty<ChapterDto>());
        }

        var chapters = book.Chapters
            .OrderBy(c => c.Order)
            .Select(c => new ChapterDto(
                c.Guid,
                c.Title,
                c.Order,
                c.Status.ToString(),
                c.Act,
                c.IsFavorite,
                c.Subtitle,
                c.HideHeading,
                c.Description,
                c.SectionTypeKey,
                ScenesOf(c.Guid)))
            .ToArray();

        return new ProjectStateDto(
            true,
            project.Name,
            Projects.ProjectRoot,
            book.Id,
            project.Books.Select(b => new BookDto(b.Id, b.Name)).ToArray(),
            chapters);
    }

    /// <summary>
    /// The colour a scene's label paints it, or the bare colour a project
    /// saved before labels had names still carries. A label key whose label is
    /// gone paints nothing, which reads as no label rather than as a mistake.
    /// </summary>
    private string? ResolveLabelColor(Core.Models.SceneData scene)
    {
        if (scene.LabelKey == null) return scene.LabelColor;
        return (Projects.ActiveBook?.SceneLabels ?? [])
            .FirstOrDefault(l => string.Equals(l.Key, scene.LabelKey, StringComparison.OrdinalIgnoreCase))
            ?.Color;
    }

    /// <summary>
    /// The colours of a scene's threads, in the book's plotline order.
    ///
    /// A plotline has carried a colour since the Plot Grid shipped and it never
    /// left that view, so the binder could not show that this scene and that one
    /// are the same thread.
    /// </summary>
    /// <summary>
    /// The threads this scene serves, by id, in the book's plotline order.
    /// Colours alone cannot drive a filter: two threads can share one, and a
    /// writer picks a thread by its name.
    /// </summary>
    private IReadOnlyList<string> ResolvePlotlineIds(SceneData scene)
    {
        var ids = scene.PlotlineIds;
        if (ids == null || ids.Count == 0) return [];

        return [.. (Projects.ActiveBook?.Plotlines ?? [])
            .OrderBy(p => p.Order)
            .Where(p => ids.Contains(p.Id, StringComparer.Ordinal))
            .Select(p => p.Id)];
    }

    private IReadOnlyList<string> ResolvePlotlineColors(SceneData scene)
    {
        var ids = scene.PlotlineIds;
        if (ids == null || ids.Count == 0) return [];

        return [.. (Projects.ActiveBook?.Plotlines ?? [])
            .OrderBy(p => p.Order)
            .Where(p => ids.Contains(p.Id, StringComparer.Ordinal))
            .Select(p => p.Color)];
    }

    private SceneDto[] ScenesOf(string chapterGuid)
    {
        var manifest = Projects.ScenesManifest;
        if (manifest == null || !manifest.Chapters.TryGetValue(chapterGuid, out var scenes))
        {
            return Array.Empty<SceneDto>();
        }
        return scenes
            .Where(s => s.ArchivedAt == null)
            .OrderBy(s => s.Order)
            .Select(s => new SceneDto(
                s.Id, s.Title, s.Order, s.WordCount, ResolveLabelColor(s), s.IsFavorite, s.Synopsis,
                s.Stage, s.ExcludeFromExport, s.Inactive, ResolvePlotlineColors(s),
                ResolvePlotlineIds(s)))
            .ToArray();
    }
}
