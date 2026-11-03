using NSubstitute;
using Novalist.Backend.Tests.TestHelpers;
using Novalist.Core.Services;
using Xunit;

namespace Novalist.Backend.Tests;

public sealed class CollaborationFileServiceTests
{
    [Fact]
    public async Task GuardForwardsProviderBatchCancellationAndReadCapabilities()
    {
        using var temp = new TempDir();
        var provider = Substitute.For(
            [typeof(IFileService), typeof(IBatchTextReader), typeof(IFileReadCache), typeof(IFileMetadataCache)], []);
        var files = (IFileService)provider;
        var batch = (IBatchTextReader)provider;
        var cache = (IFileReadCache)provider;
        var metadata = (IFileMetadataCache)provider;
        using var workspace = new Workspace(temp.Combine("settings"), fileService: files);
        var guarded = Assert.IsType<CollaborationFileService>(workspace.FileService);
        using var cancellation = new CancellationTokenSource();
        string[] paths = [temp.Combine("scene.novalist")];
        IReadOnlyDictionary<string, string> contents = new Dictionary<string, string> { [paths[0]] = "Draft" };
        batch.ReadTextsAsync(paths, cancellation.Token).Returns(contents);
        batch.ReadExistingTextsAsync(paths, cancellation.Token).Returns(contents);
        var cached = Substitute.For<IDisposable>();
        var fresh = Substitute.For<IDisposable>();
        cache.UseCachedReads().Returns(cached);
        cache.UseFreshReads().Returns(fresh);

        Assert.Same(contents, await guarded.ReadTextsAsync(paths, cancellation.Token));
        Assert.Same(contents, await guarded.ReadExistingTextsAsync(paths, cancellation.Token));
        Assert.Same(cached, guarded.UseCachedReads());
        Assert.Same(fresh, guarded.UseFreshReads());
        await guarded.RefreshMetadataAsync(paths[0]);

        await metadata.Received(1).RefreshMetadataAsync(paths[0]);
        await batch.Received(1).ReadTextsAsync(paths, cancellation.Token);
        await batch.Received(1).ReadExistingTextsAsync(paths, cancellation.Token);
        await files.DidNotReceive().ReadTextAsync(Arg.Any<string>());
    }

    [Fact]
    public async Task OrdinaryFilesKeepTextWritesAndSupportOptionalReadCapabilities()
    {
        using var temp = new TempDir();
        var files = Substitute.For<IFileService>();
        using var workspace = new Workspace(temp.Combine("settings"), fileService: files);
        var guarded = Assert.IsType<CollaborationFileService>(workspace.FileService);
        var path = temp.Combine("scene.novalist");
        files.ExistsAsync(path).Returns(true);
        files.ReadTextAsync(path).Returns("Draft");

        using (guarded.UseCachedReads())
        using (guarded.UseFreshReads())
        {
            Assert.Equal("Draft", (await guarded.ReadTextsAsync([path]))[path]);
            Assert.Equal("Draft", (await guarded.ReadExistingTextsAsync([path]))[path]);
            await guarded.RefreshMetadataAsync(path);
        }
        await guarded.WriteTextAsync(path, "Updated draft");

        await files.Received(1).WriteTextAsync(path, "Updated draft");
        await files.DidNotReceive().WriteBytesAsync(Arg.Any<string>(), Arg.Any<byte[]>());
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => guarded.ReadTextsAsync([path], cancelled.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => guarded.ReadExistingTextsAsync([path], cancelled.Token));
    }
}
