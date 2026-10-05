using System.Text.RegularExpressions;
using System.Text;
using Novalist.Core.Models;
using XBrushes = PdfSharpCore.Drawing.XBrushes;
using XFont = PdfSharpCore.Drawing.XFont;
using XFontStyle = PdfSharpCore.Drawing.XFontStyle;
using XGraphics = PdfSharpCore.Drawing.XGraphics;
using XImage = PdfSharpCore.Drawing.XImage;
using XPoint = PdfSharpCore.Drawing.XPoint;
using XUnit = PdfSharpCore.Drawing.XUnit;
using static Novalist.Core.Services.ExportProse;

namespace Novalist.Core.Services;

public partial class ExportService
{
    // ─── Codex (markdown / PDF with images) ──────────────────────────

    /// <summary>Entity kind prefixes used by <see cref="ExportOptions.SelectedEntityKeys"/>.</summary>
    private const string CodexCharacterKind = "character";
    private const string CodexLocationKind = "location";
    private const string CodexItemKind = "item";
    private const string CodexLoreKind = "lore";

    /// <summary>The codex entities selected for export, already ordered by name.</summary>
    private sealed class CodexContent
    {
        public List<CharacterData> Characters { get; init; } = [];
        public List<LocationData> Locations { get; init; } = [];
        public List<ItemData> Items { get; init; } = [];
        public List<LoreData> Lore { get; init; } = [];
    }

    private async Task<CodexContent> CompileCodexAsync(ExportOptions options)
    {
        var keys = options.SelectedEntityKeys is null
            ? null
            : new HashSet<string>(options.SelectedEntityKeys, StringComparer.OrdinalIgnoreCase);

        bool Included(string kind, string id) => keys is null || keys.Contains($"{kind}:{id}");

        var byName = System.StringComparer.CurrentCultureIgnoreCase;
        var characters = await _entityService!.LoadCharactersAsync();
        var locations = await _entityService.LoadLocationsAsync();
        var items = await _entityService.LoadItemsAsync();
        var lore = await _entityService.LoadLoreAsync();

        return new CodexContent
        {
            Characters = characters.Where(c => Included(CodexCharacterKind, c.Id))
                .OrderBy(c => c.DisplayName, byName).ToList(),
            Locations = locations.Where(l => Included(CodexLocationKind, l.Id))
                .OrderBy(l => l.Name, byName).ToList(),
            Items = items.Where(i => Included(CodexItemKind, i.Id))
                .OrderBy(i => i.Name, byName).ToList(),
            Lore = lore.Where(l => Included(CodexLoreKind, l.Id))
                .OrderBy(l => l.Name, byName).ToList()
        };
    }

    /// <summary>
    /// Resolves a fixed codex label in the user's language, falling back to
    /// English when the caller supplied no translation for it.
    /// </summary>
    private static string Label(ExportOptions options, string key, string fallback)
        => options.Labels is not null && options.Labels.TryGetValue(key, out var text)
           && !string.IsNullOrWhiteSpace(text)
            ? text
            : fallback;

    /// <summary>Field rows rendered for a character, in display order, skipping empty values.</summary>
    private static IEnumerable<KeyValuePair<string, string>> CharacterFields(CharacterData c, ExportOptions options)
    {
        // In date age mode the Age field only restates the birth date, so it is
        // dropped rather than printed as a bare date.
        var age = string.Equals(c.AgeMode, "date", StringComparison.OrdinalIgnoreCase) ? string.Empty : c.Age;

        var fixedFields = new (string Key, string Fallback, string Value)[]
        {
            ("role", "Role", c.Role),
            ("age", "Age", age),
            ("gender", "Gender", c.Gender),
            ("group", "Group", c.Group),
            ("eyes", "Eyes", c.EyeColor),
            ("hair", "Hair", c.HairColor),
            ("height", "Height", c.Height),
            ("build", "Build", c.Build),
            ("skin", "Skin", c.SkinTone),
            ("notable", "Notable", c.DistinguishingFeatures)
        };

        foreach (var (key, fallback, value) in fixedFields)
            if (!string.IsNullOrWhiteSpace(value))
                yield return new KeyValuePair<string, string>(Label(options, key, fallback), value);

        if (c.CustomProperties is { Count: > 0 })
            foreach (var kv in c.CustomProperties)
                if (!string.IsNullOrWhiteSpace(kv.Value))
                    yield return kv;
    }

    /// <summary>Field rows rendered for a location / item / lore entry.</summary>
    private static IEnumerable<KeyValuePair<string, string>> GenericFields(
        string type, string description, Dictionary<string, string>? customProps, ExportOptions options)
    {
        if (!string.IsNullOrWhiteSpace(type))
            yield return new KeyValuePair<string, string>(Label(options, "type", "Type"), type);
        if (!string.IsNullOrWhiteSpace(description))
            yield return new KeyValuePair<string, string>(Label(options, "description", "Description"), description);
        if (customProps is { Count: > 0 })
            foreach (var kv in customProps)
                if (!string.IsNullOrWhiteSpace(kv.Value))
                    yield return kv;
    }

    public async Task ExportCodexAsync(ExportOptions options, string outputPath)
    {
        if (_entityService == null)
        {
            await File.WriteAllTextAsync(outputPath, "Codex export requires entity service.", Encoding.UTF8);
            return;
        }

        var outputDir = Path.GetDirectoryName(outputPath) ?? string.Empty;
        var baseName = Path.GetFileNameWithoutExtension(outputPath);
        var imagesFolderName = SanitizeFolderName(baseName) + "_images";
        var imagesAbsDir = Path.Combine(outputDir, imagesFolderName);
        Directory.CreateDirectory(imagesAbsDir);

        var copyMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? CopyImage(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath)) return null;
            if (copyMap.TryGetValue(relativePath, out var existing)) return existing;
            var abs = _entityService!.GetImageFullPath(relativePath);
            if (string.IsNullOrWhiteSpace(abs) || !File.Exists(abs)) return null;
            var fileName = Path.GetFileName(abs);
            var dest = Path.Combine(imagesAbsDir, fileName);
            int n = 1;
            while (File.Exists(dest) &&
                   !FilesEqual(abs, dest))
            {
                fileName = $"{Path.GetFileNameWithoutExtension(abs)}_{n}{Path.GetExtension(abs)}";
                dest = Path.Combine(imagesAbsDir, fileName);
                n++;
            }
            if (!File.Exists(dest)) File.Copy(abs, dest, overwrite: false);
            var rel = imagesFolderName + "/" + fileName;
            copyMap[relativePath] = rel;
            return rel;
        }

        var sb = new StringBuilder();
        sb.AppendLine($"# {options.Title ?? "Codex"}");
        if (!string.IsNullOrWhiteSpace(options.Author)) sb.AppendLine($"_by {options.Author}_");
        sb.AppendLine();

        var content = await CompileCodexAsync(options);

        if (content.Characters.Count > 0)
        {
            sb.AppendLine($"## {Label(options, "characters", "Characters")}");
            foreach (var c in content.Characters)
                AppendCharacter(sb, c, options, CopyImage);
        }

        if (content.Locations.Count > 0)
        {
            sb.AppendLine($"## {Label(options, "locations", "Locations")}");
            foreach (var l in content.Locations)
                AppendGenericEntity(sb, l.Name, l.Type, l.Description, l.Images, l.CustomProperties, l.Sections, options, CopyImage);
        }

        if (content.Items.Count > 0)
        {
            sb.AppendLine($"## {Label(options, "items", "Items")}");
            foreach (var it in content.Items)
                AppendGenericEntity(sb, it.Name, it.Type, it.Description, it.Images, it.CustomProperties, it.Sections, options, CopyImage);
        }

        if (content.Lore.Count > 0)
        {
            sb.AppendLine($"## {Label(options, "lore", "Lore")}");
            foreach (var lo in content.Lore)
                AppendGenericEntity(sb, lo.Name, lo.Category, lo.Description, lo.Images, lo.CustomProperties, lo.Sections, options, CopyImage);
        }

        await File.WriteAllTextAsync(outputPath, sb.ToString(), Encoding.UTF8);

        // Remove the images folder if nothing was copied (entities had no images).
        if (copyMap.Count == 0)
        {
            try { Directory.Delete(imagesAbsDir, recursive: false); } catch { }
        }
    }

    // The catch is a TOCTOU safety net: callers verify both files exist before
    // calling, so FileInfo.Length cannot realistically throw — excluded as the
    // catch line is not deterministically reachable.
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    private static bool FilesEqual(string a, string b)
    {
        try
        {
            var fa = new FileInfo(a);
            var fb = new FileInfo(b);
            return fa.Length == fb.Length;
        }
        catch { return false; }
    }

    private static string SanitizeFolderName(string s)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var arr = s.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        var clean = new string(arr).Trim();
        return string.IsNullOrEmpty(clean) ? "codex" : clean;
    }

    private static void AppendCharacter(StringBuilder sb, CharacterData c, ExportOptions options, Func<string, string?> copyImage)
    {
        sb.AppendLine($"### {c.DisplayName}");
        if (c.Images is { Count: > 0 } && options.IncludesPart("images"))
        {
            foreach (var img in c.Images)
            {
                if (string.IsNullOrWhiteSpace(img.Path)) continue;
                var rel = copyImage(img.Path);
                if (rel != null) sb.AppendLine($"![{img.Name}]({rel})");
            }
        }
        sb.AppendLine();
        if (options.IncludesPart("fields"))
            foreach (var field in CharacterFields(c, options))
                sb.AppendLine($"- **{field.Key}:** {field.Value}");

        if (c.Relationships is { Count: > 0 } && options.IncludesPart("relationships"))
        {
            sb.AppendLine();
            sb.AppendLine($"**{Label(options, "relationships", "Relationships")}**");
            foreach (var r in c.Relationships)
                sb.AppendLine($"- {r.Role}: {r.Target}");
        }

        if (c.Sections is { Count: > 0 })
        {
            foreach (var s in c.Sections)
            {
                if (string.IsNullOrWhiteSpace(s.Content) || !options.IncludesSection(s.Title)) continue;
                sb.AppendLine();
                sb.AppendLine($"**{s.Title}**");
                sb.AppendLine(StripHtml(s.Content));
            }
        }
        sb.AppendLine();
    }

    private static void AppendGenericEntity(StringBuilder sb, string name, string type, string description,
        List<EntityImage>? images, Dictionary<string, string>? customProps, List<EntitySection>? sections,
        ExportOptions options, Func<string, string?> copyImage)
    {
        sb.AppendLine($"### {name}");
        if (images is { Count: > 0 } && options.IncludesPart("images"))
            foreach (var img in images)
            {
                if (string.IsNullOrWhiteSpace(img.Path)) continue;
                var rel = copyImage(img.Path);
                if (rel != null) sb.AppendLine($"![{img.Name}]({rel})");
            }
        sb.AppendLine();
        if (options.IncludesPart("fields"))
            foreach (var field in GenericFields(type, description, customProps, options))
                sb.AppendLine($"- **{field.Key}:** {field.Value}");
        if (sections is { Count: > 0 })
        {
            foreach (var s in sections)
            {
                if (string.IsNullOrWhiteSpace(s.Content) || !options.IncludesSection(s.Title)) continue;
                sb.AppendLine();
                sb.AppendLine($"**{s.Title}**");
                sb.AppendLine(StripHtml(s.Content));
            }
        }
        sb.AppendLine();
    }

    /// <summary>One logical line of codex prose after markdown-ish parsing.</summary>
    internal sealed class CodexProseLine
    {
        public bool Heading { get; init; }
        public bool Bullet { get; init; }
        public List<InlineSegment> Segments { get; init; } = [];
    }

    /// <summary>
    /// Turns stored entity prose — editor HTML, markdown, or plain text — into
    /// lines a PDF page can lay out: block tags and newlines end a line, stray
    /// control characters are dropped (they render as boxes), and leading
    /// <c>#</c> / <c>*</c> markers plus <c>**bold**</c> spans become styling
    /// instead of literal text.
    /// </summary>
    internal static List<CodexProseLine> ParseCodexProse(string content)
    {
        var text = StripHtml(BlockTagRegex().Replace(content, "\n"))
            .Replace("\r\n", "\n")
            .Replace('\r', '\n');
        // Tabs and friends become spaces; every other control character is
        // dropped, since a PDF renders it as a box glyph.
        text = new string(text
            .Select(c => c != '\n' && char.IsWhiteSpace(c) ? ' ' : c)
            .Where(c => c == '\n' || !char.IsControl(c))
            .ToArray());

        var lines = new List<CodexProseLine>();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0)
            {
                lines.Add(new CodexProseLine());
                continue;
            }

            var heading = MarkdownHeadingRegex().IsMatch(line);
            if (heading) line = MarkdownHeadingRegex().Replace(line, string.Empty);
            var bullet = !heading && MarkdownBulletRegex().IsMatch(line);
            if (bullet) line = MarkdownBulletRegex().Replace(line, string.Empty);

            lines.Add(new CodexProseLine
            {
                Heading = heading,
                Bullet = bullet,
                Segments = ParseInlineMarkdown(line)
            });
        }
        return lines;
    }

    /// <summary>Splits a line into runs, toggling bold on <c>**</c> markers.</summary>
    internal static List<InlineSegment> ParseInlineMarkdown(string line)
    {
        var segments = new List<InlineSegment>();
        var buffer = new StringBuilder();
        var bold = false;

        for (var i = 0; i < line.Length; i++)
        {
            if (line[i] == '*' && i + 1 < line.Length && line[i + 1] == '*')
            {
                if (buffer.Length > 0)
                {
                    segments.Add(new InlineSegment { Text = buffer.ToString(), Bold = bold });
                    buffer.Clear();
                }
                bold = !bold;
                i++;
                continue;
            }
            buffer.Append(line[i]);
        }

        if (buffer.Length > 0)
            segments.Add(new InlineSegment { Text = buffer.ToString(), Bold = bold });
        return segments;
    }

    /// <summary>
    /// Splits a word too wide for a whole line (a long URL, or prose written
    /// without spaces) into chunks that fit.
    /// </summary>
    private static IEnumerable<string> HardBreak(string word, XFont font, XGraphics gfx, double maxWidth)
    {
        var start = 0;
        while (start < word.Length)
        {
            var take = 1;
            while (start + take < word.Length &&
                   gfx.MeasureString(word.Substring(start, take + 1), font).Width <= maxWidth)
                take++;
            yield return word.Substring(start, take);
            start += take;
        }
    }

    /// <summary>
    /// Codex export as a self-contained PDF: entity images are drawn into the
    /// document instead of being written next to it in a sidecar folder.
    /// </summary>
    public async Task ExportCodexPdfAsync(ExportOptions options, string outputPath)
    {
        if (_entityService == null)
        {
            await File.WriteAllTextAsync(outputPath, "Codex export requires entity service.", Encoding.UTF8);
            return;
        }

        var content = await CompileCodexAsync(options);

        string? ResolveImage(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath)) return null;
            var abs = _entityService!.GetImageFullPath(relativePath);
            return !string.IsNullOrWhiteSpace(abs) && File.Exists(abs) ? abs : null;
        }

        WriteCodexPdf(content, options, outputPath, ResolveImage);
    }

    private static void WriteCodexPdf(
        CodexContent content,
        ExportOptions options,
        string outputPath,
        Func<string, string?> resolveImage)
    {
        const string fontName = "Times New Roman";
        var doc = new PdfSharpCore.Pdf.PdfDocument();
        var docTitle = string.IsNullOrWhiteSpace(options.Title) ? "Codex" : options.Title;
        doc.Info.Title = docTitle;
        if (!string.IsNullOrWhiteSpace(options.Author))
            doc.Info.Author = options.Author;

        var pageWidth = XUnit.FromInch(8.5);
        var pageHeight = XUnit.FromInch(11);
        var margin = XUnit.FromInch(1);
        var textWidth = pageWidth - 2 * margin;
        var pageBottom = pageHeight - margin;
        var fieldIndent = (double)XUnit.FromInch(0.2);
        var bulletIndent = (double)XUnit.FromInch(0.18);
        var maxImageSide = (double)XUnit.FromInch(3);
        const double lineHeight = 15.0;

        var bodyFont = new XFont(fontName, 11);
        var labelFont = new XFont(fontName, 11, XFontStyle.Bold);
        var blockFont = new XFont(fontName, 12.5, XFontStyle.Bold);
        var entityFont = new XFont(fontName, 15, XFontStyle.Bold);
        var sectionFont = new XFont(fontName, 20, XFontStyle.Bold);

        XGraphics? gfx = null;
        PdfSharpCore.Pdf.PdfPage? currentPage = null;
        var y = 0.0;

        void NewPage()
        {
            gfx?.Dispose();
            var page = doc.AddPage();
            page.Width = pageWidth;
            page.Height = pageHeight;
            currentPage = page;
            gfx = XGraphics.FromPdfPage(page);
            y = margin + lineHeight;
        }

        void Ensure(double needed)
        {
            if (gfx == null || y + needed > pageBottom) NewPage();
        }

        // Lays out styled runs as one flowing line, wrapping at the right
        // margin and continuing at `indent` on every following line.
        void DrawRuns(IEnumerable<InlineSegment> runs, double indent, XFont regular, XFont strong)
        {
            Ensure(lineHeight);
            var left = margin + indent;
            var right = margin + textWidth;
            var x = left;

            void Place(string word, XFont font)
            {
                var width = gfx!.MeasureString(word, font).Width;
                if (x + width > right && x > left)
                {
                    y += lineHeight;
                    Ensure(lineHeight);
                    x = left;
                }
                gfx!.DrawString(word, font, XBrushes.Black, new XPoint(x, y));
                x += width;
            }

            // A space is drawn only where the source text had one, so a run
            // boundary ("**bold**" followed by ".") does not invent one.
            var pendingSpace = false;
            foreach (var run in runs)
            {
                var font = run.Bold ? strong : regular;
                var parts = run.Text.Split(' ');
                for (var j = 0; j < parts.Length; j++)
                {
                    if (j > 0) pendingSpace = true;
                    var word = parts[j];
                    if (word.Length == 0) continue;
                    if (pendingSpace && x > left) x += gfx!.MeasureString(" ", font).Width;
                    pendingSpace = false;
                    if (gfx!.MeasureString(word, font).Width > right - left)
                        foreach (var chunk in HardBreak(word, font, gfx, right - left)) Place(chunk, font);
                    else
                        Place(word, font);
                }
            }

            y += lineHeight;
        }

        void DrawHeading(string text, double indent, XFont font)
            => DrawRuns([new InlineSegment { Text = text }], indent, font, font);

        void DrawProse(string content, double indent)
        {
            foreach (var line in ParseCodexProse(content))
            {
                if (line.Segments.Count == 0)
                {
                    y += lineHeight * 0.4;   // blank line -> paragraph gap
                    continue;
                }

                var lineIndent = indent;
                if (line.Bullet)
                {
                    lineIndent += bulletIndent;
                    Ensure(lineHeight);
                    gfx!.DrawString("•", bodyFont, XBrushes.Black, new XPoint(margin + indent, y));
                }
                DrawRuns(line.Segments, lineIndent, line.Heading ? labelFont : bodyFont, labelFont);
            }
        }

        void DrawField(KeyValuePair<string, string> field)
        {
            var runs = new List<InlineSegment> { new() { Text = field.Key + ": ", Bold = true } };
            runs.AddRange(ParseInlineMarkdown(WhitespaceRunRegex().Replace(field.Value, " ").Trim()));
            DrawRuns(runs, fieldIndent, bodyFont, labelFont);
        }

        void DrawImage(string absolutePath)
        {
            XImage image;
            // Unreadable or unsupported image files are skipped rather than
            // failing the whole export.
            try { image = XImage.FromFile(absolutePath); }
            catch { return; }

            using (image)
            {
                var scale = Math.Min(1.0, Math.Min(maxImageSide / image.PointWidth, maxImageSide / image.PointHeight));
                var width = image.PointWidth * scale;
                var height = image.PointHeight * scale;
                Ensure(height + lineHeight);
                gfx!.DrawImage(image, margin + fieldIndent, y, width, height);
                y += height + lineHeight * 0.5;
            }
        }

        void DrawImages(List<EntityImage>? images)
        {
            if (images is not { Count: > 0 } || !options.IncludesPart("images")) return;
            foreach (var img in images)
            {
                if (string.IsNullOrWhiteSpace(img.Path)) continue;
                var abs = resolveImage(img.Path);
                if (abs != null) DrawImage(abs);
            }
        }

        void DrawSections(List<EntitySection>? sections)
        {
            if (sections is not { Count: > 0 }) return;
            foreach (var section in sections)
            {
                if (string.IsNullOrWhiteSpace(section.Content)
                    || !options.IncludesSection(section.Title)) continue;
                y += lineHeight * 0.5;
                DrawHeading(section.Title, fieldIndent, blockFont);
                DrawProse(section.Content, fieldIndent);
            }
        }

        // Every entry opens its own page so a reader can flip to one entry, and
        // gets a bookmark nested under its group in the PDF outline.
        void DrawEntity(
            PdfSharpCore.Pdf.PdfOutline group,
            bool first,
            string name,
            IEnumerable<KeyValuePair<string, string>> fields,
            List<EntityImage>? images,
            List<EntitySection>? sections,
            List<EntityRelationship>? relationships)
        {
            if (!first) NewPage();
            group.Outlines.Add(name, currentPage, false);

            DrawHeading(name, 0, entityFont);
            DrawImages(images);
            if (options.IncludesPart("fields"))
                foreach (var field in fields) DrawField(field);

            if (relationships is { Count: > 0 } && options.IncludesPart("relationships"))
            {
                y += lineHeight * 0.5;
                DrawHeading(Label(options, "relationships", "Relationships"), fieldIndent, blockFont);
                foreach (var rel in relationships)
                    DrawHeading($"{rel.Role}: {rel.Target}", fieldIndent * 2, bodyFont);
            }

            DrawSections(sections);
            y += lineHeight;
        }

        PdfSharpCore.Pdf.PdfOutline SectionHeading(string title)
        {
            NewPage();
            var outline = doc.Outlines.Add(title, currentPage, true);
            DrawHeading(title, 0, sectionFont);
            y += lineHeight;
            return outline;
        }

        if (options.IncludeTitlePage)
        {
            NewPage();
            var titleFont = new XFont(fontName, 26, XFontStyle.Bold);
            var titleWidth = gfx!.MeasureString(docTitle, titleFont).Width;
            gfx.DrawString(docTitle, titleFont, XBrushes.Black,
                new XPoint((pageWidth - titleWidth) / 2, pageHeight * 0.45));

            if (!string.IsNullOrWhiteSpace(options.Author))
            {
                var authorFont = new XFont(fontName, 14, XFontStyle.Italic);
                var authorWidth = gfx.MeasureString(options.Author, authorFont).Width;
                gfx.DrawString(options.Author, authorFont, XBrushes.Black,
                    new XPoint((pageWidth - authorWidth) / 2, pageHeight * 0.45 + 30));
            }
        }

        if (content.Characters.Count > 0)
        {
            var group = SectionHeading(Label(options, "characters", "Characters"));
            for (var i = 0; i < content.Characters.Count; i++)
            {
                var c = content.Characters[i];
                DrawEntity(group, i == 0, c.DisplayName, CharacterFields(c, options),
                    c.Images, c.Sections, c.Relationships);
            }
        }

        if (content.Locations.Count > 0)
        {
            var group = SectionHeading(Label(options, "locations", "Locations"));
            for (var i = 0; i < content.Locations.Count; i++)
            {
                var l = content.Locations[i];
                DrawEntity(group, i == 0, l.Name, GenericFields(l.Type, l.Description, l.CustomProperties, options),
                    l.Images, l.Sections, l.Relationships);
            }
        }

        if (content.Items.Count > 0)
        {
            var group = SectionHeading(Label(options, "items", "Items"));
            for (var i = 0; i < content.Items.Count; i++)
            {
                var it = content.Items[i];
                DrawEntity(group, i == 0, it.Name, GenericFields(it.Type, it.Description, it.CustomProperties, options),
                    it.Images, it.Sections, it.Relationships);
            }
        }

        if (content.Lore.Count > 0)
        {
            var group = SectionHeading(Label(options, "lore", "Lore"));
            for (var i = 0; i < content.Lore.Count; i++)
            {
                var lo = content.Lore[i];
                DrawEntity(group, i == 0, lo.Name, GenericFields(lo.Category, lo.Description, lo.CustomProperties, options),
                    lo.Images, lo.Sections, lo.Relationships);
            }
        }

        // An empty selection still has to produce a readable file.
        if (doc.PageCount == 0) NewPage();
        gfx!.Dispose();
        doc.Save(outputPath);
    }

    [GeneratedRegex(@"<br\s*/?>|</(?:p|div|li|tr|h[1-6])\s*>", RegexOptions.IgnoreCase)]
    private static partial Regex BlockTagRegex();

    [GeneratedRegex(@"^#{1,6}\s+")]
    private static partial Regex MarkdownHeadingRegex();

    [GeneratedRegex(@"^[-*+]\s+")]
    private static partial Regex MarkdownBulletRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRunRegex();
}
