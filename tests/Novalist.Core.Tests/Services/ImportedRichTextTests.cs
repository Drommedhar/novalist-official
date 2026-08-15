using Novalist.Core.Services;
using Xunit;

namespace Novalist.Core.Tests.Services;

/// <summary>
/// The safe renderers for the structured import model. These are the branches
/// <see cref="ManuscriptSplitter"/> cannot reach on its own: it consumes a scene
/// break as a scene boundary, so the ornament only ever renders on the path
/// <see cref="ScrivenerReader"/> takes, which hands a whole document straight to
/// the renderer without splitting it first.
/// </summary>
public sealed class ImportedRichTextTests
{
    [Fact]
    public void ASceneBreakClosesAnOpenListRatherThanLeavingItHanging()
    {
        var html = ImportedRichText.ToHtml([
            new ImportedParagraph { Text = "Item", ListKind = ImportedListKind.Unordered },
            new ImportedParagraph { IsSceneBreak = true },
            new ImportedParagraph { Text = "After the break." }
        ]);

        Assert.Equal("<ul><li>Item</li></ul><p>***</p><p>After the break.</p>", html);
    }

    [Fact]
    public void BlockQuoteAndPoetryCarryTheirStyleAsAClassNotAsPageGeometry()
    {
        var html = ImportedRichText.ToHtml([
            new ImportedParagraph { Text = "Quoted", Style = ImportedParagraphStyle.BlockQuote },
            new ImportedParagraph { Text = "Versed", Style = ImportedParagraphStyle.Poetry }
        ]);

        Assert.Equal(
            "<p class=\"nv-style-blockquote\">Quoted</p><p class=\"nv-style-poetry\">Versed</p>",
            html);
    }

    [Fact]
    public void MarkdownKeepsSceneBreaksListsAndQuotations()
    {
        var markdown = ImportedRichText.ToMarkdown([
            new ImportedParagraph { Text = "Item", ListKind = ImportedListKind.Ordered },
            new ImportedParagraph { IsSceneBreak = true },
            new ImportedParagraph { Text = "Quoted", Style = ImportedParagraphStyle.BlockQuote }
        ]);

        Assert.Equal("1. Item\n\n***\n\n> Quoted", markdown);
    }
}
