using System.Text;
using Novalist.Core.Services;
using Novalist.Core.Tests.TestHelpers;
using Xunit;

namespace Novalist.Core.Tests.Services;

/// <summary>
/// Reading a Scrivener project. Both layouts are in the wild - Scrivener 2
/// numbers its documents, Scrivener 3 gives each a UUID folder - so both are
/// built here from real file structures rather than mocked.
/// </summary>
public class ScrivenerReaderTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    /// <summary>Minimal RTF carrying one paragraph, which is what Scrivener
    /// writes for a document.</summary>
    private static string Rtf(string text)
        => "{\\rtf1\\ansi\\deff0{\\fonttbl{\\f0 Times;}}\\f0\\fs24 " + text + "\\par}";

    private string NewProject(string name = "Book.scriv")
    {
        var root = Path.Combine(_dir.Path, name);
        Directory.CreateDirectory(root);
        return root;
    }

    private static void WriteScrivx(string root, string binderXml)
        => File.WriteAllText(
            Path.Combine(root, "Book.scrivx"),
            $"<?xml version=\"1.0\"?><ScrivenerProject><Binder>{binderXml}</Binder></ScrivenerProject>",
            Encoding.UTF8);

    /// <summary>Scrivener 3: a UUID folder per document.</summary>
    private static void WriteDoc3(string root, string uuid, string text, string? synopsis = null)
    {
        var folder = Path.Combine(root, "Files", "Data", uuid);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "content.rtf"), Rtf(text));
        if (synopsis != null) File.WriteAllText(Path.Combine(folder, "synopsis.txt"), synopsis);
    }

    /// <summary>Scrivener 2: a numbered file under Files/Docs.</summary>
    private static void WriteDoc2(string root, string id, string text)
    {
        var folder = Path.Combine(root, "Files", "Docs");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, id + ".rtf"), Rtf(text));
    }

    // ── Recognition ──

    [Fact]
    public void AProjectFolderIsRecognised()
    {
        Assert.True(ScrivenerReader.LooksLikeScrivener(NewProject()));
    }

    [Fact]
    public void TheScrivxInsideIsRecognisedToo()
    {
        // A file picker gives one or the other depending on the platform.
        var root = NewProject();
        WriteScrivx(root, "");

        Assert.True(ScrivenerReader.LooksLikeScrivener(Path.Combine(root, "Book.scrivx")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void NothingIsNotAProject(string path)
    {
        Assert.False(ScrivenerReader.LooksLikeScrivener(path));
    }

    [Fact]
    public void AnOrdinaryFolderIsNotAProject()
    {
        Assert.False(ScrivenerReader.LooksLikeScrivener(_dir.Path));
    }

    [Fact]
    public void AWordFileIsNotAProject()
    {
        Assert.False(ScrivenerReader.LooksLikeScrivener("book.docx"));
    }

    // ── Scrivener 3 ──

    [Fact]
    public void AScrivener3BinderBecomesChaptersAndScenes()
    {
        var root = NewProject();
        WriteScrivx(root, """
            <BinderItem UUID="F1" Type="Folder"><Title>Chapter One</Title><Children>
              <BinderItem UUID="D1" Type="Text"><Title>Arrival</Title></BinderItem>
              <BinderItem UUID="D2" Type="Text"><Title>The Inn</Title></BinderItem>
            </Children></BinderItem>
            """);
        WriteDoc3(root, "D1", "She arrived at dusk.");
        WriteDoc3(root, "D2", "The inn was full.");

        var project = ScrivenerReader.Read(root);

        Assert.Equal("3", project.Version);
        Assert.Equal(["Arrival", "The Inn"], project.Scenes.Select(s => s.Title));
        Assert.All(project.Scenes, s => Assert.Equal("Chapter One", s.ChapterTitle));
        Assert.Equal("She arrived at dusk.", project.Scenes[0].Text);
    }

    [Fact]
    public void ASynopsisCardComesAcross()
    {
        // The one piece of Scrivener metadata Novalist has an exact home for.
        var root = NewProject();
        WriteScrivx(root, """
            <BinderItem UUID="F1" Type="Folder"><Title>One</Title><Children>
              <BinderItem UUID="D1" Type="Text"><Title>Arrival</Title></BinderItem>
            </Children></BinderItem>
            """);
        WriteDoc3(root, "D1", "Prose.", synopsis: "She arrives and everything changes.");

        Assert.Equal(
            "She arrives and everything changes.",
            ScrivenerReader.Read(root).Scenes.Single().Synopsis);
    }

    [Fact]
    public void ADeeperBinderIsFlattenedIntoItsOutermostFolder()
    {
        // Scrivener nests arbitrarily; Novalist is two levels. Losing nesting
        // is the right trade against losing text.
        var root = NewProject();
        WriteScrivx(root, """
            <BinderItem UUID="F1" Type="Folder"><Title>Act One</Title><Children>
              <BinderItem UUID="F2" Type="Folder"><Title>Chapter One</Title><Children>
                <BinderItem UUID="D1" Type="Text"><Title>Deep</Title></BinderItem>
              </Children></BinderItem>
            </Children></BinderItem>
            """);
        WriteDoc3(root, "D1", "Text.");

        var scene = ScrivenerReader.Read(root).Scenes.Single();

        Assert.Equal("Act One", scene.ChapterTitle);
        Assert.Equal("Deep", scene.Title);
    }

    [Fact]
    public void AFolderCarryingTextOfItsOwnBecomesASceneToo()
    {
        // A chapter folder with an epigraph in it.
        var root = NewProject();
        WriteScrivx(root, """
            <BinderItem UUID="F1" Type="Folder"><Title>One</Title><Children>
              <BinderItem UUID="D1" Type="Text"><Title>Scene</Title></BinderItem>
            </Children></BinderItem>
            """);
        WriteDoc3(root, "F1", "An epigraph.");
        WriteDoc3(root, "D1", "The scene.");

        var project = ScrivenerReader.Read(root);

        Assert.Equal(2, project.Scenes.Count);
        Assert.Contains(project.Scenes, s => s.Text == "An epigraph.");
    }

    [Fact]
    public void AnEmptyPlaceholderDocumentIsSkipped()
    {
        var root = NewProject();
        WriteScrivx(root, """
            <BinderItem UUID="F1" Type="Folder"><Title>One</Title><Children>
              <BinderItem UUID="D1" Type="Text"><Title>Nothing here yet</Title></BinderItem>
            </Children></BinderItem>
            """);

        Assert.Empty(ScrivenerReader.Read(root).Scenes);
    }

    [Fact]
    public void ADocumentOutsideAnyFolderStillLands()
    {
        var root = NewProject();
        WriteScrivx(root, "<BinderItem UUID=\"D1\" Type=\"Text\"><Title>Loose</Title></BinderItem>");
        WriteDoc3(root, "D1", "Text.");

        Assert.Equal("Imported", ScrivenerReader.Read(root).Scenes.Single().ChapterTitle);
    }

    // ── Scrivener 2 ──

    [Fact]
    public void AScrivener2ProjectReadsFromItsNumberedDocuments()
    {
        var root = NewProject();
        WriteScrivx(root, """
            <BinderItem ID="10" Type="Folder"><Title>Chapter One</Title><Children>
              <BinderItem ID="11" Type="Text"><Title>Arrival</Title></BinderItem>
            </Children></BinderItem>
            """);
        WriteDoc2(root, "11", "Old project prose.");

        var project = ScrivenerReader.Read(root);

        Assert.Equal("2", project.Version);
        Assert.Equal("Old project prose.", project.Scenes.Single().Text);
    }

    // ── What is left behind ──

    [Fact]
    public void ScrivenersOwnFoldersAreReportedRatherThanImported()
    {
        var root = NewProject();
        WriteScrivx(root, """
            <BinderItem UUID="F1" Type="Folder"><Title>Chapter One</Title><Children>
              <BinderItem UUID="D1" Type="Text"><Title>Scene</Title></BinderItem>
            </Children></BinderItem>
            <BinderItem UUID="R1" Type="Folder"><Title>Research</Title><Children>
              <BinderItem UUID="D2" Type="Text"><Title>Notes</Title></BinderItem>
            </Children></BinderItem>
            <BinderItem UUID="T1" Type="Folder"><Title>Trash</Title></BinderItem>
            """);
        WriteDoc3(root, "D1", "Kept.");
        WriteDoc3(root, "D2", "Research notes.");

        var project = ScrivenerReader.Read(root);

        Assert.Equal(["Scene"], project.Scenes.Select(s => s.Title));
        Assert.Contains("Research", project.Losses);
        Assert.Contains("Trash", project.Losses);
    }

    // ── Unreadable input ──

    [Fact]
    public void AFolderThatIsNotAProjectReadsAsEmpty()
    {
        Assert.True(ScrivenerReader.Read(_dir.Path).IsEmpty);
    }

    [Fact]
    public void AProjectWithNoScrivxReadsAsEmpty()
    {
        Assert.True(ScrivenerReader.Read(NewProject()).IsEmpty);
    }

    [Fact]
    public void AMalformedScrivxReadsAsEmptyRatherThanThrowing()
    {
        // An import that cannot start should say so in the dialog, not crash.
        var root = NewProject();
        File.WriteAllText(Path.Combine(root, "Book.scrivx"), "<not xml at all");

        Assert.True(ScrivenerReader.Read(root).IsEmpty);
    }

    [Fact]
    public void AScrivxWithNoBinderReadsAsEmpty()
    {
        var root = NewProject();
        File.WriteAllText(Path.Combine(root, "Book.scrivx"), "<?xml version=\"1.0\"?><Project/>");

        Assert.True(ScrivenerReader.Read(root).IsEmpty);
    }

    [Fact]
    public void ADocumentWhoseFileIsMissingContributesNothing()
    {
        var root = NewProject();
        Directory.CreateDirectory(Path.Combine(root, "Files", "Data"));
        WriteScrivx(root, """
            <BinderItem UUID="F1" Type="Folder"><Title>One</Title><Children>
              <BinderItem UUID="GONE" Type="Text"><Title>Missing</Title></BinderItem>
            </Children></BinderItem>
            """);

        Assert.Empty(ScrivenerReader.Read(root).Scenes);
    }

    [Fact]
    public void ADocumentWithNoIdContributesNothing()
    {
        var root = NewProject();
        WriteScrivx(root, """
            <BinderItem Type="Folder"><Title>One</Title><Children>
              <BinderItem Type="Text"><Title>No id</Title></BinderItem>
            </Children></BinderItem>
            """);

        Assert.Empty(ScrivenerReader.Read(root).Scenes);
    }

    [Fact]
    public void ANonexistentPathReadsAsEmpty()
    {
        Assert.True(ScrivenerReader.Read(Path.Combine(_dir.Path, "nope.scriv")).IsEmpty);
    }
}
