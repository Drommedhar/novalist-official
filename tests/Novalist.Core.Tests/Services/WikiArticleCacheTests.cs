using Novalist.Core.Services;
using Novalist.Core.Tests.TestHelpers;
using Xunit;

namespace Novalist.Core.Tests.Services;

public class WikiArticleCacheTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly FileService _files = new();
    private readonly ProjectService _project;
    private readonly WikiArticleCache _cache;

    public WikiArticleCacheTests()
    {
        _project = new ProjectService(_files);
        _cache = new WikiArticleCache(_project, _files);
    }

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void ComputeInputHash_IsStableAndSensitive()
    {
        Assert.Equal(WikiArticleCache.ComputeInputHash("abc"), WikiArticleCache.ComputeInputHash("abc"));
        Assert.NotEqual(WikiArticleCache.ComputeInputHash("abc"), WikiArticleCache.ComputeInputHash("abd"));
    }

    [Fact]
    public async Task Read_NullWhenNoProjectLoaded()
    {
        Assert.Null(await _cache.ReadAsync("hero"));
    }

    [Fact]
    public async Task Write_NoOpWhenNoProjectLoaded()
    {
        await _cache.WriteAsync("hero", new WikiArticleCacheEntry { Summary = "x" });
        Assert.Null(await _cache.ReadAsync("hero"));
    }

    [Fact]
    public async Task WriteThenRead_RoundTrips()
    {
        await _project.CreateProjectAsync(_dir.Path, "P", "Book");
        var entry = new WikiArticleCacheEntry
        {
            Summary = "Aldric is a knight.",
            GeneratedAt = "2026-07-22T10:00:00Z",
            InputHash = "HASH",
            Model = "test-model"
        };

        await _cache.WriteAsync("hero", entry);
        var read = await _cache.ReadAsync("hero");

        Assert.NotNull(read);
        Assert.Equal("Aldric is a knight.", read!.Summary);
        Assert.Equal("HASH", read.InputHash);
        Assert.Equal("test-model", read.Model);
    }

    [Fact]
    public async Task Read_NullWhenNoCachedFile()
    {
        await _project.CreateProjectAsync(_dir.Path, "P", "Book");
        Assert.Null(await _cache.ReadAsync("nope"));
    }

    [Fact]
    public async Task Read_NullWhenFileCorrupt()
    {
        await _project.CreateProjectAsync(_dir.Path, "P", "Book");
        var path = _files.CombinePath(_project.ProjectRoot!, ".novalist", "wiki", "hero.json");
        await _files.CreateDirectoryAsync(_files.CombinePath(_project.ProjectRoot!, ".novalist", "wiki"));
        await _files.WriteTextAsync(path, "{ not valid json ]");

        Assert.Null(await _cache.ReadAsync("hero"));
    }
}
