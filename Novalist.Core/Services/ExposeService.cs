using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Novalist.Core.Models;
using Novalist.Core.Utilities;

namespace Novalist.Core.Services;

/// <summary>
/// The exposé of the active book: its text, its length budget, and the counts
/// the editor validates against while the writer types.
/// </summary>
/// <param name="Html">Editor HTML of the exposé (empty when none written yet).</param>
/// <param name="CharLimit">Character budget, 0 when unset.</param>
/// <param name="PageLimit">Normseiten budget, 0 when unset.</param>
/// <param name="Characters">Characters of the rendered text, spaces included.</param>
/// <param name="Lines">Normseite grid lines used.</param>
/// <param name="Pages">Normseiten used.</param>
public sealed record ExposeState(
    string Html,
    int CharLimit,
    int PageLimit,
    int Characters,
    int Lines,
    int Pages);

/// <summary>
/// Reads, writes, measures and exports the per-book exposé. The text lives in
/// a single file at the book root so it travels with the book and is readable
/// outside Novalist; the limits live with the book's metadata.
/// </summary>
public sealed partial class ExposeService
{
    /// <summary>File name of the exposé document at the book root.</summary>
    public const string FileName = "Expose.novalist";

    private readonly IProjectService _projectService;

    public ExposeService(IProjectService projectService)
    {
        _projectService = projectService;
    }

    /// <summary>Full path of the active book's exposé file, or null with no book open.</summary>
    public string? GetExposePath()
    {
        var root = _projectService.ActiveBookRoot;
        return string.IsNullOrWhiteSpace(root) ? null : Path.Combine(root, FileName);
    }

    /// <summary>Loads the exposé, its limits, and its current counts.</summary>
    public async Task<ExposeState> GetAsync()
    {
        var path = GetExposePath();
        var html = path != null && File.Exists(path)
            ? await File.ReadAllTextAsync(path, Encoding.UTF8)
            : string.Empty;
        return Describe(html);
    }

    /// <summary>Writes the exposé and returns the counts for the saved text.</summary>
    public async Task<ExposeState> SaveAsync(string html)
    {
        var path = GetExposePath();
        if (path != null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, html ?? string.Empty, Encoding.UTF8);
        }
        return Describe(html ?? string.Empty);
    }

    /// <summary>Counts the given text without saving it — what the editor polls while typing.</summary>
    public ExposeState Measure(string html) => Describe(html ?? string.Empty);

    /// <summary>
    /// Stores the exposé's length budget on the book. Negative values clamp to
    /// 0, which means "no limit".
    /// </summary>
    public async Task<ExposeState> SetLimitsAsync(int charLimit, int pageLimit)
    {
        var book = _projectService.ActiveBook;
        if (book != null)
        {
            book.ExposeCharLimit = Math.Max(0, charLimit);
            book.ExposePageLimit = Math.Max(0, pageLimit);
            await _projectService.SaveProjectAsync();
        }
        return await GetAsync();
    }

    /// <summary>
    /// Exports the exposé as a Normseiten DOCX. The document carries its own
    /// title; <paramref name="title"/> only fills the running header. Returns
    /// false when no book is open or the exposé is still empty.
    /// </summary>
    public async Task<bool> ExportAsync(string outputPath, string title)
    {
        var state = await GetAsync();
        if (state.Characters == 0) return false;

        await ExportService.WriteNormseitenDocxAsync(
            BuildBlocks(state.Html),
            new ExportOptions
            {
                Format = ExportFormat.Docx,
                Title = title,
                PresetId = ExportPresets.NormseitenId
            },
            outputPath);
        return true;
    }

    /// <summary>
    /// Turns the exposé's editor HTML into Normseite blocks, one block per
    /// line of text.
    /// </summary>
    /// <remarks>
    /// An exposé is a line-oriented document, not prose: consecutive lines —
    /// the "Genre:" / "Schauplatz:" / "Erzählweise:" block at the top, for
    /// instance — belong on adjacent grid lines, and only an empty paragraph
    /// opens a blank line. That is the opposite of a manuscript scene, where
    /// every paragraph is followed by a blank, which is why this does not reuse
    /// <see cref="ExportService.HtmlToNormseitenBlocks"/>.
    ///
    /// The two paragraph styles map onto the two heading levels: a Heading
    /// paragraph is the document title (upper-cased, no blank line around it),
    /// a Subheading paragraph is a section heading (upper-cased, blank line
    /// before and after).
    /// </remarks>
    internal static List<NormseitenBlock> BuildBlocks(string? html)
    {
        var blocks = new List<NormseitenBlock>();
        if (string.IsNullOrWhiteSpace(html)) return blocks;

        var matches = ParagraphRegex().Matches(html);
        if (matches.Count == 0)
        {
            AppendLines(blocks, PlainText(html), NormseitenBlockKind.Text);
            return blocks;
        }

        foreach (Match match in matches)
        {
            var kind = ExtractStyleClass(match.Groups[1].Value) switch
            {
                "heading" => NormseitenBlockKind.Title,
                "subheading" => NormseitenBlockKind.Heading,
                _ => NormseitenBlockKind.Text
            };
            AppendLines(blocks, PlainText(match.Groups[2].Value), kind);
        }

        return blocks;
    }

    /// <summary>Adds one block per line, blank lines included.</summary>
    private static void AppendLines(List<NormseitenBlock> blocks, string text, NormseitenBlockKind kind)
    {
        foreach (var line in text.Split('\n'))
        {
            blocks.Add(string.IsNullOrWhiteSpace(line)
                ? NormseitenBlock.Blank()
                : new NormseitenBlock(kind, line));
        }
    }

    /// <summary>Strips inline markup, keeping explicit line breaks as newlines.</summary>
    private static string PlainText(string html)
        => WebUtility.HtmlDecode(TagRegex().Replace(LineBreakRegex().Replace(html, "\n"), string.Empty));

    private static string? ExtractStyleClass(string attributes)
    {
        var match = ClassRegex().Match(attributes);
        if (!match.Success) return null;
        foreach (var token in match.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            if (token.StartsWith("nv-style-", StringComparison.Ordinal))
                return token["nv-style-".Length..];
        return null;
    }

    private ExposeState Describe(string html)
    {
        var preset = ExportPresets.GetById(ExportPresets.NormseitenId);
        var lines = NormseitenRenderer.RenderLines(BuildBlocks(html), preset.GridColumns);
        var metrics = NormseitenRenderer.Measure(lines, preset.GridLines);
        var book = _projectService.ActiveBook;
        return new ExposeState(
            html,
            book?.ExposeCharLimit ?? 0,
            book?.ExposePageLimit ?? 0,
            metrics.Characters,
            metrics.Lines,
            metrics.Pages);
    }

    [GeneratedRegex(@"<p([^>]*)>(.*?)</p>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex ParagraphRegex();

    [GeneratedRegex(@"<br\s*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex LineBreakRegex();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"class=""([^""]*)""", RegexOptions.IgnoreCase)]
    private static partial Regex ClassRegex();
}
