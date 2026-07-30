using Novalist.Core.Services;
using Novalist.Core.Tests.TestHelpers;
using Xunit;

namespace Novalist.Core.Tests.Services;

/// <summary>
/// Reading a folder of Markdown files.
///
/// Novalist imported one thing: a vault made by the old Obsidian plugin, with
/// its own metadata files. A folder of ordinary notes - which is what a vault
/// is once the plugin is gone, and what every other tool exports - had no way
/// in at all.
/// </summary>
public class MarkdownVaultImportTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    private void Write(string relativePath, string text)
    {
        var path = Path.Combine(_dir.Path, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    // ─── Reading one note ────────────────────────────────────────────

    [Fact]
    public void TheFrontMatterTitleWins()
    {
        var note = MarkdownVaultImport.Read("notes/thing.md",
            "---\ntitle: The Salt Road\ntags: [trade, roads]\n---\n# Something else\n\nProse.");

        Assert.Equal("The Salt Road", note.Title);
        Assert.Contains("trade", note.Tags);
        Assert.Contains("roads", note.Tags);
        // The front matter is metadata, not prose, so it does not land in the
        // note a writer reads.
        Assert.DoesNotContain("title:", note.Body);
        Assert.StartsWith("# Something else", note.Body);
    }

    [Fact]
    public void WithNoFrontMatterTheFirstHeadingIsTheTitle()
    {
        var note = MarkdownVaultImport.Read("thing.md", "## How the economy works\n\nSalt.");

        Assert.Equal("How the economy works", note.Title);
    }

    [Fact]
    public void WithNeitherTheFileNameIsTheTitle()
    {
        var note = MarkdownVaultImport.Read("notes/The Salt Road.md", "Just prose.");

        // A note is never nameless: an untitled row in a list of four hundred
        // is a note nobody will ever open.
        Assert.Equal("The Salt Road", note.Title);
    }

    [Fact]
    public void TheFoldersANoteWasFiledInBecomeTags()
    {
        var note = MarkdownVaultImport.Read("World/Places/Hillsford.md", "Prose.");

        // The folders are the only classification the writer actually made,
        // so they survive rather than being lost on the way in.
        Assert.Contains("World", note.Tags);
        Assert.Contains("Places", note.Tags);
    }

    [Theory]
    [InlineData("tags: [trade, roads]")]
    [InlineData("tags: trade, roads")]
    [InlineData("tags: #trade #roads")]
    public void TagsAreReadHoweverTheyWereWritten(string line)
    {
        var note = MarkdownVaultImport.Read("thing.md", $"---\n{line}\n---\nProse.");

        Assert.Contains("trade", note.Tags);
        Assert.Contains("roads", note.Tags);
    }

    [Fact]
    public void ATagInBothPlacesIsOneTag()
    {
        var note = MarkdownVaultImport.Read("Trade/thing.md", "---\ntags: [trade]\n---\nProse.");

        Assert.Single(note.Tags);
    }

    [Fact]
    public void AnEmptyFileIsStillANote()
    {
        var note = MarkdownVaultImport.Read("empty.md", string.Empty);

        Assert.Equal("empty", note.Title);
        Assert.Equal(string.Empty, note.Body);
    }

    [Fact]
    public void FrontMatterThatIsNotKeyedIsIgnoredRatherThanFatal()
    {
        var note = MarkdownVaultImport.Read("thing.md", "---\njust a line\n---\nProse.");

        Assert.Equal("thing", note.Title);
        Assert.Equal("Prose.", note.Body);
    }

    // ─── Reading a folder ────────────────────────────────────────────

    [Fact]
    public void EveryMarkdownFileIsFound()
    {
        Write("one.md", "# One");
        Write("deep/two.md", "# Two");
        Write("notes.txt", "not markdown");

        var notes = MarkdownVaultImport.Scan(_dir.Path);

        Assert.Equal(2, notes.Count);
        Assert.Contains(notes, n => n.Title == "One");
        Assert.Contains(notes, n => n.Title == "Two");
    }

    [Fact]
    public void AToolsOwnStateIsNotTheWritersNotes()
    {
        Write("real.md", "# Real");
        Write(".obsidian/plugins/thing/data.md", "# Config");
        Write(".git/notes.md", "# Git");

        // Importing a plugin's own config as a research note is worse than
        // useless: it fills the list with things nobody wrote.
        Assert.Equal("Real", Assert.Single(MarkdownVaultImport.Scan(_dir.Path)).Title);
    }

    [Fact]
    public void AFolderThatIsNotThereIsNoNotes()
    {
        Assert.Empty(MarkdownVaultImport.Scan(Path.Combine(_dir.Path, "nope")));
        Assert.Empty(MarkdownVaultImport.Scan("  "));
    }

    [Fact]
    public void NotesComeBackInAStableOrder()
    {
        Write("b.md", "# B");
        Write("a.md", "# A");

        Assert.Equal(["A", "B"], MarkdownVaultImport.Scan(_dir.Path).Select(n => n.Title));
    }

    [Theory]
    [InlineData(".obsidian/x.md", true)]
    [InlineData("notes/.trash/x.md", true)]
    [InlineData("notes/x.md", false)]
    [InlineData("obsidian-notes/x.md", false)]
    public void OnlyAWholeFolderNameIsSkipped(string path, bool skipped)
        => Assert.Equal(skipped, MarkdownVaultImport.IsSkipped(path));

    [Fact]
    public void AFileSomethingElseIsHoldingIsSkippedRatherThanFatal()
    {
        Write("readable.md", "# Readable");
        Write("locked.md", "# Locked");
        var locked = Path.Combine(_dir.Path, "locked.md");

        using var holder = new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None);

        // A vault is a live folder. One file open in another program should not
        // fail the import of the four hundred beside it.
        var notes = MarkdownVaultImport.Scan(_dir.Path);

        Assert.Equal("Readable", Assert.Single(notes).Title);
    }
}
