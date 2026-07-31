using System.Text;
using NSubstitute;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Core.Tests.TestHelpers;
using Xunit;

namespace Novalist.Core.Tests.Services;

/// <summary>
/// Scene and Codex metadata leaving the project in a form another tool reads.
///
/// The point of this export is the rows a compile throws away: a planning sheet
/// that hides the scenes somebody parked is exactly the sheet that cannot
/// answer why the act came out short.
/// </summary>
public class ExportDataTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly IProjectService _project = Substitute.For<IProjectService>();
    private readonly IEntityService _entities = Substitute.For<IEntityService>();
    private readonly ChapterData _chapter = new() { Title = "The Rookery", Order = 1 };

    public void Dispose() => _dir.Dispose();

    private ExportService Service() => new(_project, _entities);

    private string Output(string name) => Path.Combine(_dir.Path, name);

    /// <summary>One chapter of scenes, and an empty Codex unless a test fills it.</summary>
    private ExportOptions Setup(ExportFormat format, params SceneData[] scenes)
    {
        foreach (var scene in scenes) scene.ChapterGuid = _chapter.Guid;
        _project.GetChaptersOrdered().Returns([_chapter]);
        _project.GetScenesForChapter(_chapter.Guid).Returns([.. scenes]);
        _project.ActiveBook.Returns(new BookData());
        _entities.LoadCharactersAsync().Returns([]);
        _entities.LoadLocationsAsync().Returns([]);
        _entities.LoadItemsAsync().Returns([]);
        _entities.LoadLoreAsync().Returns([]);

        return new ExportOptions
        {
            Format = format,
            Title = "Salt Road",
            Author = "D. G.",
            SelectedChapterGuids = [_chapter.Guid]
        };
    }

    [Fact]
    public async Task ASceneBecomesARowInTheSheet()
    {
        var options = Setup(ExportFormat.Csv, new SceneData
        {
            Title = "The deed",
            Order = 1,
            Stage = "Draft",
            WordCount = 1200,
            WordTarget = 1500,
            Date = "1847-03-02",
            Synopsis = "She finds it.",
            Goal = "Get inside.",
            Outcome = "Thrown out.",
            AnalysisOverrides = new SceneAnalysisOverrides
            {
                Pov = "Mira",
                Conflict = "The steward.",
                Tags = ["rain", "night"]
            }
        });
        var path = Output("outline.csv");

        await Service().ExportDataAsync(options, path);

        var text = await File.ReadAllTextAsync(path);
        Assert.Contains("The Rookery,1,The deed,1,Draft,Mira,1200,1500,1847-03-02", text);
        Assert.Contains("She finds it.", text);
        Assert.Contains("rain; night", text);
    }

    [Fact]
    public async Task ThePathAndFlagsOfAParkedSceneComeThrough()
    {
        var options = Setup(ExportFormat.Csv,
            new SceneData { Title = "In the book", Order = 1 },
            new SceneData { Title = "Parked", Order = 2, Inactive = true },
            new SceneData { Title = "Held back", Order = 3, ExcludeFromExport = true });

        var path = Output("outline.csv");
        await Service().ExportDataAsync(options, path);

        // A compile drops the last two. This is not a compile: they are the
        // rows a planning sheet is for, carrying a flag rather than absent.
        var text = await File.ReadAllTextAsync(path);
        Assert.Contains("Parked", text);
        Assert.Contains("Held back", text);
        Assert.Equal(4, text.TrimEnd('\r', '\n').Split("\r\n").Length);
    }

    [Fact]
    public async Task AStageFilterIsHonoured()
    {
        var options = Setup(ExportFormat.Csv,
            new SceneData { Title = "Drafted", Order = 1, Stage = "Draft" },
            new SceneData { Title = "Revised", Order = 2, Stage = "Revised" });
        options.IncludedStages = ["Revised"];

        var path = Output("outline.csv");
        await Service().ExportDataAsync(options, path);

        // Unlike the in-the-book flags, a stage filter is something the writer
        // asked for on the way out.
        var text = await File.ReadAllTextAsync(path);
        Assert.DoesNotContain("Drafted", text);
        Assert.Contains("Revised", text);
    }

    [Fact]
    public async Task AnEmptyStageFilterMeansEveryStage()
    {
        var options = Setup(ExportFormat.Csv,
            new SceneData { Title = "Drafted", Order = 1, Stage = "Draft" });
        options.IncludedStages = [];

        var path = Output("outline.csv");
        await Service().ExportDataAsync(options, path);

        Assert.Contains("Drafted", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task ChaptersNobodyPickedStayOut()
    {
        var options = Setup(ExportFormat.Csv, new SceneData { Title = "The deed", Order = 1 });
        options.SelectedChapterGuids = [];

        var path = Output("outline.csv");
        await Service().ExportDataAsync(options, path);

        Assert.DoesNotContain("The deed", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task PlotlinesAndCastComeOutAsNames()
    {
        var plotline = new PlotlineData { Name = "The inheritance" };
        var mira = new CharacterData { Name = "Mira", Surname = "Vane" };
        var options = Setup(ExportFormat.Csv, new SceneData
        {
            Title = "The deed",
            Order = 1,
            PlotlineIds = [plotline.Id, "gone"],
            Cast = [mira.Id]
        });
        _project.ActiveBook.Returns(new BookData { Plotlines = [plotline] });
        _entities.LoadCharactersAsync().Returns([mira]);

        var path = Output("outline.csv");
        await Service().ExportDataAsync(options, path);

        // A column of GUIDs is not a readable spreadsheet, which is the whole
        // point of the format. An id nothing resolves stays as itself rather
        // than vanishing, so a stale link is visible instead of silent.
        var text = await File.ReadAllTextAsync(path);
        Assert.Contains("The inheritance; gone", text);
        Assert.Contains("Mira Vane", text);
    }

    [Fact]
    public async Task TheSheetOpensWithItsAccentsIntact()
    {
        var options = Setup(ExportFormat.Csv, new SceneData { Title = "Fürs Erste", Order = 1 });

        var path = Output("outline.csv");
        await Service().ExportDataAsync(options, path);

        // Excel reads a plain UTF-8 CSV as the local codepage and turns every
        // accent into mojibake; the byte-order mark is what stops it.
        var bytes = await File.ReadAllBytesAsync(path);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
        Assert.Contains("Fürs Erste", Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public async Task TheJsonHasNoByteOrderMark()
    {
        var options = Setup(ExportFormat.Json, new SceneData { Title = "The deed", Order = 1 });

        var path = Output("outline.json");
        await Service().ExportDataAsync(options, path);

        // Some parsers reject one, and every parser specifies UTF-8 anyway.
        var bytes = await File.ReadAllBytesAsync(path);
        Assert.NotEqual(0xEF, bytes[0]);
    }

    [Fact]
    public async Task TheCodexRidesAlongInJsonOnly()
    {
        var mira = new CharacterData
        {
            Name = "Mira",
            Role = "Protagonist",
            Sections = [new EntitySection { Title = "Appearance", Content = "Tall." }],
            Relationships = [new EntityRelationship { Role = "sister", Target = "Tomas" }]
        };
        var options = Setup(ExportFormat.Json, new SceneData { Title = "The deed", Order = 1 });
        _entities.LoadCharactersAsync().Returns([mira]);

        var path = Output("outline.json");
        await Service().ExportDataAsync(options, path);

        var json = await File.ReadAllTextAsync(path);
        Assert.Contains("\"name\": \"Mira\"", json);
        Assert.Contains("Protagonist", json);
        Assert.Contains("Appearance", json);
        Assert.Contains("sister: Tomas", json);
    }

    [Fact]
    public async Task ARelationshipWithNoRoleIsStillNamed()
    {
        var mira = new CharacterData
        {
            Name = "Mira",
            Relationships = [new EntityRelationship { Role = "  ", Target = "Tomas" }]
        };
        var options = Setup(ExportFormat.Json);
        _entities.LoadCharactersAsync().Returns([mira]);

        var path = Output("outline.json");
        await Service().ExportDataAsync(options, path);

        Assert.Contains("\"Tomas\"", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task TheCodexIsNotInTheSheet()
    {
        var options = Setup(ExportFormat.Csv, new SceneData { Title = "The deed", Order = 1 });
        _entities.LoadCharactersAsync().Returns([new CharacterData { Name = "Mira" }]);

        var path = Output("outline.csv");
        await Service().ExportDataAsync(options, path);

        // One sheet cannot hold a scene list and a character list without one
        // of them being wrong.
        Assert.DoesNotContain("Mira", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task EveryCodexKindTravels()
    {
        var options = Setup(ExportFormat.Json);
        _entities.LoadLocationsAsync().Returns(
            [new LocationData { Name = "The Rookery", Type = "House", Description = "Damp." }]);
        _entities.LoadItemsAsync().Returns(
            [new ItemData { Name = "The Crest", Type = "Heirloom" }]);
        _entities.LoadLoreAsync().Returns(
            [new LoreData { Name = "The Oath", Category = "Custom" }]);

        var path = Output("outline.json");
        await Service().ExportDataAsync(options, path);

        var json = await File.ReadAllTextAsync(path);
        Assert.Contains("The Rookery", json);
        Assert.Contains("The Crest", json);
        Assert.Contains("The Oath", json);
    }

    [Fact]
    public async Task TheEntryPickerIsHonoured()
    {
        var mira = new CharacterData { Name = "Mira" };
        var tomas = new CharacterData { Name = "Tomas" };
        var options = Setup(ExportFormat.Json);
        options.SelectedEntityKeys = [$"character:{mira.Id}"];
        _entities.LoadCharactersAsync().Returns([mira, tomas]);

        var path = Output("outline.json");
        await Service().ExportDataAsync(options, path);

        var json = await File.ReadAllTextAsync(path);
        Assert.Contains("Mira", json);
        Assert.DoesNotContain("Tomas", json);
    }

    [Fact]
    public async Task ThePartSwitchesMeanTheSameHereAsInTheDocumentExports()
    {
        var mira = new CharacterData
        {
            Name = "Mira",
            Role = "Protagonist",
            Sections =
            [
                new EntitySection { Title = "Appearance", Content = "Tall." },
                new EntitySection { Title = "Secrets", Content = "The twist." }
            ],
            Relationships = [new EntityRelationship { Role = "sister", Target = "Tomas" }]
        };
        var options = Setup(ExportFormat.Json);
        options.CodexParts = ["sections"];
        options.SelectedSectionTitles = ["Appearance"];
        _entities.LoadCharactersAsync().Returns([mira]);

        var path = Output("outline.json");
        await Service().ExportDataAsync(options, path);

        // "Names and nothing else" has to mean the same thing in every format,
        // or a packet built in one and checked in the other disagrees.
        var json = await File.ReadAllTextAsync(path);
        Assert.Contains("Mira", json);
        Assert.Contains("Tall.", json);
        Assert.DoesNotContain("Protagonist", json);
        Assert.DoesNotContain("The twist.", json);
        Assert.DoesNotContain("sister", json);
    }

    [Fact]
    public async Task ScenesStillTravelWithoutAnEntityService()
    {
        _project.GetChaptersOrdered().Returns([_chapter]);
        _project.GetScenesForChapter(_chapter.Guid)
            .Returns([new SceneData { Title = "The deed", Order = 1, ChapterGuid = _chapter.Guid }]);
        _project.ActiveBook.Returns(new BookData());
        var options = new ExportOptions
        {
            Format = ExportFormat.Json,
            SelectedChapterGuids = [_chapter.Guid]
        };

        var path = Output("outline.json");
        await new ExportService(_project).ExportDataAsync(options, path);

        // A codex nobody can load is an empty codex, not a failed export.
        var json = await File.ReadAllTextAsync(path);
        Assert.Contains("The deed", json);
        Assert.Contains("\"codex\": []", json);
    }

    [Fact]
    public async Task TheBookNeedNotBeOpenForTheSheet()
    {
        _project.GetChaptersOrdered().Returns([_chapter]);
        _project.GetScenesForChapter(_chapter.Guid)
            .Returns([new SceneData { Title = "The deed", Order = 1, ChapterGuid = _chapter.Guid }]);
        _project.ActiveBook.Returns((BookData?)null);
        _entities.LoadCharactersAsync().Returns([]);
        _entities.LoadLocationsAsync().Returns([]);
        _entities.LoadItemsAsync().Returns([]);
        _entities.LoadLoreAsync().Returns([]);

        var path = Output("outline.csv");
        await Service().ExportDataAsync(
            new ExportOptions { Format = ExportFormat.Csv, SelectedChapterGuids = [_chapter.Guid] },
            path);

        Assert.Contains("The deed", await File.ReadAllTextAsync(path));
    }

    // ─── Reports ─────────────────────────────────────────────────────

    [Fact]
    public async Task TheSynopsisReportReadsTheWholeBook()
    {
        var options = Setup(ExportFormat.SynopsisReport,
            new SceneData { Title = "The deed", Order = 1, Synopsis = "She finds it." });
        var path = Output("synopsis.md");

        await Service().ExportReportAsync(options, path);

        var text = await File.ReadAllTextAsync(path);
        Assert.Contains("# Salt Road", text);
        Assert.Contains("The Rookery", text);
        Assert.Contains("She finds it.", text);
    }

    [Fact]
    public async Task ThePovReportCountsTheWholeBook()
    {
        var options = Setup(ExportFormat.PovReport, new SceneData
        {
            Title = "The deed",
            Order = 1,
            WordCount = 1000,
            AnalysisOverrides = new SceneAnalysisOverrides { Pov = "Mira" }
        });
        var path = Output("pov.md");

        await Service().ExportReportAsync(options, path);

        Assert.Contains("| Mira | 1 | 1,000 | 100% |", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task AReportKeepsTheScenesTheBookLeavesOut()
    {
        var options = Setup(ExportFormat.SynopsisReport,
            new SceneData { Title = "In the book", Order = 1 },
            new SceneData { Title = "Parked", Order = 2, Inactive = true });
        var path = Output("synopsis.md");

        await Service().ExportReportAsync(options, path);

        // Same reason as the metadata export: a report that hides the scenes
        // somebody set aside cannot answer why the act is short.
        Assert.Contains("Parked", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task AReportHonoursTheStageFilter()
    {
        var options = Setup(ExportFormat.SynopsisReport,
            new SceneData { Title = "Drafted", Order = 1, Stage = "Draft" },
            new SceneData { Title = "Revised", Order = 2, Stage = "Revised" });
        options.IncludedStages = ["Revised"];
        var path = Output("synopsis.md");

        await Service().ExportReportAsync(options, path);

        var text = await File.ReadAllTextAsync(path);
        Assert.DoesNotContain("Drafted", text);
        Assert.Contains("Revised", text);
    }

    [Fact]
    public async Task AReportOnANamelessBookIsStillTitled()
    {
        var options = Setup(ExportFormat.SynopsisReport, new SceneData { Title = "A", Order = 1 });
        options.Title = "   ";
        var path = Output("synopsis.md");

        await Service().ExportReportAsync(options, path);

        Assert.StartsWith("# Report", await File.ReadAllTextAsync(path));
    }

    // The other half of the CSV pair: the scene sheet has no room for the Codex,
    // and an entry's shape is its own, so the sheet is one row per field.
    [Fact]
    public async Task TheCodexSheetIsOneRowPerField()
    {
        var options = Setup(ExportFormat.CodexCsv, new SceneData { Title = "S", Order = 1 });
        _entities.LoadCharactersAsync().Returns([
            new CharacterData
            {
                Name = "Mira",
                Role = "Protagonist",
                Sections = [new EntitySection { Title = "History", Content = "Born north." }],
                Relationships = [new EntityRelationship { Role = "sister", Target = "Tomas" }]
            }
        ]);
        var path = Output("codex.csv");

        await Service().ExportDataAsync(options, path);

        var text = await File.ReadAllTextAsync(path);
        Assert.StartsWith("Kind,Name,Field,Value", text);
        Assert.Contains(",Mira,", text);
        Assert.Contains("Born north.", text);
        Assert.Contains("sister: Tomas", text);
    }

    [Fact]
    public async Task TheOutlineLeavesAsAShapeRatherThanATable()
    {
        var options = Setup(
            ExportFormat.Opml,
            new SceneData { Title = "Arrival", Order = 1, Synopsis = "She gets off the train." },
            new SceneData { Title = "The room", Order = 2 });
        var path = Output("outline.opml");

        await Service().ExportDataAsync(options, path);

        var text = await File.ReadAllTextAsync(path);
        Assert.Contains("<opml version=\"2.0\">", text);
        Assert.Contains("<outline text=\"Arrival\" _note=\"She gets off the train.\" />", text);
        // No synopsis is no note rather than an empty one.
        Assert.Contains("<outline text=\"The room\" />", text);
    }

    // A mark is what makes Excel read UTF-8; in front of JSON or XML it is what
    // makes a strict parser refuse the file.
    [Theory]
    [InlineData(ExportFormat.Csv, true)]
    [InlineData(ExportFormat.CodexCsv, true)]
    [InlineData(ExportFormat.Json, false)]
    [InlineData(ExportFormat.Opml, false)]
    public async Task OnlyTheSpreadsheetsCarryAByteOrderMark(ExportFormat format, bool expected)
    {
        var options = Setup(format, new SceneData { Title = "S", Order = 1 });
        var path = Output($"mark-{format}.out");

        await Service().ExportDataAsync(options, path);

        var bytes = await File.ReadAllBytesAsync(path);
        var hasMark = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        Assert.Equal(expected, hasMark);
    }

    // Everything the project holds, through the same export path as the rest.
    [Fact]
    public async Task TheWholeProjectLeavesAsOneDocument()
    {
        var options = Setup(ExportFormat.WorldJson,
            new SceneData { Title = "Arrival", Order = 1, Synopsis = "She gets off the train." });
        var book = new BookData { Name = "Salt Road" };
        book.Plotlines.Add(new PlotlineData { Name = "The debt" });
        _project.ActiveBook.Returns(book);
        _project.CurrentProject.Returns(new ProjectMetadata { Name = "SaltProject" });
        var path = Output("world.json");

        await Service().ExportDataAsync(options, path);

        var text = await File.ReadAllTextAsync(path);
        Assert.Contains("\"project\": \"SaltProject\"", text);
        Assert.Contains("Arrival", text);
        Assert.Contains("The debt", text);
        // The sections that had no export path of their own are present even
        // when empty, so a reader can tell "none" from "not exported".
        Assert.Contains("\"research\"", text);
        Assert.Contains("\"smartLists\"", text);
    }

    [Fact]
    public async Task TheWholeProjectAlsoLeavesAsOnePage()
    {
        var options = Setup(ExportFormat.WorldHtml,
            new SceneData { Title = "Arrival", Order = 1 });
        _project.ActiveBook.Returns(new BookData { Name = "Salt Road" });
        var path = Output("world.html");

        await Service().ExportDataAsync(options, path);

        var text = await File.ReadAllTextAsync(path);
        Assert.StartsWith("<!doctype html>", text);
        Assert.Contains("Arrival", text);
        // One file, so it opens by double-clicking and survives being emailed.
        Assert.DoesNotContain("<link", text);
    }

    // The archive is named for the project. Carrying one book of a trilogy was
    // two-thirds missing, and the document said nothing about the fact.
    [Fact]
    public async Task TheArchiveReachesTheProjectsOtherBooks()
    {
        var options = Setup(ExportFormat.WorldJson,
            new SceneData { Title = "Arrival", Order = 1 });
        var open = new BookData { Id = "b1", Name = "Book One" };
        var closed = new BookData { Id = "b2", Name = "Book Two" };
        closed.Plotlines.Add(new PlotlineData { Name = "The reckoning" });
        _project.ActiveBook.Returns(open);
        _project.CurrentProject.Returns(new ProjectMetadata
        {
            Name = "Series",
            Books = [open, closed]
        });
        // The closed book is read through the per-book accessors rather than by
        // switching to it, so nothing about the open book moves.
        var closedChapter = new ChapterData { Guid = "c-two", Title = "Later", Order = 0 };
        closed.Chapters.Add(closedChapter);
        var manifest = new ScenesManifest();
        manifest.Chapters["c-two"] =
        [
            new SceneData { Id = "s1", Title = "Elsewhere", Order = 0, WordCount = 400,
                Synopsis = "It ends." },
            // Archived scenes are not part of the book and do not belong in its
            // outline; parked and held-back ones are flagged rather than dropped.
            new SceneData { Id = "s2", Title = "Cut", Order = 1, ArchivedAt = DateTime.UtcNow },
            new SceneData { Id = "s3", Title = "Parked", Order = 2, Inactive = true }
        ];
        _project.LoadScenesManifestForAsync(closed).Returns(manifest);
        var path = Output("series.json");

        await Service().ExportDataAsync(options, path);

        var text = await File.ReadAllTextAsync(path);
        Assert.Contains("\"otherBooks\"", text);
        Assert.Contains("Book Two", text);
        Assert.Contains("The reckoning", text);
        // Exactly one other book, and the open one is not repeated among them.
        var archive = System.Text.Json.JsonSerializer.Deserialize<WorldArchiveDocument>(text)!;
        var volume = Assert.Single(archive.OtherBooks);
        Assert.Equal("Book Two", volume.Book);
        Assert.Equal(["Elsewhere", "Parked"], volume.Scenes.Select(s => s.Scene));
        Assert.Equal("Later", volume.Scenes[0].Chapter);
        Assert.Equal(400, volume.Scenes[0].Words);
        Assert.Equal("It ends.", volume.Scenes[0].Synopsis);
        Assert.True(volume.Scenes[1].Inactive);
        Assert.Same(open, _project.ActiveBook);
    }

    // A box set: the open book, then each further volume the writer asked for.
    [Fact]
    public async Task ABoxSetCarriesEveryVolumeItWasAskedFor()
    {
        var options = Setup(ExportFormat.Markdown,
            new SceneData { Title = "Arrival", Order = 1 });
        var open = new BookData { Id = "b1", Name = "Book One" };
        var closed = new BookData { Id = "b2", Name = "Book Two", ChapterFolder = "Chapters" };
        var closedChapter = new ChapterData { Guid = "c2", Title = "Later", Order = 0 };
        closed.Chapters.Add(closedChapter);
        _project.ActiveBook.Returns(open);
        _project.CurrentProject.Returns(new ProjectMetadata { Books = [open, closed] });

        var manifest = new ScenesManifest();
        var closedScene = new SceneData { Id = "s9", Title = "Elsewhere", Order = 0 };
        manifest.Chapters["c2"] = [closedScene];
        _project.LoadScenesManifestForAsync(closed).Returns(manifest);
        _project.ReadSceneContentForAsync(closed, closedChapter, closedScene)
            .Returns("<p>The second book.</p>");

        options.IncludedBookIds = ["b2"];
        var chapters = await Service().CompileChaptersAsync(options);

        // The open book's chapter, then the volume divider, then the closed
        // book's chapter - a divider so eighty chapters do not run together.
        Assert.Equal(3, chapters.Count);
        Assert.Equal("Book Two", chapters[1].Heading);
        Assert.Empty(chapters[1].Scenes);
        Assert.Equal("Later", chapters[2].Title);
        Assert.Contains("The second book.", chapters[2].Scenes.Single().HtmlContent);
        // Order is renumbered across the whole set, not restarted per volume.
        Assert.Equal([0, 1, 2], chapters.Select(c => c.Order));
    }

    [Fact]
    public async Task WithNoVolumesAskedForNothingIsAppended()
    {
        var options = Setup(ExportFormat.Markdown, new SceneData { Title = "Arrival", Order = 1 });
        var open = new BookData { Id = "b1", Name = "Book One" };
        _project.ActiveBook.Returns(open);
        _project.CurrentProject.Returns(new ProjectMetadata
        {
            Books = [open, new BookData { Id = "b2", Name = "Book Two" }]
        });

        // The default, and what every export did before box sets existed.
        Assert.Single(await Service().CompileChaptersAsync(options));
    }

    [Fact]
    public async Task AVolumeWithNoManifestIsSkippedRatherThanEmpty()
    {
        var options = Setup(ExportFormat.Markdown, new SceneData { Title = "Arrival", Order = 1 });
        var open = new BookData { Id = "b1", Name = "Book One" };
        var ghost = new BookData { Id = "b2", Name = "Never written" };
        _project.ActiveBook.Returns(open);
        _project.CurrentProject.Returns(new ProjectMetadata { Books = [open, ghost] });
        _project.LoadScenesManifestForAsync(ghost).Returns((ScenesManifest?)null);

        options.IncludedBookIds = ["b2"];

        // A heading announcing a volume with nothing in it is worse than not
        // printing it.
        Assert.Single(await Service().CompileChaptersAsync(options));
    }
}
