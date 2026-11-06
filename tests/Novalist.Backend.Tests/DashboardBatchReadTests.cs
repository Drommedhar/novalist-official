using Novalist.Backend.Rpc;
using Novalist.Backend.Tests.TestHelpers;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Xunit;

namespace Novalist.Backend.Tests;

public sealed class DashboardBatchReadTests
{
    [Fact]
    public async Task InitialMobileStatisticsDoNotReadSceneContents()
    {
        using var fixture = await Fixture.CreateAsync();

        var result = await new DashboardRpc(fixture.Workspace).GetAsync(1, includeEchoPhrases: false);

        Assert.Equal(36, result.TotalWords);
        Assert.Equal(3, result.SceneCount);
        Assert.Equal(3, result.RecentActivity.Count);
        Assert.Empty(result.EchoPhrases);
        Assert.Empty(fixture.Files.Batches);
        Assert.Equal(0, fixture.Files.DirectSceneReads);
    }

    [Fact]
    public async Task SeparateEchoReportBatchesOnlyLiveScenes()
    {
        using var fixture = await Fixture.CreateAsync();

        var result = await new DashboardRpc(fixture.Workspace).EchoPhrasesAsync();

        Assert.Contains(result, phrase => phrase.Phrase == "cold wind howled" && phrase.Count == 9);
        Assert.Equal(2, fixture.Files.Batches.Count);
        Assert.Equal(2, fixture.Files.Batches[0].Length);
        Assert.Single(fixture.Files.Batches[1]);
        Assert.Equal(0, fixture.Files.DirectSceneReads);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SeparateEchoReportStopsWhenCancelled(bool alreadyCancelled)
    {
        using var fixture = await Fixture.CreateAsync();
        using var cancellation = new CancellationTokenSource();
        fixture.Files.AfterBatch = cancellation.Cancel;
        if (alreadyCancelled) cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new DashboardRpc(fixture.Workspace).EchoPhrasesAsync(cancellation.Token));

        Assert.Equal(alreadyCancelled ? 0 : 1, fixture.Files.Batches.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnalyticsCardsBatchManuscriptReads(bool castAbsence)
    {
        using var fixture = await Fixture.CreateAsync();
        var rpc = new AnalyticsRpc(fixture.Workspace);

        if (castAbsence) await rpc.CastAbsenceAsync();
        else Assert.Equal(5, (await rpc.BookAsync()).Pov.Sum(row => row.SceneCount));

        Assert.Equal(2, fixture.Files.Batches.Count);
        Assert.Equal(4, fixture.Files.Batches[0].Length);
        Assert.Single(fixture.Files.Batches[1]);
        Assert.Equal(0, fixture.Files.DirectSceneReads);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task AnalyticsCancellationStopsBeforeAnotherChapter(bool castAbsence, bool alreadyCancelled)
    {
        using var fixture = await Fixture.CreateAsync();
        using var cancellation = new CancellationTokenSource();
        fixture.Files.AfterBatch = cancellation.Cancel;
        if (alreadyCancelled) cancellation.Cancel();
        var rpc = new AnalyticsRpc(fixture.Workspace);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            if (castAbsence) await rpc.CastAbsenceAsync(cancellationToken: cancellation.Token);
            else await rpc.BookAsync(cancellation.Token);
        });

        Assert.Equal(alreadyCancelled ? 0 : 1, fixture.Files.Batches.Count);
        if (!alreadyCancelled) Assert.Equal(cancellation.Token, fixture.Files.LastToken);
        Assert.Equal(0, fixture.Files.DirectSceneReads);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DashboardBatchesEachChaptersScenesAndPreservesItsVisibilityRules(bool overview)
    {
        using var fixture = await Fixture.CreateAsync();
        var rpc = new DashboardRpc(fixture.Workspace);
        if (overview)
        {
            var result = await rpc.OverviewAsync();
            Assert.Equal(new[] { "First", "Second", "Inactive" }, result.Chapters[0].Scenes.Select(scene => scene.Title));
            Assert.Equal(48, result.Chapters[0].Words);
            Assert.True(result.Chapters[0].Readability > 0);
        }
        else
        {
            var result = await rpc.GetAsync(1);
            Assert.Equal(36, result.TotalWords);
            Assert.Equal(3, result.SceneCount);
            Assert.Contains(result.EchoPhrases, phrase => phrase.Phrase == "cold wind howled" && phrase.Count >= 5);
            Assert.DoesNotContain(result.RecentActivity, scene => scene.SceneTitle is "Inactive" or "Archived");
        }

        Assert.Equal(2, fixture.Files.Batches.Count);
        Assert.Equal(overview ? 3 : 2, fixture.Files.Batches[0].Length);
        Assert.Single(fixture.Files.Batches[1]);
        Assert.Equal(0, fixture.Files.DirectSceneReads);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task CancellationStopsBeforeStartingAnotherChapter(bool overview, bool alreadyCancelled)
    {
        using var fixture = await Fixture.CreateAsync();
        using var cancellation = new CancellationTokenSource();
        fixture.Files.AfterBatch = cancellation.Cancel;
        if (alreadyCancelled) cancellation.Cancel();
        var rpc = new DashboardRpc(fixture.Workspace);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            if (overview) await rpc.OverviewAsync(cancellation.Token);
            else await rpc.GetAsync(1, cancellationToken: cancellation.Token);
        });

        Assert.Equal(alreadyCancelled ? 0 : 1, fixture.Files.Batches.Count);
        if (!alreadyCancelled) Assert.Equal(cancellation.Token, fixture.Files.LastToken);
        Assert.Equal(0, fixture.Files.DirectSceneReads);
    }

    [Fact]
    public async Task ASceneRemovedExternallyIsOmittedFromRecentActivity()
    {
        using var fixture = await Fixture.CreateAsync();
        var projects = fixture.Workspace.Projects;
        var chapter = projects.GetChaptersOrdered()[0];
        var scene = projects.GetScenesForChapter(chapter.Guid)[0];
        File.Delete(projects.GetSceneFilePath(chapter, scene));

        var result = await new DashboardRpc(fixture.Workspace).GetAsync(1);

        Assert.Equal(36, result.TotalWords);
        Assert.Equal(3, result.SceneCount);
        Assert.Equal(2, result.RecentActivity.Count);
        Assert.DoesNotContain(result.RecentActivity, activity => activity.SceneId == scene.Id);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TimestampProviderFailuresAreReported(bool accessDenied)
    {
        using var fixture = await Fixture.CreateAsync();
        Exception failure = accessDenied ? new UnauthorizedAccessException("Permission revoked") : new IOException("Provider unavailable");
        fixture.Files.TimestampFailure = failure;

        var error = await Assert.ThrowsAsync(failure.GetType(), () => new DashboardRpc(fixture.Workspace).GetAsync(1));

        Assert.Same(failure, error);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly TempDir _temp = new();
        public BatchFiles Files { get; } = new();
        public Workspace Workspace { get; }
        private Fixture() => Workspace = new Workspace(_temp.Combine("settings"), fileService: Files);

        public static async Task<Fixture> CreateAsync()
        {
            var fixture = new Fixture();
            var projects = fixture.Workspace.Projects;
            await projects.CreateProjectAsync(fixture._temp.Path, "Novel", "Book");
            var chapter = await projects.CreateChapterAsync("One");
            await fixture.AddSceneAsync(chapter, "First");
            await fixture.AddSceneAsync(chapter, "Second");
            var inactive = await fixture.AddSceneAsync(chapter, "Inactive");
            inactive.Inactive = true;
            var archived = await fixture.AddSceneAsync(chapter, "Archived");
            archived.ArchivedAt = DateTime.UtcNow;
            await fixture.AddSceneAsync(await projects.CreateChapterAsync("Two"), "Last");
            fixture.Files.Batches.Clear();
            fixture.Files.DirectSceneReads = 0;
            return fixture;
        }

        private async Task<SceneData> AddSceneAsync(ChapterData chapter, string title)
        {
            var projects = Workspace.Projects;
            var scene = await projects.CreateSceneAsync(chapter.Guid, title);
            await projects.WriteSceneContentAsync(chapter, scene,
                "<p>cold wind howled tonight. cold wind howled tonight. cold wind howled tonight.</p>");
            scene.WordCount = title == "Inactive" ? 24 : 12;
            return scene;
        }

        public void Dispose() { Workspace.Dispose(); _temp.Dispose(); }
    }

    private sealed class BatchFiles : FileService, IFileService, IBatchTextReader
    {
        public List<string[]> Batches { get; } = [];
        public int DirectSceneReads { get; set; }
        public Action? AfterBatch { get; set; }
        public CancellationToken LastToken { get; private set; }
        public Exception? TimestampFailure { get; set; }

        Task<DateTime> IFileService.GetLastWriteTimeUtcAsync(string path)
        {
            if (TimestampFailure != null) return Task.FromException<DateTime>(TimestampFailure);
            if (!File.Exists(path)) return Task.FromException<DateTime>(new FileNotFoundException("Scene was removed", path));
            return GetLastWriteTimeUtcAsync(path);
        }

        Task<string> IFileService.ReadTextAsync(string path)
        {
            if (path.EndsWith(".novalist", StringComparison.Ordinal)) DirectSceneReads++;
            return ReadTextAsync(path);
        }

        public async Task<IReadOnlyDictionary<string, string>> ReadTextsAsync(IReadOnlyList<string> paths,
            CancellationToken cancellationToken = default)
        {
            var contents = new Dictionary<string, string>();
            foreach (var path in paths.Reverse())
            {
                cancellationToken.ThrowIfCancellationRequested();
                contents[path] = await ReadTextAsync(path);
            }
            return contents;
        }

        public async Task<IReadOnlyDictionary<string, string>> ReadExistingTextsAsync(IReadOnlyList<string> paths,
            CancellationToken cancellationToken = default)
        {
            Batches.Add(paths.ToArray());
            LastToken = cancellationToken;
            var contents = await ReadTextsAsync(paths.Where(File.Exists).ToArray(), cancellationToken);
            AfterBatch?.Invoke();
            return contents;
        }
    }
}
