using Novalist.Backend.Tests.TestHelpers;
using Novalist.Core.Services;
using NSubstitute;
using Xunit;

namespace Novalist.Backend.Tests;

public sealed class ExportAssetStagingTests
{
    [Fact]
    public async Task StagedAssetsKeepDistinctNamesAndAreRemovedOnDisposal()
    {
        using var temp = new TempDir();
        var files = Substitute.For<IFileService>();
        var first = temp.Combine("first", "cover.png");
        var second = temp.Combine("second", "cover.png");
        files.ExistsAsync(Arg.Any<string>()).Returns(true);
        files.ReadBytesAsync(first).Returns([1, 2, 3]);
        files.ReadBytesAsync(second).Returns([4, 5, 6]);
        string stagedFirst, stagedSecond;

        using (var staging = new ExportAssetStaging(files))
        {
            stagedFirst = await staging.MaterializeAsync(first);
            stagedSecond = await staging.MaterializeAsync(second);
            Assert.Equal(stagedFirst, await staging.MaterializeAsync(first));
            Assert.Equal("cover.png", Path.GetFileName(stagedFirst));
            Assert.Equal("cover.png", Path.GetFileName(stagedSecond));
            Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(stagedFirst));
            Assert.Equal(new byte[] { 4, 5, 6 }, await File.ReadAllBytesAsync(stagedSecond));
        }

        Assert.False(File.Exists(stagedFirst));
        Assert.False(File.Exists(stagedSecond));
        Assert.False(Directory.Exists(Path.GetDirectoryName(Path.GetDirectoryName(stagedFirst))));
    }

    [Fact]
    public async Task LocalMissingAndRemoteUrlsRetainTheirExistingMeaning()
    {
        using var temp = new TempDir();
        var local = temp.Combine("local.png");
        await File.WriteAllBytesAsync(local, [1]);
        var missing = temp.Combine("missing.png");
        using (var staging = new ExportAssetStaging(new FileService()))
        {
            Assert.Equal(local, await staging.MaterializeAsync(local));
            Assert.Equal(missing, await staging.MaterializeAsync(missing));
            Assert.Equal("https://example.test/image.png", await staging.MaterializeAsync("https://example.test/image.png"));
        }
        Assert.Equal(new byte[] { 1 }, await File.ReadAllBytesAsync(local));
    }

    [Fact]
    public async Task FailedReadDoesNotRetainEarlierStagedAssets()
    {
        using var temp = new TempDir();
        var files = Substitute.For<IFileService>();
        var first = temp.Combine("first.png");
        var second = temp.Combine("second.png");
        files.ExistsAsync(Arg.Any<string>()).Returns(true);
        files.ReadBytesAsync(first).Returns([1]);
        files.ReadBytesAsync(second).Returns(Task.FromException<byte[]>(new IOException("Offline")));
        string staged;
        using (var staging = new ExportAssetStaging(files))
        {
            staged = await staging.MaterializeAsync(first);
            await Assert.ThrowsAsync<IOException>(() => staging.MaterializeAsync(second));
        }
        Assert.False(File.Exists(staged));
    }
}
