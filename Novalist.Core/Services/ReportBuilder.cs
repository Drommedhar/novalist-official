using System.Globalization;
using System.Text;

namespace Novalist.Core.Services;

/// <summary>One scene, as a report needs to see it.</summary>
public sealed class ReportScene
{
    public string Chapter { get; init; } = string.Empty;
    public int ChapterNumber { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Synopsis { get; init; } = string.Empty;
    public string Pov { get; init; } = string.Empty;
    public int Words { get; init; }
}

/// <summary>
/// Documents compiled out of what the writer already recorded.
///
/// Every scene carries a synopsis and a POV, and neither could be read as a
/// whole: the synopsis of a book existed only as forty separate boxes nobody
/// could put side by side, and "how much of this book is in Mira's head" could
/// not be answered at all.
/// </summary>
public static class ReportBuilder
{
    /// <summary>What a synopsis report is called when the writer named nothing.</summary>
    public const string UntitledPov = "—";

    /// <summary>
    /// Every scene's synopsis under its chapter, in reading order.
    ///
    /// A scene with no synopsis is named and left blank rather than skipped:
    /// the gaps are the reason to read this, and a document that quietly omits
    /// them reads as a finished outline.
    /// </summary>
    public static string Synopsis(IEnumerable<ReportScene> scenes, string title)
    {
        var sb = new StringBuilder();
        sb.Append("# ").Append(title).Append("\n\n");

        var chapter = string.Empty;
        var any = false;
        foreach (var scene in scenes)
        {
            any = true;
            if (!string.Equals(chapter, scene.Chapter, StringComparison.Ordinal))
            {
                chapter = scene.Chapter;
                sb.Append("\n## ").Append(chapter).Append("\n\n");
            }

            sb.Append("**").Append(scene.Title).Append("**");
            if (scene.Words > 0)
                sb.Append("  \n_").Append(Words(scene.Words)).Append('_');
            sb.Append("\n\n");
            sb.Append(string.IsNullOrWhiteSpace(scene.Synopsis)
                ? "_No synopsis yet._"
                : scene.Synopsis.Trim());
            sb.Append("\n\n");
        }

        if (!any) sb.Append("_Nothing to report yet._\n");
        return sb.ToString();
    }

    /// <summary>
    /// How the book divides between points of view, in words and in per cent.
    ///
    /// Scenes with no POV are their own row rather than being dropped. A
    /// breakdown that silently excludes a fifth of the book is worse than one
    /// that says a fifth of the book has no POV recorded.
    /// </summary>
    public static string PovBreakdown(IEnumerable<ReportScene> scenes, string title)
    {
        var list = scenes.ToList();
        var total = list.Sum(s => s.Words);

        var sb = new StringBuilder();
        sb.Append("# ").Append(title).Append("\n\n");

        if (list.Count == 0)
        {
            sb.Append("_Nothing to report yet._\n");
            return sb.ToString();
        }

        sb.Append("| Point of view | Scenes | Words | Share |\n");
        sb.Append("| --- | ---: | ---: | ---: |\n");

        var groups = list
            .GroupBy(s => string.IsNullOrWhiteSpace(s.Pov) ? UntitledPov : s.Pov.Trim(),
                StringComparer.CurrentCultureIgnoreCase)
            .Select(g => (Pov: g.Key, Scenes: g.Count(), Words: g.Sum(s => s.Words)))
            // Largest first: the question this answers is whose book it is.
            .OrderByDescending(g => g.Words)
            .ThenBy(g => g.Pov, StringComparer.CurrentCultureIgnoreCase);

        foreach (var group in groups)
        {
            sb.Append("| ").Append(group.Pov)
              .Append(" | ").Append(group.Scenes.ToString(CultureInfo.InvariantCulture))
              .Append(" | ").Append(group.Words.ToString("N0", CultureInfo.CurrentCulture))
              .Append(" | ").Append(Share(group.Words, total))
              .Append(" |\n");
        }

        sb.Append("| **Total** | **")
          .Append(list.Count.ToString(CultureInfo.InvariantCulture))
          .Append("** | **").Append(total.ToString("N0", CultureInfo.CurrentCulture))
          .Append("** | |\n");
        return sb.ToString();
    }

    private static string Words(int words)
        => words.ToString("N0", CultureInfo.CurrentCulture) + " words";

    /// <summary>
    /// A share of the whole. A book of nothing but empty scenes divides by
    /// zero otherwise, and an outline is exactly where that happens.
    /// </summary>
    private static string Share(int words, int total)
        => total == 0
            ? "—"
            : (words * 100.0 / total).ToString("0.#", CultureInfo.CurrentCulture) + "%";
}
