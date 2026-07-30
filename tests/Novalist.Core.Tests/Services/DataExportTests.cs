using NSubstitute;
using Novalist.Core.Models;
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

/// <summary>
/// The half that was missing: nothing ever filled a <see cref="MetadataExport"/>,
/// so the writers above could not be reached from the app at all.
/// </summary>
public class MetadataCollectorTests
{
    private static (MetadataCollector Sut, IProjectService Projects, IEntityService Entities) Build()
    {
        var projects = Substitute.For<IProjectService>();
        var entities = Substitute.For<IEntityService>();
        projects.ProjectSettings.Returns(new ProjectSettings { Author = "Ada" });
        projects.ActiveBook.Returns(new BookData { Name = "Salt Road" });
        projects.GetChaptersOrdered().Returns([]);
        entities.LoadCharactersAsync().Returns([]);
        entities.LoadLocationsAsync().Returns([]);
        entities.LoadItemsAsync().Returns([]);
        entities.LoadLoreAsync().Returns([]);
        entities.GetCustomEntityTypes().Returns([]);
        return (new MetadataCollector(projects, entities), projects, entities);
    }

    [Fact]
    public async Task CarriesTheBookAndEverySceneColumn()
    {
        var (sut, projects, _) = Build();
        var chapter = new ChapterData { Guid = "c1", Title = "One", Order = 0 };
        projects.GetChaptersOrdered().Returns([chapter]);
        projects.GetScenesForChapter("c1").Returns([
            new SceneData
            {
                Id = "s1", Title = "Opening", Order = 2, Stage = "revised",
                WordCount = 900, WordTarget = 1200, Date = "2026-01-01",
                Synopsis = "She leaves", Goal = "Get out", Outcome = "She does",
                Inactive = true, ExcludeFromExport = true,
                AnalysisOverrides = new SceneAnalysisOverrides
                {
                    Pov = "Mira", Conflict = "The gate", Tags = ["cold", "night"]
                }
            }
        ]);

        var export = await sut.CollectAsync();

        Assert.Equal("Salt Road", export.Title);
        Assert.Equal("Ada", export.Author);
        var row = Assert.Single(export.Scenes);
        Assert.Equal("One", row.Chapter);
        Assert.Equal("Opening", row.Scene);
        Assert.Equal(2, row.SceneOrder);
        Assert.Equal("revised", row.Stage);
        Assert.Equal("Mira", row.Pov);
        Assert.Equal(900, row.Words);
        Assert.Equal(1200, row.WordTarget);
        Assert.Equal("2026-01-01", row.Date);
        Assert.Equal("She leaves", row.Synopsis);
        Assert.Equal("Get out", row.Goal);
        Assert.Equal("The gate", row.Conflict);
        Assert.Equal("She does", row.Outcome);
        Assert.Equal("cold, night", row.Tags);
        Assert.True(row.Inactive);
        Assert.True(row.ExcludedFromExport);
    }

    [Fact]
    public async Task ResolvesCastAndThreadIdsToNames()
    {
        var (_, projects, entities) = Build();
        var plotlines = Substitute.For<IPlotlineService>();
        plotlines.GetPlotlines().Returns([new PlotlineData { Id = "p1", Name = "The debt" }]);
        entities.LoadCharactersAsync().Returns([new CharacterData { Id = "e1", Name = "Mira" }]);
        var chapter = new ChapterData { Guid = "c1", Title = "One" };
        projects.GetChaptersOrdered().Returns([chapter]);
        projects.GetScenesForChapter("c1").Returns([
            // "gone" is an entry deleted from the Codex: the id is written
            // rather than dropped, so the row says something is missing.
            new SceneData { Id = "s1", Cast = ["e1", "gone"], PlotlineIds = ["p1"] }
        ]);

        var export = await new MetadataCollector(projects, entities, plotlines).CollectAsync();

        var row = Assert.Single(export.Scenes);
        Assert.Equal("Mira, gone", row.Cast);
        Assert.Equal("The debt", row.Plotlines);
    }

    [Fact]
    public async Task FlattensEveryKindOfEntryIncludingTheWritersOwn()
    {
        var (_, projects, entities) = Build();
        entities.LoadCharactersAsync().Returns([
            new CharacterData
            {
                Name = "Mira", Surname = "Frost", Role = "Protagonist", EyeColor = "grey",
                Sections = [new EntitySection { Title = "History", Content = "Born north." }],
                Relationships = [new EntityRelationship { Role = "sister", Target = "Tomas" }],
                CustomProperties = new Dictionary<string, string> { ["Scar"] = "left hand" }
            }
        ]);
        entities.LoadLocationsAsync().Returns([
            new LocationData { Name = "Deepforge", Type = "Fortress", Description = "Cut into rock." }
        ]);
        entities.LoadItemsAsync().Returns([new ItemData { Name = "The Ring", Type = "Relic" }]);
        entities.LoadLoreAsync().Returns([new LoreData { Name = "The Pact", Category = "Law" }]);
        entities.GetCustomEntityTypes().Returns([
            new CustomEntityTypeDefinition { TypeKey = "faction", DisplayName = "Faction" }
        ]);
        entities.LoadCustomEntitiesAsync("faction").Returns([new CustomEntityData { Name = "Nightwatch" }]);

        var export = await new MetadataCollector(projects, entities).CollectAsync();

        var mira = export.Codex.Single(e => e.Name == "Mira Frost");
        Assert.Equal("Character", mira.Kind);
        Assert.Equal("Protagonist", mira.Properties["Role"]);
        Assert.Equal("grey", mira.Properties["Eyes"]);
        Assert.Equal("left hand", mira.Properties["Scar"]);
        Assert.Equal("Born north.", mira.Sections["History"]);
        Assert.Equal("sister: Tomas", Assert.Single(mira.Relationships));
        // An empty field is left out rather than written as a blank column.
        Assert.False(mira.Properties.ContainsKey("Build"));

        var place = export.Codex.Single(e => e.Name == "Deepforge");
        Assert.Equal("Location", place.Kind);
        Assert.Equal("Fortress", place.Properties["Type"]);
        Assert.Equal("Cut into rock.", place.Properties["Description"]);
        Assert.Equal("Relic", export.Codex.Single(e => e.Name == "The Ring").Properties["Type"]);

        // Lore calls its kind a category, and the column says so.
        Assert.Equal("Law", export.Codex.Single(e => e.Name == "The Pact").Properties["Category"]);
        Assert.Equal("Faction", export.Codex.Single(e => e.Name == "Nightwatch").Kind);
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
