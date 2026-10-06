using System.IO.Compression;
using System.Text;
using Novalist.Core.Models;
using static Novalist.Core.Services.ExportArchive;

namespace Novalist.Core.Services;

public partial class ExportService
{
    private static string GenerateEpubStylesheet(ExportOptions options)
    {
        var preset = options.ResolvePreset();
        // Appended rather than merged, so the writer's rules win by cascade
        // order - which is the only way they can override anything above.
        var extra = string.IsNullOrWhiteSpace(preset.EbookCss)
            ? string.Empty
            : "\n\n/* From your export layout */\n" + preset.EbookCss;
        // Concatenated rather than interpolated: the stylesheet below is full of
        // CSS braces, every one of which an interpolated string reads as a hole.
        return BaseEpubStylesheet + extra;
    }

    private const string BaseEpubStylesheet = """
            @page { margin: 1in; }

            body {
              font-family: Georgia, "Times New Roman", Times, serif;
              line-height: 1.5;
              margin: 1em;
              padding: 0;
            }

            h1.chapter-title {
              font-size: 1.5em;
              text-align: center;
              font-weight: bold;
              margin-top: 3em;
              margin-bottom: 2em;
            }

            p {
              margin-top: 0;
              margin-bottom: 0.8em;
              text-align: justify;
              orphans: 2;
              widows: 2;
            }

            p.scene-break {
              text-align: center;
              margin-top: 1.5em;
              margin-bottom: 1.5em;
            }

            p.chapter-subtitle {
              text-align: center;
              text-indent: 0;
              font-style: italic;
              margin-top: -0.6em;
              margin-bottom: 1.6em;
            }

            span.drop-cap {
              float: left;
              font-size: 3.2em;
              line-height: 0.85;
              padding-right: 0.06em;
            }

            span.lead-in {
              font-variant: small-caps;
            }

            p.prose-image {
              text-align: center;
              text-indent: 0;
              margin: 1.5em 0;
            }

            p.prose-image img {
              max-width: 100%;
            }

            h2, h3 {
              text-align: left;
              margin-top: 1.6em;
              margin-bottom: 0.6em;
            }

            blockquote {
              margin: 1em 2em;
              font-style: italic;
            }

            blockquote p {
              text-align: left;
              text-indent: 0;
            }

            /* Verse keeps its own line breaks and is never justified, which
               would stretch a short line across the page. */
            p.poetry {
              margin: 0 0 0.2em 2em;
              text-align: left;
              text-indent: 0;
              white-space: pre-wrap;
            }

            ul, ol {
              margin: 0.8em 0 0.8em 2em;
              padding: 0;
            }

            li {
              margin-bottom: 0.3em;
              text-align: left;
            }

            p.series, p.publisher {
              text-align: center;
              font-style: italic;
              margin: 0.4em 0;
            }

            div.title-page {
              text-align: center;
              padding-top: 30%;
            }

            div.title-page h1 {
              font-size: 2em;
              font-weight: bold;
              margin-bottom: 1em;
              text-indent: 0;
            }

            div.title-page p.author {
              font-size: 1.2em;
              font-style: italic;
              text-indent: 0;
            }
            """;
}
