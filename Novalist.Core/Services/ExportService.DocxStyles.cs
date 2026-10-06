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
    /// The styles part of a reference DOCX, or null when there is not a usable
    /// one. A publisher's house style arrives as a styled Word file, not as a
    /// list of settings, and reapplying it by hand after every export is how a
    /// submission goes out in the wrong font.
    /// </summary>
    internal static string? ReadReferenceStyles(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        try
        {
            using var archive = ZipFile.OpenRead(path);
            var entry = archive.GetEntry("word/styles.xml");
            if (entry == null) return null;

            using var reader = new StreamReader(entry.Open());
            var xml = reader.ReadToEnd();
            // A part without the wordprocessing root is not a styles part, and
            // writing it would produce a file Word refuses to open at all.
            return xml.Contains("<w:styles", StringComparison.Ordinal) ? xml : null;
        }
        catch (Exception ex) when (
            ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            // A reference document that cannot be read is a reason to fall back
            // to ours, never a reason to fail the export the writer asked for.
            return null;
        }
    }

    private static string GenerateDocxStyles(ExportOptions options)
    {
        if (ReadReferenceStyles(options.ReferenceDocPath) is { } borrowed) return borrowed;

        var smf = options.ResolvePreset().ShunnHeader;
        var fontFamily = smf ? "Courier New" : "Georgia";
        var fontSize = "24";
        var lineSpacing = smf ? "480" : "360";
        var bodyIndent = smf ? "<w:ind w:firstLine=\"720\"/>" : "<w:spacing w:after=\"160\"/>";
        var noIndentSpacing = smf ? "" : "<w:spacing w:after=\"160\"/>";

        return $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:docDefaults>
                <w:rPrDefault>
                  <w:rPr>
                    <w:rFonts w:ascii="{fontFamily}" w:hAnsi="{fontFamily}" w:eastAsia="{fontFamily}" w:cs="{fontFamily}"/>
                    <w:sz w:val="{fontSize}"/>
                    <w:szCs w:val="{fontSize}"/>
                    <w:lang w:val="en-US"/>
                  </w:rPr>
                </w:rPrDefault>
                <w:pPrDefault>
                  <w:pPr>
                    <w:spacing w:line="{lineSpacing}" w:lineRule="auto"/>
                  </w:pPr>
                </w:pPrDefault>
              </w:docDefaults>

            {DocxTitleStyles}

              <w:style w:type="paragraph" w:styleId="BodyText">
                <w:name w:val="Body Text"/>
                <w:basedOn w:val="Normal"/>
                <w:pPr>
                  {bodyIndent}
                  <w:jc w:val="both"/>
                </w:pPr>
              </w:style>

              <w:style w:type="paragraph" w:styleId="NoIndent">
                <w:name w:val="No Indent"/>
                <w:basedOn w:val="Normal"/>
                <w:pPr>
                  <w:ind w:firstLine="0"/>
                  {noIndentSpacing}
                  <w:jc w:val="both"/>
                </w:pPr>
              </w:style>

            {DocxNoteStyles}

            {DocxBlockStyles}

            </w:styles>
            """;
    }

    private static string GenerateDocxNumbering()
    {
        var bulletLevels = new StringBuilder();
        var decimalLevels = new StringBuilder();
        for (var level = 0; level < 9; level++)
        {
            var indent = 720 * (level + 1);
            bulletLevels.Append($"""
                  <w:lvl w:ilvl="{level}">
                    <w:start w:val="1"/>
                    <w:numFmt w:val="bullet"/>
                    <w:lvlText w:val="&#8226;"/>
                    <w:lvlJc w:val="left"/>
                    <w:pPr><w:ind w:left="{indent}" w:hanging="360"/></w:pPr>
                    <w:rPr><w:rFonts w:ascii="Symbol" w:hAnsi="Symbol" w:hint="default"/></w:rPr>
                  </w:lvl>
                """);
            decimalLevels.Append($"""
                  <w:lvl w:ilvl="{level}">
                    <w:start w:val="1"/>
                    <w:numFmt w:val="decimal"/>
                    <w:lvlText w:val="%{level + 1}."/>
                    <w:lvlJc w:val="left"/>
                    <w:pPr><w:ind w:left="{indent}" w:hanging="360"/></w:pPr>
                  </w:lvl>
                """);
        }

        return $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:numbering xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:abstractNum w:abstractNumId="0">
                <w:multiLevelType w:val="hybridMultilevel"/>
            {bulletLevels}
              </w:abstractNum>
              <w:abstractNum w:abstractNumId="1">
                <w:multiLevelType w:val="hybridMultilevel"/>
            {decimalLevels}
              </w:abstractNum>
              <w:num w:numId="{DocxBulletNumId}"><w:abstractNumId w:val="0"/></w:num>
              <w:num w:numId="{DocxNumberNumId}"><w:abstractNumId w:val="1"/></w:num>
            </w:numbering>
            """;
    }

    private const int DocxBulletNumId = 1;

    private const int DocxNumberNumId = 2;

    /// <summary>The paragraph properties for one exported block: its Word style
    /// and, for a list item, the numbering it belongs to.</summary>
    private static string DocxParagraphProperties(ExportBlock block, bool firstInScene)
    {
        if (block.List != ListKind.None)
        {
            var numId = block.List == ListKind.Number ? DocxNumberNumId : DocxBulletNumId;
            return "<w:pStyle w:val=\"ListParagraph\"/>"
                   + $"<w:numPr><w:ilvl w:val=\"0\"/><w:numId w:val=\"{numId}\"/></w:numPr>";
        }

        var style = block.StyleId switch
        {
            "heading" => "Heading2",
            "subheading" => "Heading3",
            "blockquote" => "Quote",
            "poetry" => "Verse",
            // The first paragraph of a scene is not indented; typographic
            // convention, and what the exporter already did.
            _ => firstInScene ? "NoIndent" : "BodyText"
        };
        return $"<w:pStyle w:val=\"{style}\"/>";
    }

    private const string DocxTitleStyles = """
              <w:style w:type="paragraph" w:default="1" w:styleId="Normal">
                <w:name w:val="Normal"/>
              </w:style>

              <w:style w:type="paragraph" w:styleId="Title">
                <w:name w:val="Title"/>
                <w:basedOn w:val="Normal"/>
                <w:pPr>
                  <w:jc w:val="center"/>
                  <w:spacing w:before="4800" w:after="240"/>
                </w:pPr>
                <w:rPr>
                  <w:sz w:val="52"/>
                  <w:szCs w:val="52"/>
                  <w:b/>
                  <w:bCs/>
                </w:rPr>
              </w:style>

              <w:style w:type="paragraph" w:styleId="Subtitle">
                <w:name w:val="Subtitle"/>
                <w:basedOn w:val="Normal"/>
                <w:pPr>
                  <w:jc w:val="center"/>
                  <w:spacing w:before="240"/>
                </w:pPr>
                <w:rPr>
                  <w:sz w:val="32"/>
                  <w:szCs w:val="32"/>
                  <w:i/>
                  <w:iCs/>
                </w:rPr>
              </w:style>

              <w:style w:type="paragraph" w:styleId="Heading1">
                <w:name w:val="heading 1"/>
                <w:basedOn w:val="Normal"/>
                <w:pPr>
                  <w:jc w:val="center"/>
                  <w:spacing w:before="1440" w:after="720"/>
                </w:pPr>
                <w:rPr>
                  <w:sz w:val="36"/>
                  <w:szCs w:val="36"/>
                  <w:b/>
                  <w:bCs/>
                </w:rPr>
              </w:style>
            """;

    private const string DocxNoteStyles = """
              <w:style w:type="character" w:styleId="FootnoteReference">
                <w:name w:val="footnote reference"/>
                <w:rPr><w:vertAlign w:val="superscript"/></w:rPr>
              </w:style>
              <w:style w:type="paragraph" w:styleId="FootnoteText">
                <w:name w:val="footnote text"/>
                <w:basedOn w:val="Normal"/>
                <w:pPr><w:spacing w:line="240" w:lineRule="auto" w:after="0"/></w:pPr>
                <w:rPr><w:sz w:val="20"/></w:rPr>
              </w:style>
              <w:style w:type="paragraph" w:styleId="SceneBreak">
                <w:name w:val="Scene Break"/>
                <w:basedOn w:val="Normal"/>
                <w:pPr>
                  <w:jc w:val="center"/>
                  <w:spacing w:before="360" w:after="360"/>
                </w:pPr>
              </w:style>
            """;

    private const string DocxBlockStyles = """
              <w:style w:type="paragraph" w:styleId="Heading2">
                <w:name w:val="heading 2"/>
                <w:basedOn w:val="Normal"/>
                <w:pPr>
                  <w:outlineLvl w:val="1"/>
                  <w:ind w:firstLine="0"/>
                  <w:spacing w:before="360" w:after="180"/>
                  <w:keepNext/>
                </w:pPr>
                <w:rPr><w:b/><w:sz w:val="30"/><w:szCs w:val="30"/></w:rPr>
              </w:style>

              <w:style w:type="paragraph" w:styleId="Heading3">
                <w:name w:val="heading 3"/>
                <w:basedOn w:val="Normal"/>
                <w:pPr>
                  <w:outlineLvl w:val="2"/>
                  <w:ind w:firstLine="0"/>
                  <w:spacing w:before="280" w:after="140"/>
                  <w:keepNext/>
                </w:pPr>
                <w:rPr><w:b/><w:sz w:val="26"/><w:szCs w:val="26"/></w:rPr>
              </w:style>

              <w:style w:type="paragraph" w:styleId="Quote">
                <w:name w:val="Quote"/>
                <w:basedOn w:val="Normal"/>
                <w:pPr>
                  <w:ind w:left="720" w:right="720" w:firstLine="0"/>
                  <w:spacing w:before="180" w:after="180"/>
                </w:pPr>
                <w:rPr><w:i/></w:rPr>
              </w:style>

              <w:style w:type="paragraph" w:styleId="Verse">
                <w:name w:val="Verse"/>
                <w:basedOn w:val="Normal"/>
                <w:pPr>
                  <w:ind w:left="720" w:firstLine="0"/>
                  <w:jc w:val="left"/>
                </w:pPr>
              </w:style>

              <w:style w:type="paragraph" w:styleId="ListParagraph">
                <w:name w:val="List Paragraph"/>
                <w:basedOn w:val="Normal"/>
                <w:pPr>
                  <w:ind w:left="720" w:firstLine="0"/>
                  <w:contextualSpacing/>
                  <w:jc w:val="left"/>
                </w:pPr>
              </w:style>
            """;
}
