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

    // ── Keeping what has already been made ──

    [Fact]
    public async Task Has_IsFalseUntilTheClipIsThereAndTrueAfterwards()
    {
        var cache = Cache();

        Assert.False(cache.Has("abc123.wav"));
        await cache.WriteAsAsync("abc123.wav", [1, 2, 3]);

        Assert.True(cache.Has("abc123.wav"));
    }

    [Fact]
    public async Task WriteAs_KeepsTheNameItWasGiven()
    {
        // Named for its recipe rather than for its own bytes, which is what
        // makes "have we spoken this line before" a look at the filesystem
        // instead of a minute inside a model.
        var cache = Cache();

        Assert.Equal("deadbeef01.wav", await cache.WriteAsAsync("deadbeef01.wav", [1, 2, 3]));
        Assert.Equal([1, 2, 3], await cache.ReadAsync("deadbeef01.wav"));
    }

    [Fact]
    public async Task WriteAs_ANameThatWouldReachOutsideTheCacheIsRefused()
    {
        // The name comes from a recipe and so is always ours - but a cache that
        // writes wherever it is told is one bug away from writing anywhere.
        var cache = Cache();

        var written = await cache.WriteAsAsync("../escaped.wav", [1, 2, 3]);

        Assert.DoesNotContain("..", written);
        Assert.NotNull(await cache.ReadAsync(written));
    }

    [Fact]
    public async Task Has_OfSomethingThatIsNotAName_IsFalseRatherThanALook()
    {
        await Cache().WriteAsAsync("cafe01.wav", [1, 2, 3]);

        Assert.False(Cache().Has("../cafe01.wav"));
        Assert.False(Cache().Has(string.Empty));
    }

    [Fact]
    public async Task Trim_DropsTheLeastRecentlyWantedAndKeepsTheRest()
    {
        // A book is hours of audio and clips outlive a reading now, so
        // something has to decide when enough is enough. What goes is the
        // chapter the writer has stopped working on.
        var cache = Cache();
        await cache.WriteAsAsync("0d0d0d.wav", new byte[400]);
        File.SetLastWriteTimeUtc(
            Path.Combine(_dir.Path, NarrationClipCache.FolderName, "0d0d0d.wav"),
            DateTime.UtcNow.AddHours(-2));
        await cache.WriteAsAsync("0e0e0e.wav", new byte[400]);

        cache.Trim(500);

        Assert.False(cache.Has("0d0d0d.wav"));
        Assert.True(cache.Has("0e0e0e.wav"));
    }

    [Fact]
    public async Task Trim_UnderTheCeilingChangesNothing()
    {
        var cache = Cache();
        await cache.WriteAsAsync("cafe01.wav", new byte[100]);

        cache.Trim(1000);

        Assert.True(cache.Has("cafe01.wav"));
    }

    [Fact]
    public void Trim_OfACacheThatWasNeverWrittenToIsNotAFault()
        => Cache().Trim(1000);

    [Fact]
    public async Task AClipBeingPlayedRightNow_IsNeitherTrimmedNorAFault()
    {
        // The cache is served to an audio element while the reading plays, so a
        // clip genuinely is open when a trim comes round. Losing the trim - or
        // the reading - over one locked file would be worse than keeping it a
        // moment longer.
        var cache = Cache();
        await cache.WriteAsAsync("0d0d0d.wav", new byte[400]);
        await cache.WriteAsAsync("0e0e0e.wav", new byte[400]);
        var path = Path.Combine(_dir.Path, NarrationClipCache.FolderName, "0d0d0d.wav");

        using (var held = new FileStream(
                   path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            // Held exclusively: the same shape as a clip the interface has open
            // and is streaming from. Neither keeping it warm nor trimming may
            // fail over it.
            Assert.True(cache.Has("0d0d0d.wav"));
            cache.Trim(100);
            Assert.True(cache.Has("0d0d0d.wav"));
        }

        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task Trim_KeepsWhatTheReadingIsAboutToPlayHoweverOldItIs()
    {
        // A writer listening to one chapter all afternoon is playing clips made
        // hours ago. Evicting those would make the cache useless to exactly the
        // person it is for.
        var cache = Cache();
        await cache.WriteAsAsync("0d0d0d.wav", new byte[400]);
        File.SetLastWriteTimeUtc(
            Path.Combine(_dir.Path, NarrationClipCache.FolderName, "0d0d0d.wav"),
            DateTime.UtcNow.AddHours(-2));
        await cache.WriteAsAsync("0e0e0e.wav", new byte[400]);

        cache.Trim(500, ["0d0d0d.wav"]);

        Assert.True(cache.Has("0d0d0d.wav"));
        Assert.False(cache.Has("0e0e0e.wav"));
    }
}
