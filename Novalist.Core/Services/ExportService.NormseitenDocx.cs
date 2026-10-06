using System.IO.Compression;
using System.Text;
using Novalist.Core.Models;
using Novalist.Core.Utilities;
using static Novalist.Core.Services.ExportArchive;
using static Novalist.Core.Services.ExportProse;

namespace Novalist.Core.Services;

public partial class ExportService
{
    /// <summary>
    /// Writes a DOCX laid out on the Normseite grid: every line hard-wrapped to
    /// the preset's column count, a forced page break every N lines, and a
    /// running header carrying the title and "Seite x von y".
    /// </summary>
    public static async Task WriteNormseitenDocxAsync(
        IReadOnlyList<NormseitenBlock> blocks,
        ExportOptions options,
        string outputPath)
    {
        var preset = options.ResolvePreset();
        var lines = NormseitenRenderer.RenderLines(blocks, preset.GridColumns);
        var metrics = NormseitenRenderer.Measure(lines, preset.GridLines);
        var pages = Math.Max(1, metrics.Pages);

        using var stream = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create);

        await WriteNormseitenPackageLinksAsync(zip);
        await WriteNormseitenStylesAsync(zip, preset);
        await WriteNormseitenHeaderAsync(zip, options, preset, pages);

        var body = new StringBuilder();
        for (var i = 0; i < lines.Count; i++)
        {
            var pageBreak = i > 0 && i % preset.GridLines == 0
                ? "<w:r><w:br w:type=\"page\"/></w:r>"
                : string.Empty;
            var content = lines[i].Length == 0
                ? string.Empty
                : $"<w:r><w:t xml:space=\"preserve\">{EscapeXml(lines[i])}</w:t></w:r>";
            body.Append($"<w:p>{pageBreak}{content}</w:p>");
        }

        await WriteEntryAsync(zip, "word/document.xml", $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <w:body>
                {body}
                <w:sectPr>
                  <w:headerReference w:type="default" r:id="rId2"/>
                  <w:pgSz w:w="{CmToTwips(preset.PageWidthCm)}" w:h="{CmToTwips(preset.PageHeightCm)}"/>
                  <w:pgMar w:top="{CmToTwips(preset.MarginTopCm)}" w:right="{CmToTwips(preset.MarginRightCm)}" w:bottom="{CmToTwips(preset.MarginBottomCm)}" w:left="{CmToTwips(preset.MarginLeftCm)}" w:header="{CmToTwips(preset.HeaderDistanceCm)}" w:footer="{CmToTwips(preset.HeaderDistanceCm)}" w:gutter="0"/>
                </w:sectPr>
              </w:body>
            </w:document>
            """);
    }

    private static async Task WriteNormseitenPackageLinksAsync(ZipArchive zip)
    {
        await WriteEntryAsync(zip, "[Content_Types].xml", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
              <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
              <Default Extension="xml" ContentType="application/xml"/>
              <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
              <Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/>
              <Override PartName="/word/header1.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.header+xml"/>
            </Types>
            """);

        await WriteEntryAsync(zip, "_rels/.rels", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
            </Relationships>
            """);

        await WriteEntryAsync(zip, "word/_rels/document.xml.rels", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
              <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/header" Target="header1.xml"/>
            </Relationships>
            """);

    }

    private static async Task WriteNormseitenStylesAsync(ZipArchive zip, ExportPreset preset)
    {
        var font = preset.BodyFontFamily;
        var halfPoints = (int)Math.Round(preset.BodyFontSizePt * 2);
        // Word measures exact line spacing in twentieths of a point.
        var exactLine = (int)Math.Round((preset.LineHeightPt > 0 ? preset.LineHeightPt : preset.BodyFontSizePt * 2) * 20);

        await WriteEntryAsync(zip, "word/styles.xml", $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:docDefaults>
                <w:rPrDefault>
                  <w:rPr>
                    <w:rFonts w:ascii="{font}" w:hAnsi="{font}" w:eastAsia="{font}" w:cs="{font}"/>
                    <w:sz w:val="{halfPoints}"/>
                    <w:szCs w:val="{halfPoints}"/>
                  </w:rPr>
                </w:rPrDefault>
                <w:pPrDefault>
                  <w:pPr>
                    <w:spacing w:before="0" w:after="0" w:line="{exactLine}" w:lineRule="exact"/>
                  </w:pPr>
                </w:pPrDefault>
              </w:docDefaults>
              <w:style w:type="paragraph" w:default="1" w:styleId="Normal">
                <w:name w:val="Normal"/>
              </w:style>
              <w:style w:type="paragraph" w:styleId="Header">
                <w:name w:val="header"/>
                <w:basedOn w:val="Normal"/>
                <w:pPr>
                  <w:spacing w:before="0" w:after="0" w:line="240" w:lineRule="auto"/>
                </w:pPr>
                <w:rPr>
                  <w:sz w:val="20"/>
                  <w:szCs w:val="20"/>
                </w:rPr>
              </w:style>
            </w:styles>
            """);

    }

    private static async Task WriteNormseitenHeaderAsync(ZipArchive zip, ExportOptions options, ExportPreset preset, int pages)
    {
        // Header: title flush left, page counter flush right against the text edge.
        var textWidthTwips = CmToTwips(preset.TextWidthCm);
        var headerTitle = string.IsNullOrWhiteSpace(options.Title) ? string.Empty : EscapeXml(options.Title);
        await WriteEntryAsync(zip, "word/header1.xml", $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:hdr xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:p>
                <w:pPr>
                  <w:pStyle w:val="Header"/>
                  <w:tabs><w:tab w:val="right" w:pos="{textWidthTwips}"/></w:tabs>
                </w:pPr>
                <w:r><w:t xml:space="preserve">{headerTitle}</w:t></w:r>
                <w:r><w:tab/><w:t xml:space="preserve">Seite </w:t></w:r>
                <w:r><w:fldChar w:fldCharType="begin"/></w:r>
                <w:r><w:instrText xml:space="preserve"> PAGE </w:instrText></w:r>
                <w:r><w:fldChar w:fldCharType="end"/></w:r>
                <w:r><w:t xml:space="preserve"> von {pages}</w:t></w:r>
              </w:p>
            </w:hdr>
            """);

    }
}
