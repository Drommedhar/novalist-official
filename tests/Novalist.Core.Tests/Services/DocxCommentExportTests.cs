using System.IO.Compression;
using System.Text;
using NSubstitute;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Core.Tests.TestHelpers;
using Xunit;

namespace Novalist.Core.Tests.Services;

/// <summary>
/// Scene comments leaving as real Word comments, so an editor can reply to them
/// in Word and the reply comes back through <see cref="DocxReviewReader"/>.
/// </summary>
public class DocxCommentExportTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly IProjectService _project = Substitute.For<IProjectService>();

    public void Dispose() => _dir.Dispose();

    private ExportService BuildWith(SceneData scene, string html, out string chapterGuid)
    {
        var chapter = new ChapterData { Title = "One", Order = 1 };
        scene.ChapterGuid = chapter.Guid;
        _project.ReadSceneContentAsync(chapter, scene).Returns(html);
        _project.GetChaptersOrdered().Returns(new List<ChapterData> { chapter });
        _project.GetScenesForChapter(chapter.Guid).Returns(new List<SceneData> { scene });
        chapterGuid = chapter.Guid;
        return new ExportService(_project);
    }

    private static string ReadEntry(string docx, string entry)
    {
        using var zip = ZipFile.OpenRead(docx);
        var found = zip.GetEntry(entry);
        Assert.NotNull(found);
        using var reader = new StreamReader(found!.Open(), Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static bool HasEntry(string docx, string entry)
    {
        using var zip = ZipFile.OpenRead(docx);
        return zip.GetEntry(entry) != null;
    }

    private async Task<string> ExportAsync(SceneData scene, string html)
    {
        var sut = BuildWith(scene, html, out var guid);
        var outPath = _dir.Combine("book.docx");
        await sut.ExportAsync(
            new ExportOptions
            {
                Format = ExportFormat.Docx,
                Title = "T",
                SelectedChapterGuids = [guid]
            },
            outPath);
        return outPath;
    }

    [Fact]
    public async Task Export_NoComments_OmitsTheCommentsPartEntirely()
    {
        var docx = await ExportAsync(
            new SceneData { Id = "s1", Title = "S", Order = 1 },
            "<p>Plain prose.</p>");

        Assert.False(HasEntry(docx, "word/comments.xml"));
        Assert.DoesNotContain("comments.xml", ReadEntry(docx, "[Content_Types].xml"));
    }

    [Fact]
    public async Task Export_Comment_WritesPartRelationshipAndContentType()
    {
        var scene = new SceneData
        {
            Id = "s1",
            Title = "S",
            Order = 1,
            Comments =
            [
                new SceneComment { Id = "c1", AnchorText = "the flagged phrase", Text = "This drags." }
            ]
        };

        var docx = await ExportAsync(scene, "<p>Before the flagged phrase after.</p>");

        Assert.True(HasEntry(docx, "word/comments.xml"));
        Assert.Contains("comments.xml", ReadEntry(docx, "[Content_Types].xml"));
        Assert.Contains("comments.xml", ReadEntry(docx, "word/_rels/document.xml.rels"));

        var comments = ReadEntry(docx, "word/comments.xml");
        Assert.Contains("This drags.", comments);
        Assert.Contains("w:id=\"0\"", comments);
    }

    [Fact]
    public async Task Export_Comment_AnchorsToTheParagraphContainingItsText()
    {
        var scene = new SceneData
        {
            Id = "s1",
            Title = "S",
            Order = 1,
            Comments = [new SceneComment { Id = "c1", AnchorText = "second", Text = "Note" }]
        };

        var docx = await ExportAsync(scene, "<p>First paragraph.</p><p>The second paragraph.</p>");
        var document = ReadEntry(docx, "word/document.xml");

        Assert.Contains("<w:commentRangeStart w:id=\"0\"/>", document);
        Assert.Contains("<w:commentRangeEnd w:id=\"0\"/>", document);
        Assert.Contains("<w:commentReference w:id=\"0\"/>", document);
        // The marker belongs to the paragraph that actually contains the anchor.
        var markerAt = document.IndexOf("commentRangeStart", StringComparison.Ordinal);
        var secondAt = document.IndexOf("The second paragraph.", StringComparison.Ordinal);
        var firstAt = document.IndexOf("First paragraph.", StringComparison.Ordinal);
        Assert.True(markerAt > firstAt && markerAt < secondAt);
    }

    [Fact]
    public async Task Export_ResolvedComments_AreLeftOut()
    {
        var scene = new SceneData
        {
            Id = "s1",
            Title = "S",
            Order = 1,
            Comments =
            [
                new SceneComment { Id = "c1", AnchorText = "phrase", Text = "Settled", Resolved = true }
            ]
        };

        var docx = await ExportAsync(scene, "<p>A phrase here.</p>");

        Assert.False(HasEntry(docx, "word/comments.xml"));
    }

    [Fact]
    public async Task Export_EmptyCommentText_IsLeftOut()
    {
        var scene = new SceneData
        {
            Id = "s1",
            Title = "S",
            Order = 1,
            Comments = [new SceneComment { Id = "c1", AnchorText = "phrase", Text = "   " }]
        };

        Assert.False(HasEntry(await ExportAsync(scene, "<p>A phrase here.</p>"), "word/comments.xml"));
    }

    [Fact]
    public async Task Export_CommentWhoseAnchorIsNotFound_StillShipsTheComment()
    {
        // The prose may have been rewritten since the comment was made. Losing
        // the anchor must not lose the editor's note.
        var scene = new SceneData
        {
            Id = "s1",
            Title = "S",
            Order = 1,
            Comments = [new SceneComment { Id = "c1", AnchorText = "text that is gone", Text = "Still relevant" }]
        };

        var docx = await ExportAsync(scene, "<p>Entirely different prose.</p>");

        Assert.Contains("Still relevant", ReadEntry(docx, "word/comments.xml"));
        Assert.DoesNotContain("commentRangeStart", ReadEntry(docx, "word/document.xml"));
    }

    [Fact]
    public async Task Export_RepeatedAnchorText_IsMarkedOnce()
    {
        var scene = new SceneData
        {
            Id = "s1",
            Title = "S",
            Order = 1,
            Comments = [new SceneComment { Id = "c1", AnchorText = "echo", Text = "Note" }]
        };

        var document = ReadEntry(
            await ExportAsync(scene, "<p>An echo here.</p><p>Another echo there.</p>"),
            "word/document.xml");

        var occurrences = document.Split("commentRangeStart").Length - 1;
        Assert.Equal(1, occurrences);
    }

    [Fact]
    public async Task Export_MultipleComments_GetDistinctIds()
    {
        var scene = new SceneData
        {
            Id = "s1",
            Title = "S",
            Order = 1,
            Comments =
            [
                new SceneComment { Id = "c1", AnchorText = "alpha", Text = "First" },
                new SceneComment { Id = "c2", AnchorText = "beta", Text = "Second" }
            ]
        };

        var comments = ReadEntry(
            await ExportAsync(scene, "<p>The alpha and the beta.</p>"),
            "word/comments.xml");

        Assert.Contains("w:id=\"0\"", comments);
        Assert.Contains("w:id=\"1\"", comments);
    }

    [Fact]
    public async Task Export_CommentText_IsXmlEscaped()
    {
        var scene = new SceneData
        {
            Id = "s1",
            Title = "S",
            Order = 1,
            Comments = [new SceneComment { Id = "c1", AnchorText = "x", Text = "Uses <b> & \"quotes\"" }]
        };

        var comments = ReadEntry(await ExportAsync(scene, "<p>An x here.</p>"), "word/comments.xml");

        Assert.Contains("&lt;b&gt;", comments);
        Assert.Contains("&amp;", comments);
        Assert.DoesNotContain("<b>", comments);
    }

    [Fact]
    public async Task Export_MultiLineComment_BecomesMultipleParagraphs()
    {
        var scene = new SceneData
        {
            Id = "s1",
            Title = "S",
            Order = 1,
            Comments = [new SceneComment { Id = "c1", AnchorText = "x", Text = "Line one\nLine two" }]
        };

        var comments = ReadEntry(await ExportAsync(scene, "<p>An x here.</p>"), "word/comments.xml");

        Assert.Contains("Line one", comments);
        Assert.Contains("Line two", comments);
        Assert.Equal(2, comments.Split("<w:p>").Length - 1);
    }

    [Fact]
    public async Task Export_RoundTripsThroughTheReviewReader()
    {
        // The point of the feature: what leaves as a comment comes back as one.
        var scene = new SceneData
        {
            Id = "s1",
            Title = "S",
            Order = 1,
            Comments = [new SceneComment { Id = "c1", AnchorText = "flagged phrase", Text = "Tighten this." }]
        };

        var docx = await ExportAsync(scene, "<p>Before the flagged phrase after.</p>");
        var review = DocxReviewReader.Read(docx);

        var comment = Assert.Single(review.Comments);
        Assert.Equal("Tighten this.", comment.Text);
        Assert.Contains("flagged phrase", comment.AnchorText);
    }

    [Fact]
    public void GenerateDocxComments_EmptyList_ProducesAWellFormedEmptyPart()
    {
        var xml = ExportService.GenerateDocxComments([]);

        Assert.Contains("<w:comments", xml);
        Assert.DoesNotContain("<w:comment ", xml);
    }

    [Fact]
    public async Task Export_SceneWithoutComments_IsUntouchedWhenAnotherSceneHasThem()
    {
        var chapter = new ChapterData { Title = "One", Order = 1 };
        var commented = new SceneData
        {
            Id = "s1",
            Title = "A",
            Order = 1,
            ChapterGuid = chapter.Guid,
            Comments = [new SceneComment { Id = "c1", AnchorText = "marked", Text = "Note" }]
        };
        var plain = new SceneData { Id = "s2", Title = "B", Order = 2, ChapterGuid = chapter.Guid };

        _project.ReadSceneContentAsync(chapter, commented).Returns("<p>A marked phrase.</p>");
        _project.ReadSceneContentAsync(chapter, plain).Returns("<p>Nothing flagged here.</p>");
        _project.GetChaptersOrdered().Returns(new List<ChapterData> { chapter });
        _project.GetScenesForChapter(chapter.Guid).Returns(new List<SceneData> { commented, plain });

        var outPath = _dir.Combine("two-scenes.docx");
        await new ExportService(_project).ExportAsync(
            new ExportOptions
            {
                Format = ExportFormat.Docx,
                Title = "T",
                SelectedChapterGuids = [chapter.Guid]
            },
            outPath);

        var document = ReadEntry(outPath, "word/document.xml");
        // Exactly one anchor: the uncommented scene gets no markers at all.
        Assert.Equal(1, document.Split("commentRangeStart").Length - 1);
        Assert.Contains("Nothing flagged here.", document);
    }
}
