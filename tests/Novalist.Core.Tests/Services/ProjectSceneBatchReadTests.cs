using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Core.Tests.TestHelpers;
using Xunit;

namespace Novalist.Core.Tests.Services;

public sealed class ProjectSceneBatchReadTests
{
    [Fact]
    public async Task BookAnalyticsUsesValidatedSceneBatchesAndCurrentCodexNames()
    {
        using var fixture = await Fixture.CreateAsync();
        var entities = new EntityService(fixture.Projects);
        await entities.SaveCharacterAsync(new CharacterData { Id = "mira", Name = "Mira" });
        await fixture.Projects.WriteSceneContentAsync(fixture.Chapter, fixture.Scenes[0],
            "<p><span data-entity-id=\"mira\">Mira</span> arrived.</p>");
        fixture.Files.ExistingBatches = 0;

        var result = await new BookAnalyticsService(fixture.Projects, entities).ComputeAsync();

        var character = Assert.Single(result.Characters);
        Assert.Equal("Mira", character.Label);
        Assert.Equal(1, character.TotalScenes);
        Assert.Equal(1, fixture.Files.ExistingBatches);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SceneBatchReadsUseTheProviderAndKeepSceneOrderIncludingMissingFiles(bool missing)
    {
        using var fixture = await Fixture.CreateAsync();
        if (missing) File.Delete(fixture.Projects.GetSceneFilePath(fixture.Chapter, fixture.Scenes[1]));

        var contents = await fixture.Projects.ReadSceneContentsAsync(fixture.Chapter, fixture.Scenes);

        Assert.Equal(new[] { "<p>First</p>", missing ? "" : "<p>Second</p>" }, contents);
        Assert.Equal(1, fixture.Files.ExistingBatches);
        Assert.Equal(0, fixture.Files.DirectReads);
    }

    [Fact]
    public async Task AChangedIdentityInABatchReconcilesAndRejectsTheStaleScene()
    {
        using var fixture = await Fixture.CreateAsync();
        var path = fixture.Projects.GetSceneFilePath(fixture.Chapter, fixture.Scenes[0]);
        var modified = File.GetLastWriteTimeUtc(path);
        var replacementId = Guid.NewGuid().ToString();
        var replacement = FileFrontMatter.Build(replacementId) + "<p>First</p>";
        await File.WriteAllTextAsync(path, replacement);
        File.SetLastWriteTimeUtc(path, modified);

        await Assert.ThrowsAsync<IOException>(() => fixture.Projects.ReadSceneContentsAsync(fixture.Chapter, fixture.Scenes));

        Assert.Contains(fixture.Projects.GetScenesForChapter(fixture.Chapter.Guid), scene => scene.Id == replacementId);
        Assert.Equal(replacement, await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task CancellationAfterABatchStopsBeforeAnyContextIsReturned()
    {
        using var fixture = await Fixture.CreateAsync();
        using var cancellation = new CancellationTokenSource();
        fixture.Files.AfterBatch = cancellation.Cancel;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            fixture.Projects.ReadSceneContentsAsync(fixture.Chapter, fixture.Scenes, cancellation.Token));

        Assert.Equal(cancellation.Token, fixture.Files.LastToken);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly TempDir _temp = new();
        public BatchFiles Files { get; } = new();
        public ProjectService Projects { get; }
        public ChapterData Chapter { get; private set; } = new();
        public List<SceneData> Scenes { get; } = [];
        private Fixture() => Projects = new ProjectService(Files);

        public static async Task<Fixture> CreateAsync()
        {
            var fixture = new Fixture();
            await fixture.Projects.CreateProjectAsync(fixture._temp.Path, "Novel", "Book");
            fixture.Chapter = await fixture.Projects.CreateChapterAsync("Chapter");
            foreach (var title in new[] { "First", "Second" })
            {
                var scene = await fixture.Projects.CreateSceneAsync(fixture.Chapter.Guid, title);
                await fixture.Projects.WriteSceneContentAsync(fixture.Chapter, scene, $"<p>{title}</p>");
                fixture.Scenes.Add(scene);
            }
            await fixture.Projects.LoadProjectAsync(fixture.Projects.ProjectRoot!);
            fixture.Files.ExistingBatches = fixture.Files.DirectReads = 0;
            return fixture;
        }

        public void Dispose() => _temp.Dispose();
    }

    private sealed class BatchFiles : FileService, IFileService, IBatchTextReader
    {
        public int ExistingBatches { get; set; }
        public int DirectReads { get; set; }
        public Action? AfterBatch { get; set; }
        public CancellationToken LastToken { get; private set; }
        Task<string> IFileService.ReadTextAsync(string path) { DirectReads++; return ReadTextAsync(path); }

        public async Task<IReadOnlyDictionary<string, string>> ReadTextsAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken = default)
        {
            var contents = new Dictionary<string, string>();
            foreach (var path in paths.Reverse())
            {
                cancellationToken.ThrowIfCancellationRequested();
                contents[path] = await ReadTextAsync(path);
            }
            return contents;
        }

        public async Task<IReadOnlyDictionary<string, string>> ReadExistingTextsAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken = default)
        {
            ExistingBatches++;
            LastToken = cancellationToken;
            var contents = await ReadTextsAsync(paths.Where(File.Exists).ToArray(), cancellationToken);
            AfterBatch?.Invoke();
            return contents;
        }
    }
}
