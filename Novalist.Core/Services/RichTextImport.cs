using System.Net;
using System.Text;

namespace Novalist.Core.Services;

/// <summary>The semantic inline formatting recovered from an imported file.</summary>
public sealed record ImportedTextRun(
    string Text,
    bool Bold = false,
    bool Italic = false,
    bool Underline = false,
    bool Strike = false,
    bool Superscript = false,
    bool Subscript = false);

/// <summary>A list an imported paragraph belongs to.</summary>
public enum ImportedListKind
{
    None,
    Ordered,
    Unordered
}

/// <summary>Alignment that carries meaning without importing source-page geometry.</summary>
public enum ImportedTextAlignment
{
    Default,
    Left,
    Center,
    Right,
    Justify
}

/// <summary>Named paragraph styles Novalist's editor and exporters understand.</summary>
public enum ImportedParagraphStyle
{
    Normal,
    Heading,
    Subheading,
    BlockQuote,
    Poetry
}

/// <summary>
/// Safe renderers for the structured rich-text model. Source text is always
/// encoded here; parsers never get to manufacture arbitrary HTML or Markdown.
/// </summary>
internal static class ImportedRichText
{
    public static string ToHtml(IEnumerable<ImportedParagraph> source)
    {
        var paragraphs = source.Where(HasContent).ToList();
        var html = new StringBuilder();
        ImportedListKind openList = ImportedListKind.None;

        foreach (var paragraph in paragraphs)
        {
            if (paragraph.IsSceneBreak)
            {
                CloseList();
                html.Append("<p>***</p>");
                continue;
            }

            if (paragraph.ListKind != openList)
            {
                CloseList();
                if (paragraph.ListKind != ImportedListKind.None)
                {
                    openList = paragraph.ListKind;
                    html.Append(openList == ImportedListKind.Ordered ? "<ol>" : "<ul>");
                }
            }

            if (openList != ImportedListKind.None)
            {
                html.Append("<li>");
                AppendInlineHtml(html, paragraph);
                html.Append("</li>");
                continue;
            }

            html.Append("<p");
            var cssClass = paragraph.Style switch
            {
                ImportedParagraphStyle.Heading => "nv-style-heading",
                ImportedParagraphStyle.Subheading => "nv-style-subheading",
                ImportedParagraphStyle.BlockQuote => "nv-style-blockquote",
                ImportedParagraphStyle.Poetry => "nv-style-poetry",
                _ => string.Empty
            };
            if (cssClass.Length > 0)
                html.Append(" class=\"").Append(cssClass).Append('"');

            var alignment = paragraph.Alignment switch
            {
                ImportedTextAlignment.Center => "center",
                ImportedTextAlignment.Right => "right",
                ImportedTextAlignment.Justify => "justify",
                _ => string.Empty
            };
            if (alignment.Length > 0)
                html.Append(" style=\"text-align:").Append(alignment).Append("\"");

            html.Append('>');
            AppendInlineHtml(html, paragraph);
            html.Append("</p>");
        }

        CloseList();
        return html.ToString();

        void CloseList()
        {
            if (openList == ImportedListKind.None) return;
            html.Append(openList == ImportedListKind.Ordered ? "</ol>" : "</ul>");
            openList = ImportedListKind.None;
        }
    }

    public static string ToPlainText(IEnumerable<ImportedParagraph> paragraphs)
        => string.Join("\n\n", paragraphs
            .Where(p => !p.IsSceneBreak && p.Text.Length > 0)
            .Select(p => p.Text)).Trim();

    public static string ToMarkdown(IEnumerable<ImportedParagraph> source)
    {
        var blocks = new List<string>();
        foreach (var paragraph in source.Where(HasContent))
        {
            if (paragraph.IsSceneBreak)
            {
                blocks.Add("***");
                continue;
            }

            var inline = InlineMarkdown(paragraph);
            if (paragraph.ListKind == ImportedListKind.Ordered)
                blocks.Add("1. " + inline);
            else if (paragraph.ListKind == ImportedListKind.Unordered)
                blocks.Add("- " + inline);
            else if (paragraph.Style == ImportedParagraphStyle.Heading || paragraph.HeadingLevel == 1)
                blocks.Add("# " + inline);
            else if (paragraph.Style == ImportedParagraphStyle.Subheading || paragraph.HeadingLevel > 1)
                blocks.Add(new string('#', Math.Clamp(paragraph.HeadingLevel, 2, 6)) + " " + inline);
            else if (paragraph.Style == ImportedParagraphStyle.BlockQuote)
                blocks.Add("> " + inline);
            else
                blocks.Add(inline);
        }

        return string.Join("\n\n", blocks).Trim();
    }

    private static bool HasContent(ImportedParagraph paragraph)
        => paragraph.IsSceneBreak || paragraph.Text.Length > 0;

    private static void AppendInlineHtml(StringBuilder html, ImportedParagraph paragraph)
    {
        var runs = paragraph.Runs.Count > 0
            ? paragraph.Runs
            : [new ImportedTextRun(paragraph.Text)];

        foreach (var run in runs)
        {
            if (run.Text.Length == 0) continue;

            var styles = new List<string>(4);
            if (run.Bold) styles.Add("font-weight:bold");
            if (run.Italic) styles.Add("font-style:italic");
            if (run.Underline || run.Strike)
            {
                var decoration = run.Underline && run.Strike
                    ? "underline line-through"
                    : run.Underline ? "underline" : "line-through";
                styles.Add("text-decoration:" + decoration);
            }

            if (styles.Count > 0) html.Append("<span style=\"").AppendJoin(';', styles).Append("\">");
            if (run.Superscript) html.Append("<sup>");
            if (run.Subscript) html.Append("<sub>");

            html.Append(WebUtility.HtmlEncode(run.Text)
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n')
                .Replace("\n", "<br>", StringComparison.Ordinal)
                .Replace("\t", "    ", StringComparison.Ordinal));

            if (run.Subscript) html.Append("</sub>");
            if (run.Superscript) html.Append("</sup>");
            if (styles.Count > 0) html.Append("</span>");
        }
    }

    private static string InlineMarkdown(ImportedParagraph paragraph)
    {
        var markdown = new StringBuilder();
        var runs = paragraph.Runs.Count > 0
            ? paragraph.Runs
            : [new ImportedTextRun(paragraph.Text)];

        foreach (var run in runs)
        {
            var leadingLength = run.Text.Length - run.Text.TrimStart().Length;
            var trailingLength = run.Text.Length - run.Text.TrimEnd().Length;
            var coreLength = Math.Max(0, run.Text.Length - leadingLength - trailingLength);
            if (coreLength == 0)
            {
                markdown.Append(run.Text);
                continue;
            }
            var leading = run.Text[..leadingLength];
            var core = run.Text.Substring(leadingLength, coreLength);
            var trailing = trailingLength > 0 ? run.Text[^trailingLength..] : string.Empty;
            var text = EscapeMarkdown(core);
            if (text.Length > 0)
            {
                if (run.Superscript) text = "<sup>" + text + "</sup>";
                if (run.Subscript) text = "<sub>" + text + "</sub>";
                if (run.Underline) text = "<u>" + text + "</u>";
                if (run.Strike) text = "~~" + text + "~~";
                if (run.Bold && run.Italic) text = "***" + text + "***";
                else if (run.Bold) text = "**" + text + "**";
                else if (run.Italic) text = "*" + text + "*";
            }
            markdown.Append(leading).Append(text).Append(trailing);
        }

        return markdown.ToString();
    }

    private static string EscapeMarkdown(string text)
    {
        var escaped = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (c is '\\' or '*' or '_' or '[' or ']' or '<' or '>' or '`') escaped.Append('\\');
            escaped.Append(c);
        }

        return escaped.ToString();
    }
}
