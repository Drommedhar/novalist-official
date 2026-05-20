using System.Text.Json;
using NSubstitute;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Core.Tests.TestHelpers;
using Xunit;

namespace Novalist.Core.Tests.Services;

public class WordHistoryServiceTests
{
    private const string Root = "/proj";
    private static string HistPath => Path.Combine(Root, ".novalist", "word-history.jsonl");

    private static (WordHistoryService Sut, InMemoryFileService Files, IProjectService Project) Build(bool withRoot = true)
    {
        var files = new InMemoryFileService();
        var project = Substitute.For<IProjectService>();
        project.ProjectRoot.Returns(withRoot ? Root : null);
        return (new WordHistoryService(files, project), files, project);
    }

    private static string Line(string date, string sceneId, string bookId, int words, int delta)
        => JsonSerializer.Serialize(new WordHistoryEntry
        {
            Date = date, SceneId = sceneId, BookId = bookId, Words = words, Delta = delta
        });

    private static void Seed(InMemoryFileService files, params string[] lines)
        => files.Files[HistPath] = string.Join("\n", lines) + "\n";

    [Fact]
    public async Task LoadAsync_NoProject_MarksLoaded()
    {
        var (sut, _, _) = Build(withRoot: false);
        await sut.LoadAsync(); // no throw, no file
        Assert.Empty(sut.ReadRange(DateOnly.MinValue, DateOnly.MaxValue));
    }

    [Fact]
    public async Task LoadAsync_MissingFile_NoEntries()
    {
        var (sut, _, _) = Build();
        await sut.LoadAsync();
        Assert.Empty(sut.ReadRange(DateOnly.MinValue, DateOnly.MaxValue));
    }

    [Fact]
    public async Task LoadAsync_ParsesValidLines_SkipsMalformed()
    {
        var (sut, files, _) = Build();
        files.Files[HistPath] = Line("2024-01-01", "s1", "b1", 100, 100) + "\n" +
                                "   \n" +                 // blank -> skipped
                                "{ not json }\n" +        // malformed -> skipped
                                Line("2024-01-02", "s1", "b1", 150, 50) + "\n";
        await sut.LoadAsync();
        var all = sut.ReadRange(new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 31));
        Assert.Equal(2, all.Count);
    }

    [Fact]
    public async Task RecordSaveAsync_EmptySceneId_NoOp()
    {
        var (sut, files, _) = Build();
        await sut.RecordSaveAsync("b1", "", 100);
        Assert.False(files.Files.ContainsKey(HistPath));
    }

    [Fact]
    public async Task RecordSaveAsync_NoProject_NoOp()
    {
        var (sut, files, _) = Build(withRoot: false);
        await sut.RecordSaveAsync("b1", "s1", 100);
        Assert.Empty(files.Files);
    }

    [Fact]
    public async Task RecordSaveAsync_NewScene_RecordsDeltaFromZero()
    {
        var (sut, files, _) = Build();
        var today = DateOnly.FromDateTime(DateTime.Now);
        await sut.RecordSaveAsync("b1", "s1", 120);
        Assert.True(files.Files.ContainsKey(HistPath));
        Assert.Equal(120, sut.TotalForDay(today));
    }

    [Fact]
    public async Task RecordSaveAsync_SameDayScene_AccumulatesDelta()
    {
        var (sut, _, _) = Build();
        var today = DateOnly.FromDateTime(DateTime.Now);
        await sut.RecordSaveAsync("b1", "s1", 100);
        await sut.RecordSaveAsync("b1", "s1", 130); // +30 cumulative
        Assert.Equal(130, sut.TotalForDay(today));
    }

    [Fact]
    public async Task RecordSaveAsync_LoadsJournalWithMalformedLine_Skips()
    {
        var (sut, files, _) = Build();
        var today = DateOnly.FromDateTime(DateTime.Now).ToString("yyyy-MM-dd");
        // Malformed line exercises the no-lock loader's catch path.
        files.Files[HistPath] = "{ broken json\n" + Line(today, "s1", "b1", 40, 40) + "\n";
        await sut.RecordSaveAsync("b1", "s1", 70); // prev 40 -> delta 30
        Assert.Equal(70, sut.TotalForDay(DateOnly.FromDateTime(DateTime.Now)));
    }

    [Fact]
    public async Task RecordSaveAsync_LoadsExistingJournalFirst()
    {
        var (sut, files, _) = Build();
        var today = DateOnly.FromDateTime(DateTime.Now).ToString("yyyy-MM-dd");
        Seed(files, Line(today, "s1", "b1", 50, 50));
        await sut.RecordSaveAsync("b1", "s1", 80); // prev 50 -> delta 30, cumulative 80
        Assert.Equal(80, sut.TotalForDay(DateOnly.FromDateTime(DateTime.Now)));
    }

    [Fact]
    public void Reset_ClearsState()
    {
        var (sut, _, _) = Build();
        sut.Reset();
        Assert.Empty(sut.ReadRange(DateOnly.MinValue, DateOnly.MaxValue));
    }

    [Fact]
    public async Task TotalForDay_FiltersByBookAndPositiveDelta()
    {
        var (sut, files, _) = Build();
        Seed(files,
            Line("2024-03-01", "s1", "b1", 100, 100),
            Line("2024-03-01", "s2", "b1", 0, -20),   // negative delta ignored
            Line("2024-03-01", "s3", "b2", 50, 50),   // other book
            Line("2024-03-02", "s1", "b1", 200, 100)); // other day
        await sut.LoadAsync();
        Assert.Equal(100, sut.TotalForDay(new DateOnly(2024, 3, 1), "b1"));
        Assert.Equal(150, sut.TotalForDay(new DateOnly(2024, 3, 1))); // all books
    }

    [Fact]
    public async Task ReadRange_RespectsBoundsAndBook()
    {
        var (sut, files, _) = Build();
        Seed(files,
            Line("2024-01-01", "s1", "b1", 10, 10),
            Line("2024-01-05", "s1", "b1", 20, 10),
            Line("2024-01-10", "s1", "b2", 30, 10));
        await sut.LoadAsync();
        Assert.Equal(2, sut.ReadRange(new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 6)).Count);
        Assert.Single(sut.ReadRange(new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 31), "b2"));
    }

    [Fact]
    public void CurrentStreak_NonPositiveGoal_ReturnsZero()
    {
        var (sut, _, _) = Build();
        Assert.Equal(0, sut.CurrentStreak(DateOnly.FromDateTime(DateTime.Now), 0));
    }

    [Fact]
    public async Task CurrentStreak_CountsConsecutiveDaysMeetingGoal()
    {
        var (sut, files, _) = Build();
        var d0 = new DateOnly(2024, 5, 10);
        Seed(files,
            Line(d0.ToString("yyyy-MM-dd"), "s1", "b1", 100, 100),
            Line(d0.AddDays(-1).ToString("yyyy-MM-dd"), "s1", "b1", 100, 100));
        // gap on d0-2 breaks the streak
        await sut.LoadAsync();
        Assert.Equal(2, sut.CurrentStreak(d0, 50));
    }

    [Fact]
    public async Task ScenesTouchedOn_AggregatesDeltasSkippingZero()
    {
        var (sut, files, _) = Build();
        Seed(files,
            Line("2024-06-01", "s1", "b1", 100, 100),
            Line("2024-06-01", "s1", "b1", 130, 30),
            Line("2024-06-01", "s2", "b1", 0, 0),    // zero delta skipped
            Line("2024-06-01", "s3", "b2", 40, 40));
        await sut.LoadAsync();
        var touched = sut.ScenesTouchedOn(new DateOnly(2024, 6, 1), "b1");
        Assert.Equal(130, touched["s1"]);
        Assert.False(touched.ContainsKey("s2"));
        Assert.False(touched.ContainsKey("s3"));
    }

    [Fact]
    public async Task MigrateLegacyBaseline_NoProject_NoOp()
    {
        var (sut, files, _) = Build(withRoot: false);
        await sut.MigrateLegacyBaselineAsync();
        Assert.Empty(files.Files);
    }

    [Fact]
    public async Task MigrateLegacyBaseline_ExistingJournal_SkipsSeed()
    {
        var (sut, files, _) = Build();
        Seed(files, Line("2024-01-01", "s1", "b1", 10, 10));
        await sut.MigrateLegacyBaselineAsync();
        // Journal unchanged (single line) -> no reseed.
        Assert.Single(files.Files[HistPath].Split('\n', StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact]
    public async Task MigrateLegacyBaseline_NoManifestOrBook_NoOp()
    {
        var (sut, files, project) = Build();
        project.ScenesManifest.Returns((ScenesManifest?)null);
        project.ActiveBook.Returns((BookData?)null);
        await sut.MigrateLegacyBaselineAsync();
        Assert.False(files.Files.ContainsKey(HistPath));
    }

    [Fact]
    public async Task MigrateLegacyBaseline_SeedsFromManifestScenes()
    {
        var (sut, files, project) = Build();
        var manifest = new ScenesManifest();
        manifest.Chapters["c1"] = new List<SceneData> { new() { Id = "s1", WordCount = 200 } };
        project.ScenesManifest.Returns(manifest);
        project.ActiveBook.Returns(new BookData { Id = "b1" });
        project.ProjectSettings.Returns(new ProjectSettings());

        await sut.MigrateLegacyBaselineAsync();

        Assert.True(files.Files.ContainsKey(HistPath));
        await project.Received(1).SaveProjectSettingsAsync();
    }

    [Fact]
    public async Task MigrateLegacyBaseline_NoLegacyAndNoScenes_NoOp()
    {
        var (sut, files, project) = Build();
        project.ScenesManifest.Returns(new ScenesManifest()); // empty
        project.ActiveBook.Returns(new BookData { Id = "b1" });
        project.ProjectSettings.Returns(new ProjectSettings());
        await sut.MigrateLegacyBaselineAsync();
        Assert.False(files.Files.ContainsKey(HistPath));
    }

    [Fact]
    public async Task MigrateLegacyBaseline_UsesLegacyBaselineDate()
    {
        var (sut, files, project) = Build();
        var manifest = new ScenesManifest();
        manifest.Chapters["c1"] = new List<SceneData> { new() { Id = "s1", WordCount = 10 } };
        project.ScenesManifest.Returns(manifest);
        project.ActiveBook.Returns(new BookData { Id = "b1" });
        var settings = new ProjectSettings();
        settings.WordCountGoals.DailyBaselineDate = "2020-01-01";
        project.ProjectSettings.Returns(settings);

        await sut.MigrateLegacyBaselineAsync();
        await sut.LoadAsync();
        Assert.Single(sut.ReadRange(new DateOnly(2020, 1, 1), new DateOnly(2020, 1, 1)));
    }
}
