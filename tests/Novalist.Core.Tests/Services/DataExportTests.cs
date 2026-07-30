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
