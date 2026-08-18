using Novalist.Core.Services;
using Novalist.Core.Tests.TestHelpers;
using Xunit;

namespace Novalist.Core.Tests.Services;

/// <summary>
/// Covers where rendered speech is put: beside the application rather than in
/// the project, named after its own bytes, and gone when the reading stops.
/// </summary>
public class NarrationClipCacheTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    private NarrationClipCache Cache() => new(_dir.Path);

    private static byte[] Audio(byte seed = 1) => [0x52, 0x49, 0x46, 0x46, seed];

    [Fact]
    public async Task WriteAsync_PutsTheClipUnderTheApplicationFolder()
    {
        var cache = Cache();

        var name = await cache.WriteAsync(Audio(), "wav");

        Assert.Equal(Path.Combine(_dir.Path, NarrationClipCache.FolderName), cache.Root);
        Assert.True(File.Exists(Path.Combine(cache.Root, name)));
        Assert.Equal(Audio(), await cache.ReadAsync(name));
    }

    [Fact]
    public async Task WriteAsync_NamesAClipAfterItsOwnBytes()
    {
        // So the same line rendered twice is written once, and so nothing the
        // writer typed can be read off the cache folder.
        var cache = Cache();

        var first = await cache.WriteAsync(Audio(), "wav");
        var again = await cache.WriteAsync(Audio(), "wav");
        var other = await cache.WriteAsync(Audio(2), "wav");

        Assert.Equal(first, again);
        Assert.NotEqual(first, other);
        Assert.Equal(first, NarrationClipCache.NameFor(Audio(), "wav"));
        Assert.Single(Directory.GetFiles(cache.Root), f => Path.GetFileName(f) == first);
    }

    [Theory]
    [InlineData("mp3", ".mp3")]
    [InlineData(".OPUS", ".opus")]
    [InlineData("", ".wav")]
    [InlineData("../escape", ".wav")]
    public async Task WriteAsync_NamesTheContainerOrFallsBackToWav(string format, string expected)
    {
        var name = await Cache().WriteAsync(Audio(), format);

        Assert.EndsWith(expected, name);
    }

    [Fact]
    public async Task ReadAsync_RefusesAnythingThatIsNotAClipName()
    {
        var cache = Cache();
        await cache.WriteAsync(Audio(), "wav");

        // A caller that passed a path would otherwise reach outside the cache.
        Assert.Null(await cache.ReadAsync("../../secrets.txt"));
        Assert.Null(await cache.ReadAsync("nothex.wav"));
        Assert.Null(await cache.ReadAsync("abcdef"));
        Assert.Null(await cache.ReadAsync(".wav"));
        Assert.Null(await cache.ReadAsync("abcdef."));
        Assert.Null(await cache.ReadAsync("   "));
        Assert.Null(await cache.ReadAsync(null));
    }

    [Fact]
    public async Task ReadAsync_AClipThatIsNotThereIsNull()
        => Assert.Null(await Cache().ReadAsync("00112233.wav"));

    [Fact]
    public async Task ReadAsync_AFileSomethingElseIsHoldingIsNullRatherThanACrash()
    {
        var cache = Cache();
        var name = await cache.WriteAsync(Audio(), "wav");

        using var hold = new FileStream(
            Path.Combine(cache.Root, name), FileMode.Open, FileAccess.Read, FileShare.None);

        Assert.Null(await cache.ReadAsync(name));
    }

    [Fact]
    public async Task Clear_EmptiesTheCache()
    {
        // Audio of somebody's manuscript should not outlive the sitting it was
        // made in.
        var cache = Cache();
        await cache.WriteAsync(Audio(1), "wav");
        await cache.WriteAsync(Audio(2), "wav");

        cache.Clear();

        Assert.Empty(Directory.GetFiles(cache.Root));
        Assert.Equal(0, cache.Size());
    }

    [Fact]
    public async Task Clear_LeavesBehindWhateverIsBeingPlayedRatherThanFailing()
    {
        var cache = Cache();
        var name = await cache.WriteAsync(Audio(), "wav");

        using var hold = new FileStream(
            Path.Combine(cache.Root, name), FileMode.Open, FileAccess.Read, FileShare.Read);
        cache.Clear();

        // It goes with the next clear rather than taking the stop with it.
        Assert.True(File.Exists(Path.Combine(cache.Root, name)));
    }

    [Fact]
    public void Clear_WithNothingWrittenYetDoesNothing()
    {
        var cache = Cache();

        cache.Clear();

        Assert.Equal(0, cache.Size());
        Assert.False(Directory.Exists(cache.Root));
    }

    [Fact]
    public async Task Size_ReportsHowMuchAudioIsSittingThere()
    {
        var cache = Cache();
        await cache.WriteAsync(Audio(1), "wav");
        await cache.WriteAsync(Audio(2), "wav");

        Assert.Equal(Audio(1).Length * 2, cache.Size());
    }

    [Fact]
    public async Task Size_CountsWhatItCanReadAndDoesNotFailOnTheRest()
    {
        var cache = Cache();
        await cache.WriteAsync(Audio(), "wav");

        Assert.True(cache.Size() > 0);
    }

    [Fact]
    public void ADefaultCacheSitsBesideTheOtherApplicationFiles()
    {
        // Not in the project: a repository should not grow by tens of megabytes
        // because somebody pressed Play.
        var cache = new NarrationClipCache();

        Assert.EndsWith(
            Path.Combine("Novalist", NarrationClipCache.FolderName), cache.Root);
    }

    [Fact]
    public async Task SeveralClipsAtOnce_ComeBackByName()
    {
        var cache = Cache();
        var first = await cache.WriteAsync([1, 2, 3], "wav");
        var second = await cache.WriteAsync([4, 5, 6], "wav");

        var found = await cache.ReadManyAsync([first, second]);

        Assert.Equal([1, 2, 3], found[first]);
        Assert.Equal([4, 5, 6], found[second]);
    }

    [Fact]
    public async Task AClipThatIsNoLongerThere_IsSimplyAbsent()
    {
        // A reference the writer pointed at, cleared since. The line is
        // performed on its vector instead; refusing to render at all would be
        // worse than reading it plainly.
        var cache = Cache();
        var present = await cache.WriteAsync([1, 2, 3], "wav");

        var found = await cache.ReadManyAsync([present, "gone.wav"]);

        Assert.Equal([present], found.Keys);
    }
}
