using Xunit;

namespace Novalist.Core.Tests.Services;

public sealed partial class AndroidDocumentTreeTests
{
    [Fact]
    public async Task ReadingLocalSettingsDoesNotInvalidateExternalFolderMetadata()
    {
        using var fixture = new DocumentFixture();
        File.WriteAllText(fixture.Provider.Combine("scene.txt"), "scene");
        File.WriteAllText(fixture.Local.Combine("settings.json"), "{}");
        var path = Path.Combine(fixture.Mount, "scene.txt");
        using var scope = fixture.Files.UseCachedReads();
        Assert.Equal("scene", await fixture.Files.ReadTextAsync(path));
        var listings = fixture.Tree.ListCalls;
        using (fixture.Files.UseFreshReads())
            Assert.Equal("{}", await fixture.Files.ReadTextAsync(fixture.Local.Combine("settings.json")));
        Assert.Equal("scene", await fixture.Files.ReadTextAsync(path));
        Assert.Equal(listings, fixture.Tree.ListCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExpiryAndProjectReopeningDetectEditsWithIdenticalMetadata(bool reopen)
    {
        using var fixture = new DocumentFixture();
        var source = fixture.Provider.Combine("scene.txt");
        File.WriteAllText(source, "before");
        var timestamp = File.GetLastWriteTimeUtc(source);
        var path = Path.Combine(fixture.Mount, "scene.txt");
        using var scope = fixture.Files.UseCachedReads();
        Assert.Equal("before", await fixture.Files.ReadTextAsync(path));
        File.WriteAllText(source, "after!");
        File.SetLastWriteTimeUtc(source, timestamp);
        if (reopen) fixture.Files.InvalidateReadCache(fixture.Mount);
        else fixture.Clock.Now += TimeSpan.FromMinutes(5);
        Assert.Equal("after!", await fixture.Files.ReadTextAsync(path));
    }

    [Fact]
    public async Task ReadOnlyScreensReuseBytesAndReturnIndependentArrays()
    {
        using var fixture = new DocumentFixture();
        File.WriteAllText(fixture.Provider.Combine("scene.txt"), "scene");
        var path = Path.Combine(fixture.Mount, "scene.txt");
        var reads = 0;
        fixture.Tree.ReadOverride = file => { reads++; return File.ReadAllBytes(file); };

        var original = await fixture.Files.ReadBytesAsync(path);
        original[0] = 0;
        using (fixture.Files.UseCachedReads())
        {
            var cached = await fixture.Files.ReadBytesAsync(path);
            Assert.Equal((byte)'s', cached[0]);
            cached[0] = 0;
            Assert.Equal("scene", await fixture.Files.ReadTextAsync(path));
        }
        Assert.Equal(1, reads);
        Assert.Equal("scene", await fixture.Files.ReadTextAsync(path));
        Assert.Equal(2, reads);
    }

    [Fact]
    public async Task FreshScopeWithoutFileOperationsPreservesCachedFolderMetadata()
    {
        using var fixture = new DocumentFixture();
        File.WriteAllText(fixture.Provider.Combine("scene.txt"), "scene");
        var path = Path.Combine(fixture.Mount, "scene.txt");
        var reads = 0;
        fixture.Tree.ReadOverride = file => { reads++; return File.ReadAllBytes(file); };
        using var cached = fixture.Files.UseCachedReads();
        Assert.Equal("scene", await fixture.Files.ReadTextAsync(path));
        var listings = fixture.Tree.ListCalls;

        using (fixture.Files.UseFreshReads())
            await Task.Yield();

        Assert.Equal("scene", await fixture.Files.ReadTextAsync(path));
        Assert.Equal(listings, fixture.Tree.ListCalls);
        Assert.Equal(1, reads);
    }

    [Fact]
    public async Task FreshScopeReadsExternalReplacementEvenWithIdenticalMetadata()
    {
        using var fixture = new DocumentFixture();
        var source = fixture.Provider.Combine("scene.txt");
        File.WriteAllText(source, "before");
        var timestamp = File.GetLastWriteTimeUtc(source);
        var path = Path.Combine(fixture.Mount, "scene.txt");
        using (fixture.Files.UseCachedReads())
        {
            Assert.Equal("before", await fixture.Files.ReadTextAsync(path));
            File.WriteAllText(source, "after!");
            File.SetLastWriteTimeUtc(source, timestamp);
            using (fixture.Files.UseFreshReads())
                Assert.Equal("after!", await fixture.Files.ReadTextAsync(path));
            Assert.Equal("after!", await fixture.Files.ReadTextAsync(path));
        }
    }

    [Fact]
    public async Task CacheRevalidatesMetadataAndPermissions()
    {
        using var fixture = new DocumentFixture();
        var source = fixture.Provider.Combine("scene.txt");
        File.WriteAllText(source, "before");
        var path = Path.Combine(fixture.Mount, "scene.txt");
        using var scope = fixture.Files.UseCachedReads();
        Assert.Equal("before", await fixture.Files.ReadTextAsync(path));
        File.WriteAllText(source, "changed length");
        fixture.Clock.Now += TimeSpan.FromSeconds(31);
        Assert.Equal("changed length", await fixture.Files.ReadTextAsync(path));
        fixture.Tree.Granted = false;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Files.ReadTextAsync(path));
    }

    [Fact]
    public async Task UnknownTimestampsNeverReuseContent()
    {
        using var fixture = new DocumentFixture();
        var source = fixture.Provider.Combine("scene.txt");
        File.WriteAllText(source, "before");
        File.SetLastWriteTimeUtc(source, DateTime.UnixEpoch);
        var path = Path.Combine(fixture.Mount, "scene.txt");
        using var scope = fixture.Files.UseCachedReads();
        Assert.Equal("before", await fixture.Files.ReadTextAsync(path));
        File.WriteAllText(source, "after!");
        File.SetLastWriteTimeUtc(source, DateTime.UnixEpoch);
        Assert.Equal("after!", await fixture.Files.ReadTextAsync(path));
    }

    [Fact]
    public async Task WritesMovesAndDeletesInvalidateReadOnlyContent()
    {
        using var fixture = new DocumentFixture();
        var path = Path.Combine(fixture.Mount, "scene.txt");
        var renamed = Path.Combine(fixture.Mount, "renamed.txt");
        await fixture.Files.WriteTextAsync(path, "before");
        using var scope = fixture.Files.UseCachedReads();
        Assert.Equal("before", await fixture.Files.ReadTextAsync(path));
        await fixture.Files.WriteTextAsync(path, "after!");
        Assert.Equal("after!", await fixture.Files.ReadTextAsync(path));
        await fixture.Files.MoveFileAsync(path, renamed);
        Assert.Equal("after!", await fixture.Files.ReadTextAsync(renamed));
        await Assert.ThrowsAsync<FileNotFoundException>(() => fixture.Files.ReadTextAsync(path));
        await fixture.Files.DeleteFileAsync(renamed);
        await Assert.ThrowsAsync<FileNotFoundException>(() => fixture.Files.ReadTextAsync(renamed));
    }

    [Fact]
    public async Task ConcurrentFreshReadDoesNotInheritAnotherRequestsCacheScope()
    {
        using var fixture = new DocumentFixture();
        var source = fixture.Provider.Combine("scene.txt");
        File.WriteAllText(source, "before");
        var path = Path.Combine(fixture.Mount, "scene.txt");
        var warmed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cachedRead = Task.Run(async () =>
        {
            using var scope = fixture.Files.UseCachedReads();
            Assert.Equal("before", await fixture.Files.ReadTextAsync(path));
            warmed.SetResult();
            await release.Task;
        });
        try
        {
            await warmed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            fixture.Tree.ReadOverride = _ => throw new IOException("Provider failed");
            await Assert.ThrowsAsync<IOException>(() => fixture.Files.ReadTextAsync(path));
        }
        finally { release.SetResult(); }
        await cachedRead;
    }
}
