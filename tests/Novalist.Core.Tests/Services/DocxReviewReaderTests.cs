using System.IO.Compression;
using System.Text;
using Novalist.Core.Services;
using Novalist.Core.Tests.TestHelpers;
using Xunit;

namespace Novalist.Core.Tests.Services;

/// <summary>
/// Reading the review layer back out of a .docx an editor returned. Every
/// failure mode here is a file the writer did not author, so nothing may throw
/// - an unreadable file must read as "nothing to import".
/// </summary>
public class DocxReviewReaderTests
{
    private const string W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    /// <summary>Builds a minimal .docx with the given part bodies.</summary>
    private static string BuildDocx(TempDir dir, string documentXml, string? commentsXml = null)
    {
        var path = dir.Combine("review.docx");
        using (var stream = File.Create(path))
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            Write(zip, "word/document.xml", documentXml);
            if (commentsXml != null)
                Write(zip, "word/comments.xml", commentsXml);
        }
        return path;
    }

    private static void Write(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name);
        using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
        writer.Write(content);
    }

    private static string Document(string body) =>
        $"""<?xml version="1.0" encoding="UTF-8"?><w:document xmlns:w="{W}"><w:body>{body}</w:body></w:document>""";

    private static string Comments(string comments) =>
        $"""<?xml version="1.0" encoding="UTF-8"?><w:comments xmlns:w="{W}">{comments}</w:comments>""";

    // ── Tolerance ──

    [Fact]
    public void Read_MissingFile_IsEmpty()
    {
        using var dir = new TempDir();
        var review = DocxReviewReader.Read(dir.Combine("nope.docx"));
        Assert.True(review.IsEmpty);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Read_BlankPath_IsEmpty(string path) =>
        Assert.True(DocxReviewReader.Read(path).IsEmpty);

    [Fact]
    public void Read_NotAZipFile_IsEmpty()
    {
        using var dir = new TempDir();
        var path = dir.Combine("not-a-docx.docx");
        File.WriteAllText(path, "this is plain text, not a Word document");

        Assert.True(DocxReviewReader.Read(path).IsEmpty);
    }

    [Fact]
    public void Read_ZipWithoutDocumentPart_IsEmpty()
    {
        using var dir = new TempDir();
        var path = dir.Combine("empty.docx");
        using (var stream = File.Create(path))
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
            Write(zip, "word/styles.xml", "<styles/>");

        Assert.True(DocxReviewReader.Read(path).IsEmpty);
    }

    [Fact]
    public void Read_MalformedDocumentXml_IsEmpty()
    {
        using var dir = new TempDir();
        var path = BuildDocx(dir, "<w:document><unclosed>");

        Assert.True(DocxReviewReader.Read(path).IsEmpty);
    }

    [Fact]
    public void Read_CleanDocument_IsEmptyButNotAnError()
    {
        using var dir = new TempDir();
        var path = BuildDocx(dir, Document("<w:p><w:r><w:t>Just prose.</w:t></w:r></w:p>"));

        var review = DocxReviewReader.Read(path);

        Assert.True(review.IsEmpty);
        Assert.Empty(review.Comments);
        Assert.Empty(review.Revisions);
    }

    // ── Comments ──

    [Fact]
    public void Read_Comment_CarriesAuthorTextAndAnchor()
    {
        using var dir = new TempDir();
        var path = BuildDocx(
            dir,
            Document("""
                <w:p>
                  <w:r><w:t>Before </w:t></w:r>
                  <w:commentRangeStart w:id="1"/>
                  <w:r><w:t>the flagged phrase</w:t></w:r>
                  <w:commentRangeEnd w:id="1"/>
                  <w:r><w:t> after.</w:t></w:r>
                </w:p>
                """),
            Comments("""
                <w:comment w:id="1" w:author="An Editor" w:date="2026-07-29T10:00:00Z">
                  <w:p><w:r><w:t>This line drags.</w:t></w:r></w:p>
                </w:comment>
                """));

        var review = DocxReviewReader.Read(path);

        var comment = Assert.Single(review.Comments);
        Assert.Equal("An Editor", comment.Author);
        Assert.Equal("This line drags.", comment.Text);
        Assert.Equal("the flagged phrase", comment.AnchorText);
        Assert.Equal("2026-07-29T10:00:00Z", comment.Date);
    }

    [Fact]
    public void Read_MultiParagraphComment_JoinsLines()
    {
        using var dir = new TempDir();
        var path = BuildDocx(
            dir,
            Document("<w:p><w:r><w:t>Text.</w:t></w:r></w:p>"),
            Comments("""
                <w:comment w:id="7" w:author="E">
                  <w:p><w:r><w:t>First point.</w:t></w:r></w:p>
                  <w:p><w:r><w:t>Second point.</w:t></w:r></w:p>
                </w:comment>
                """));

        var comment = Assert.Single(DocxReviewReader.Read(path).Comments);
        Assert.Equal("First point.\nSecond point.", comment.Text);
    }

    [Fact]
    public void Read_CommentWithoutRangeMarkers_HasNoAnchorButIsStillReturned()
    {
        using var dir = new TempDir();
        var path = BuildDocx(
            dir,
            Document("<w:p><w:r><w:t>Text.</w:t></w:r></w:p>"),
            Comments("""<w:comment w:id="2" w:author="E"><w:p><w:r><w:t>General note.</w:t></w:r></w:p></w:comment>"""));

        var comment = Assert.Single(DocxReviewReader.Read(path).Comments);
        Assert.Equal("General note.", comment.Text);
        Assert.Equal(string.Empty, comment.AnchorText);
    }

    [Fact]
    public void Read_OverlappingCommentRanges_EachGetsItsOwnAnchor()
    {
        using var dir = new TempDir();
        var path = BuildDocx(
            dir,
            Document("""
                <w:p>
                  <w:commentRangeStart w:id="1"/>
                  <w:r><w:t>outer </w:t></w:r>
                  <w:commentRangeStart w:id="2"/>
                  <w:r><w:t>inner</w:t></w:r>
                  <w:commentRangeEnd w:id="2"/>
                  <w:commentRangeEnd w:id="1"/>
                </w:p>
                """),
            Comments("""
                <w:comment w:id="1" w:author="E"><w:p><w:r><w:t>One</w:t></w:r></w:p></w:comment>
                <w:comment w:id="2" w:author="E"><w:p><w:r><w:t>Two</w:t></w:r></w:p></w:comment>
                """));

        var review = DocxReviewReader.Read(path);

        Assert.Equal("outer inner", review.Comments.Single(c => c.Id == "1").AnchorText);
        Assert.Equal("inner", review.Comments.Single(c => c.Id == "2").AnchorText);
    }

    [Fact]
    public void Read_CommentsPartWithoutDocumentAnchors_StillReturnsComments()
    {
        using var dir = new TempDir();
        var path = BuildDocx(
            dir,
            Document("<w:p><w:r><w:t>Prose.</w:t></w:r></w:p>"),
            Comments("""<w:comment w:id="9"><w:p><w:r><w:t>No author set.</w:t></w:r></w:p></w:comment>"""));

        var comment = Assert.Single(DocxReviewReader.Read(path).Comments);
        Assert.Equal(string.Empty, comment.Author);
        Assert.Equal("No author set.", comment.Text);
    }

    [Fact]
    public void Read_MalformedCommentsPart_YieldsNoCommentsButKeepsRevisions()
    {
        using var dir = new TempDir();
        var path = BuildDocx(
            dir,
            Document("""<w:ins w:author="E"><w:r><w:t>added</w:t></w:r></w:ins>"""),
            "<w:comments><broken>");

        var review = DocxReviewReader.Read(path);

        Assert.Empty(review.Comments);
        Assert.Single(review.Revisions);
    }

    // ── Tracked changes ──

    [Fact]
    public void Read_Insertion_IsCaptured()
    {
        using var dir = new TempDir();
        var path = BuildDocx(dir, Document("""
            <w:p><w:ins w:id="1" w:author="An Editor" w:date="2026-07-29T10:00:00Z">
              <w:r><w:t>newly added words</w:t></w:r>
            </w:ins></w:p>
            """));

        var revision = Assert.Single(DocxReviewReader.Read(path).Revisions);
        Assert.Equal("insert", revision.Kind);
        Assert.Equal("An Editor", revision.Author);
        Assert.Equal("newly added words", revision.Text);
    }

    [Fact]
    public void Read_Deletion_ReadsDelTextNotT()
    {
        using var dir = new TempDir();
        var path = BuildDocx(dir, Document("""
            <w:p><w:del w:id="2" w:author="An Editor">
              <w:r><w:delText>removed words</w:delText></w:r>
            </w:del></w:p>
            """));

        var revision = Assert.Single(DocxReviewReader.Read(path).Revisions);
        Assert.Equal("delete", revision.Kind);
        Assert.Equal("removed words", revision.Text);
    }

    [Fact]
    public void Read_EmptyRevisionsAreSkipped()
    {
        using var dir = new TempDir();
        // A formatting-only revision carries no text and is not a content change.
        var path = BuildDocx(dir, Document("""<w:p><w:ins w:author="E"><w:r/></w:ins></w:p>"""));

        Assert.Empty(DocxReviewReader.Read(path).Revisions);
    }

    [Fact]
    public void Read_MixedRevisions_KeepDocumentOrder()
    {
        using var dir = new TempDir();
        var path = BuildDocx(dir, Document("""
            <w:p>
              <w:ins w:author="E"><w:r><w:t>first</w:t></w:r></w:ins>
              <w:del w:author="E"><w:r><w:delText>second</w:delText></w:r></w:del>
              <w:ins w:author="E"><w:r><w:t>third</w:t></w:r></w:ins>
            </w:p>
            """));

        var revisions = DocxReviewReader.Read(path).Revisions;

        Assert.Equal(3, revisions.Count);
        Assert.Equal(["first", "second", "third"], revisions.Select(r => r.Text));
        Assert.Equal(["insert", "delete", "insert"], revisions.Select(r => r.Kind));
    }

    [Fact]
    public void Read_CommentsAndRevisionsTogether()
    {
        using var dir = new TempDir();
        var path = BuildDocx(
            dir,
            Document("""
                <w:p>
                  <w:commentRangeStart w:id="1"/>
                  <w:r><w:t>anchored</w:t></w:r>
                  <w:commentRangeEnd w:id="1"/>
                  <w:ins w:author="E"><w:r><w:t> plus</w:t></w:r></w:ins>
                </w:p>
                """),
            Comments("""<w:comment w:id="1" w:author="E"><w:p><w:r><w:t>Note</w:t></w:r></w:p></w:comment>"""));

        var review = DocxReviewReader.Read(path);

        Assert.False(review.IsEmpty);
        Assert.Single(review.Comments);
        Assert.Single(review.Revisions);
    }

    [Fact]
    public void Read_FromStream_MatchesReadFromPath()
    {
        using var dir = new TempDir();
        var path = BuildDocx(dir, Document("""<w:p><w:ins w:author="E"><w:r><w:t>x</w:t></w:r></w:ins></w:p>"""));

        using var stream = File.OpenRead(path);
        Assert.Single(DocxReviewReader.Read(stream).Revisions);
    }

    [Fact]
    public void Read_LockedFile_IsEmptyNotAnError()
    {
        using var dir = new TempDir();
        var path = BuildDocx(dir, Document("<w:p><w:r><w:t>x</w:t></w:r></w:p>"));

        // Another process holding the file exclusively is an ordinary situation
        // when the editor still has it open in Word.
        using var hold = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);

        Assert.True(DocxReviewReader.Read(path).IsEmpty);
    }
}
