using System.IO.Compression;
using System.Text;
using Novalist.Backend;
using Novalist.Backend.Rpc;
using Xunit;

namespace Novalist.Backend.Tests;

/// <summary>Bringing an editor's marked-up Word file back into the project.</summary>
public sealed class ReviewRpcTests : IDisposable
{
    private const string W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private readonly string _root;
    private readonly Workspace _workspace;
    private readonly ReviewRpc _rpc;

    public ReviewRpcTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "nl-review-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _workspace = new Workspace(Path.Combine(_root, "settings"));
        _workspace.Projects.CreateProjectAsync(_root, "ReviewNovel", "Book").GetAwaiter().GetResult();
        _workspace.OpenProjectAsync(_workspace.Projects.ProjectRoot!).GetAwaiter().GetResult();
        _rpc = new ReviewRpc(_workspace);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private string BuildDocx(string body, string? comments = null)
    {
        var path = Path.Combine(_root, "edited.docx");
        using var stream = File.Create(path);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create);

        WriteEntry(zip, "word/document.xml",
            $"""<?xml version="1.0"?><w:document xmlns:w="{W}"><w:body>{body}</w:body></w:document>""");
        if (comments != null)
            WriteEntry(zip, "word/comments.xml",
                $"""<?xml version="1.0"?><w:comments xmlns:w="{W}">{comments}</w:comments>""");
        return path;
    }

    private static void WriteEntry(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name);
        using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
        writer.Write(content);
    }

    [Fact]
    public async Task Read_ReturnsCommentsAndRevisions()
    {
        var path = BuildDocx(
            """
            <w:p>
              <w:commentRangeStart w:id="0"/><w:r><w:t>flagged</w:t></w:r><w:commentRangeEnd w:id="0"/>
              <w:ins w:author="Ed"><w:r><w:t>added</w:t></w:r></w:ins>
              <w:del w:author="Ed"><w:r><w:delText>cut</w:delText></w:r></w:del>
            </w:p>
            """,
            """<w:comment w:id="0" w:author="Ed"><w:p><w:r><w:t>Tighten.</w:t></w:r></w:p></w:comment>""");

        var review = await _rpc.ReadAsync(path);

        var comment = Assert.Single(review.Comments);
        Assert.Equal("Ed", comment.Author);
        Assert.Equal("Tighten.", comment.Text);
        Assert.Equal("flagged", comment.AnchorText);
        Assert.Equal(2, review.Revisions.Length);
    }

    [Fact]
    public async Task Read_UnreadableFile_IsEmptyNotAnError()
    {
        var review = await _rpc.ReadAsync(Path.Combine(_root, "missing.docx"));

        Assert.Empty(review.Comments);
        Assert.Empty(review.Revisions);
    }

    [Fact]
    public async Task ApplyComments_AddsThemToTheScene()
    {
        var chapter = await _workspace.Projects.CreateChapterAsync("C");
        var scene = await _workspace.Projects.CreateSceneAsync(chapter.Guid, "S");

        var added = await _rpc.ApplyCommentsAsync(chapter.Guid, scene.Id,
        [
            new ReviewCommentDto("0", "Ed", "", "Tighten this.", "flagged"),
            new ReviewCommentDto("1", "", "", "No author.", "other")
        ]);

        Assert.Equal(2, added);

        var saved = _workspace.Projects.GetScenesForChapter(chapter.Guid).First(s => s.Id == scene.Id);
        Assert.Equal(2, saved.Comments!.Count);
        // The editor's name rides in the text, since SceneComment has no author field.
        Assert.Contains("Ed: Tighten this.", saved.Comments.Select(c => c.Text));
        Assert.Contains("No author.", saved.Comments.Select(c => c.Text));
        Assert.Contains("flagged", saved.Comments.Select(c => c.AnchorText));
    }

    [Fact]
    public async Task ApplyComments_SkipsBlankText()
    {
        var chapter = await _workspace.Projects.CreateChapterAsync("C");
        var scene = await _workspace.Projects.CreateSceneAsync(chapter.Guid, "S");

        var added = await _rpc.ApplyCommentsAsync(chapter.Guid, scene.Id,
            [new ReviewCommentDto("0", "Ed", "", "   ", "x")]);

        Assert.Equal(0, added);
        var saved = _workspace.Projects.GetScenesForChapter(chapter.Guid).First(s => s.Id == scene.Id);
        Assert.True(saved.Comments == null || saved.Comments.Count == 0);
    }

    [Fact]
    public async Task ApplyComments_EmptyList_ChangesNothing()
    {
        var chapter = await _workspace.Projects.CreateChapterAsync("C");
        var scene = await _workspace.Projects.CreateSceneAsync(chapter.Guid, "S");

        Assert.Equal(0, await _rpc.ApplyCommentsAsync(chapter.Guid, scene.Id, []));
    }

    [Fact]
    public async Task ApplyComments_KeepsExistingComments()
    {
        var chapter = await _workspace.Projects.CreateChapterAsync("C");
        var scene = await _workspace.Projects.CreateSceneAsync(chapter.Guid, "S");
        scene.Comments = [new Core.Models.SceneComment { Text = "Mine", AnchorText = "a" }];

        await _rpc.ApplyCommentsAsync(chapter.Guid, scene.Id,
            [new ReviewCommentDto("0", "Ed", "", "Theirs", "b")]);

        var saved = _workspace.Projects.GetScenesForChapter(chapter.Guid).First(s => s.Id == scene.Id);
        Assert.Equal(2, saved.Comments!.Count);
        Assert.Contains("Mine", saved.Comments.Select(c => c.Text));
    }
}
