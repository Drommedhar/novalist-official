using System.Xml.Linq;
using System.Text.RegularExpressions;

namespace Novalist.Core.Services;

public static partial class ScrivenerReader
{
    private sealed record ScrivenerStyleInfo(
        string Id,
        string Name,
        bool AppliesToCharacters,
        bool Bold,
        bool Italic,
        bool Underline,
        bool Strike,
        int HeadingLevel,
        ImportedParagraphStyle ParagraphStyle,
        ImportedTextAlignment Alignment);

    /// <summary>
    /// Scrivener writes named-style boundaries into the visible RTF stream as
    /// &lt;$Scr_Ps::N&gt;, &lt;$Scr_Cs::N&gt; and &lt;$Scr_H::N&gt;. The N indexes
    /// the document's content.styles list, whose UUIDs resolve through
    /// Files/styles.xml. Interpret those markers and remove them from prose.
    /// </summary>
    private static class ScrivenerFormatting
    {
        private static readonly Regex Marker = new(
            @"<(?<close>!)?\$Scr_(?<kind>Ps|Cs|H)::(?<index>\d+)>",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public static IReadOnlyList<ImportedParagraph> Apply(
            IReadOnlyList<ImportedParagraph> source,
            IReadOnlyDictionary<string, ScrivenerStyleInfo> catalog,
            IReadOnlyList<string> styleIds)
        {
            var state = new FormattingState(catalog, styleIds);
            return source.Select(state.ApplyParagraph).OfType<ImportedParagraph>().ToArray();
        }

        private static IReadOnlyList<ImportedTextRun> MergeRuns(List<ImportedTextRun> source)
        {
            var merged = new List<ImportedTextRun>();
            foreach (var run in source.Where(r => r.Text.Length > 0))
            {
                if (merged.Count > 0 && SameStyle(merged[^1], run))
                    merged[^1] = merged[^1] with { Text = merged[^1].Text + run.Text };
                else
                    merged.Add(run);
            }

            return merged;
        }

        private static bool SameStyle(ImportedTextRun left, ImportedTextRun right)
            => left.Bold == right.Bold && left.Italic == right.Italic
               && left.Underline == right.Underline && left.Strike == right.Strike
               && left.Superscript == right.Superscript && left.Subscript == right.Subscript;

        private static void TrimRuns(List<ImportedTextRun> runs)
        {
            while (runs.Count > 0)
            {
                var text = runs[0].Text.TrimStart();
                if (text.Length == 0) runs.RemoveAt(0);
                else
                {
                    runs[0] = runs[0] with { Text = text };
                    break;
                }
            }

            while (runs.Count > 0)
            {
                var text = runs[^1].Text.TrimEnd();
                if (text.Length == 0) runs.RemoveAt(runs.Count - 1);
                else
                {
                    runs[^1] = runs[^1] with { Text = text };
                    break;
                }
            }
        }

        private sealed class FormattingState
        {
            private readonly IReadOnlyDictionary<string, ScrivenerStyleInfo> catalog;
            private readonly IReadOnlyList<string> styleIds;
            private int? activeParagraphStyle;
            private int? activeCharacterStyle;
            private int activeHeading;
            private int? usedParagraphStyle;
            private int usedHeading;
            private List<ImportedTextRun> runs = [];

            public FormattingState(IReadOnlyDictionary<string, ScrivenerStyleInfo> catalog, IReadOnlyList<string> styleIds)
            {
                this.catalog = catalog;
                this.styleIds = styleIds;
            }

            public ImportedParagraph? ApplyParagraph(ImportedParagraph paragraph)
            {
                runs = [];
                usedParagraphStyle = null;
                usedHeading = 0;

                foreach (var run in paragraph.Runs.Count > 0
                             ? paragraph.Runs
                             : [new ImportedTextRun(paragraph.Text)])
                {
                    var offset = 0;
                    foreach (Match match in Marker.Matches(run.Text))
                    {
                        AddText(run.Text[offset..match.Index], run);
                        ApplyMarker(match);
                        offset = match.Index + match.Length;
                    }

                    AddText(run.Text[offset..], run);
                }

                TrimRuns(runs);
                var text = string.Concat(runs.Select(r => r.Text));
                if (paragraph.IsSceneBreak)
                {
                    return paragraph;
                }
                if (text.Length == 0) return null;

                var named = StyleAt(usedParagraphStyle);
                var heading = Math.Max(paragraph.HeadingLevel,
                    Math.Max(usedHeading, named?.HeadingLevel ?? 0));
                var paragraphStyle = heading switch
                {
                    1 => ImportedParagraphStyle.Heading,
                    > 1 => ImportedParagraphStyle.Subheading,
                    _ => named?.ParagraphStyle ?? paragraph.Style
                };

                return new ImportedParagraph
                {
                    Text = text,
                    Runs = MergeRuns(runs),
                    HeadingLevel = heading,
                    IsSceneBreak = paragraph.IsSceneBreak,
                    ListKind = paragraph.ListKind,
                    ListLevel = paragraph.ListLevel,
                    Alignment = paragraph.Alignment != ImportedTextAlignment.Default
                        ? paragraph.Alignment
                        : named?.Alignment ?? ImportedTextAlignment.Default,
                    Style = paragraphStyle
                };
            }

            private void AddText(string value, ImportedTextRun original)
            {
                if (value.Length == 0) return;
                if (value.Any(c => !char.IsWhiteSpace(c)))
                {
                    usedParagraphStyle ??= activeParagraphStyle;
                    if (activeHeading > 0) usedHeading = Math.Max(usedHeading, activeHeading);
                }

                var paragraphNamed = StyleAt(activeParagraphStyle);
                var characterNamed = StyleAt(activeCharacterStyle);
                runs.Add(original with
                {
                    Text = value,
                    Bold = original.Bold || paragraphNamed?.Bold == true || characterNamed?.Bold == true,
                    Italic = original.Italic || paragraphNamed?.Italic == true || characterNamed?.Italic == true,
                    Underline = original.Underline || paragraphNamed?.Underline == true
                        || characterNamed?.Underline == true,
                    Strike = original.Strike || paragraphNamed?.Strike == true || characterNamed?.Strike == true
                });
            }

            private void ApplyMarker(Match match)
            {
                var closing = match.Groups["close"].Success;
                var kind = match.Groups["kind"].Value;
                _ = int.TryParse(match.Groups["index"].Value, out var index);
                switch (kind)
                {
                    case "Ps":
                        activeParagraphStyle = closing ? null : index;
                        break;
                    case "Cs":
                        activeCharacterStyle = closing ? null : index;
                        break;
                    case "H":
                        activeHeading = closing ? 0 : Math.Clamp(index, 1, 6);
                        break;
                }
            }

            private ScrivenerStyleInfo? StyleAt(int? index)
            {
                if (index is null || index < 0 || index >= styleIds.Count) return null;
                return catalog.TryGetValue(styleIds[index.Value], out var value) ? value : null;
            }
        }
    }
}
