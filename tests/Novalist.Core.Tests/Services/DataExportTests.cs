using Novalist.Core.Services;
using Xunit;

namespace Novalist.Core.Tests.Services;

/// <summary>
/// Machine-readable metadata export.
///
/// Every other format Novalist writes is prose or a document, so an outline
/// could only reach a spreadsheet or another tool by being retyped.
/// </summary>
public class CsvTests
{
    [Theory]
    [InlineData("Mira", "Mira")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void PlainTextIsWrittenPlainly(string? value, string expected)
        => Assert.Equal(expected, Csv.Cell(value));

    [Theory]
    [InlineData("Then, later", "\"Then, later\"")]
    [InlineData("She said \"no\"", "\"She said \"\"no\"\"\"")]
    [InlineData("first\nsecond", "\"first\nsecond\"")]
    [InlineData("first\rsecond", "\"first\rsecond\"")]
    public void AnythingThatWouldBreakTheColumnsIsQuoted(string value, string expected)
    {
        // A synopsis with a comma in it is ordinary content here, and it
        // silently shifts every column after it in a naive join.
        Assert.Equal(expected, Csv.Cell(value));
    }

    [Fact]
    public void ARowEndsTheWayEveryReaderAccepts()
        => Assert.Equal("a,b\r\n", Csv.Row(["a", "b"]));

    [Fact]
    public void ASheetLeadsWithItsHeader()
    {
        var sheet = Csv.Sheet(["One", "Two"], [["a", "b"], ["c", "d"]]);

        Assert.Equal("One,Two\r\na,b\r\nc,d\r\n", sheet);
    }

    [Fact]
    public void ASheetWithNoRowsIsStillReadable()
    {
        // A header alone opens as an empty table rather than as a broken file.
        Assert.Equal("One,Two\r\n", Csv.Sheet(["One", "Two"], []));
    }
}

/// <summary>What the two machine-readable formats actually write.</summary>
public class MetadataWriterTests
{
    private static SceneMetadataRow Row() => new()
    {
        Chapter = "The Rookery",
        ChapterOrder = 1,
        Scene = "Then, later",
        SceneOrder = 2,
        Stage = "Draft",
        Pov = "Mira",
        Words = 1200,
        WordTarget = 1500,
        Date = "1847-03-02",
        Synopsis = "She finds the deed.",
        Goal = "Get inside.",
        Conflict = "The steward.",
        Outcome = "Thrown out.",
        Tags = "rain; night",
        Plotlines = "The inheritance",
        Cast = "Mira; Tomas"
    };

    [Fact]
    public void EveryColumnHasAHeading()
    {
        // A scene whose fields hold no commas, so counting them is honest -
        // a quoted cell contains separators of its own.
        var plain = Row();
        plain.Scene = "Then later";
        var sheet = MetadataWriter.SceneCsv([plain]);
        var lines = sheet.Split("\r\n");

        // A column with no heading is a column nobody can read, and the two
        // lists drifting apart is the way that happens.
        Assert.Equal(MetadataWriter.SceneColumns.Length, lines[0].Split(',').Length);
        Assert.Equal(MetadataWriter.SceneColumns.Length, lines[1].Split(',').Length);
    }

    [Fact]
    public void ASceneBecomesARow()
    {
        var sheet = MetadataWriter.SceneCsv([Row()]);

        Assert.Contains("The Rookery,1,\"Then, later\",2,Draft,Mira,1200,1500", sheet);
        Assert.Contains("She finds the deed.", sheet);
        Assert.Contains("rain; night", sheet);
    }

    [Fact]
    public void TheFlagsAreSpelledOut()
    {
        var parked = Row();
        parked.Inactive = true;
        parked.ExcludedFromExport = false;

        // A sheet a person reads should say what the column means without
        // needing a legend beside it.
        Assert.EndsWith("yes,no\r\n", MetadataWriter.SceneCsv([parked]));
    }

    [Fact]
    public void NumbersDoNotFollowTheMachinesLocale()
    {
        var row = Row();
        row.Words = 1200;

        // A German machine writes 1.200 for this, which a spreadsheet reads as
        // 1.2 and a parser rejects.
        Assert.Contains(",1200,", MetadataWriter.SceneCsv([row]));
    }

    [Fact]
    public void TheJsonCarriesScenesAndCodexTogether()
    {
        var export = new MetadataExport
        {
            Title = "Salt Road",
            Author = "D. G.",
            Scenes = [Row()],
            Codex =
            [
                new EntityMetadataRow
                {
                    Kind = "character",
                    Name = "Mira",
                    Properties = { ["Role"] = "Protagonist" },
                    Sections = { ["Appearance"] = "Tall." },
                    Relationships = { "sister: Tomas" }
                }
            ]
        };

        var json = MetadataWriter.Json(export);

        Assert.Contains("\"title\": \"Salt Road\"", json);
        Assert.Contains("\"scenes\"", json);
        Assert.Contains("\"Role\": \"Protagonist\"", json);
        Assert.Contains("\"sister: Tomas\"", json);
    }

    [Fact]
    public void AccentsSurviveReadable()
    {
        var export = new MetadataExport { Title = "Für Elise" };

        // Escaping every accented letter would make a German outline
        // unreadable by hand, and this is a file people open in an editor.
        Assert.Contains("Für Elise", MetadataWriter.Json(export));
    }

    [Fact]
    public void AnEmptyExportIsStillValidJson()
    {
        var json = MetadataWriter.Json(new MetadataExport());

        Assert.Contains("\"scenes\": []", json);
        Assert.Contains("\"codex\": []", json);
    }
}

public class CodexCsvTests
{
    [Fact]
    public void OneRowPerFieldRatherThanOneColumnPerField()
    {
        var codex = new List<EntityMetadataRow>
        {
            new()
            {
                Kind = "Character", Name = "Mira",
                Properties = new Dictionary<string, string> { ["Role"] = "Protagonist" },
                Sections = new Dictionary<string, string> { ["History"] = "Born north." },
                Relationships = ["sister: Tomas"]
            }
        };

        var sheet = MetadataWriter.CodexCsv(codex);

        Assert.StartsWith("Kind,Name,Field,Value", sheet);
        Assert.Contains("Character,Mira,Role,Protagonist", sheet);
        Assert.Contains("Character,Mira,History,Born north.", sheet);
        Assert.Contains("Character,Mira,Relationship,sister: Tomas", sheet);
    }

    [Fact]
    public void AnEmptyCodexIsStillAHeaderRow()
        => Assert.StartsWith("Kind,Name,Field,Value", MetadataWriter.CodexCsv([]));
}

/// <summary>
/// The outline as OPML - what every outliner reads. A sheet can be pivoted but
/// it cannot carry a shape.
/// </summary>
public class OpmlTests
{
    private static SceneMetadataRow Scene(string chapter, int order, string scene, string synopsis = "")
        => new() { Chapter = chapter, ChapterOrder = order, Scene = scene, Synopsis = synopsis };

    [Fact]
    public void ChaptersBranchAndScenesLeaf()
    {
        var opml = MetadataWriter.Opml(new MetadataExport
        {
            Title = "Salt Road",
            Scenes =
            [
                Scene("One", 0, "Arrival", "She gets off the train."),
                Scene("One", 0, "The room"),
                Scene("Two", 1, "The gate")
            ]
        });

        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"UTF-8\"?>", opml);
        Assert.Contains("<title>Salt Road</title>", opml);
        Assert.Contains("<outline text=\"One\">", opml);
        Assert.Contains("<outline text=\"Arrival\" _note=\"She gets off the train.\" />", opml);
        // No synopsis is no note, rather than an empty one.
        Assert.Contains("<outline text=\"The room\" />", opml);
        Assert.Contains("<outline text=\"Two\">", opml);
        // Two chapters opened, two closed - scenes are self-closing leaves.
        Assert.Equal(2, opml.Split("</outline>").Length - 1);
    }

    [Fact]
    public void TwoChaptersWithTheSameNameStaySeparate()
    {
        var opml = MetadataWriter.Opml(new MetadataExport
        {
            Scenes = [Scene("Interlude", 0, "A"), Scene("Interlude", 3, "B")]
        });

        // Merging them would silently reorder the book.
        Assert.Equal(2, opml.Split("<outline text=\"Interlude\">").Length - 1);
    }

    [Fact]
    public void ProseThatWouldBreakTheFileIsEscaped()
    {
        var opml = MetadataWriter.Opml(new MetadataExport
        {
            Title = "Tom & Jerry",
            Scenes = [Scene("<Act>", 0, "She said \"no\"", "A line\nand another")]
        });

        Assert.Contains("<title>Tom &amp; Jerry</title>", opml);
        Assert.Contains("text=\"&lt;Act&gt;\"", opml);
        Assert.Contains("text=\"She said &quot;no&quot;\"", opml);
        // An attribute cannot hold a newline, and a raw one makes the file
        // invalid rather than merely ugly.
        Assert.Contains("_note=\"A line and another\"", opml);
    }

    [Fact]
    public void AnEmptyBookIsStillAValidDocument()
    {
        var opml = MetadataWriter.Opml(new MetadataExport());

        Assert.Contains("<body>", opml);
        Assert.EndsWith("</body>\n</opml>\n", opml);
    }
}
