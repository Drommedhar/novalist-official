using Novalist.Core.Services;
using Novalist.Core.Tests.TestHelpers;
using Xunit;

namespace Novalist.Core.Tests.Services;

public class NarrationFileLifetimeTests
{
    [Theory]
    [InlineData(FileShare.Read)]
    [InlineData(FileShare.None)]
    public async Task Clear_KeepsAnOpenClipPlayableUntilItsReaderCloses(FileShare sharing)
    {
        using var directory = new TempDir();
        var cache = new NarrationClipCache(directory.Path);
        byte[] audio = [0x52, 0x49, 0x46, 0x46];
        var name = await cache.WriteAsync(audio, "wav");
        var path = Path.Combine(cache.Root, name);

        using (var reader = new FileStream(path, FileMode.Open, FileAccess.Read, sharing))
        {
            cache.Clear();

            // Unix unlink would leave this reader alive while breaking the URL
            // used by later audio range requests. Both must remain usable.
            Assert.True(cache.Has(name));
            var remaining = new byte[audio.Length];
            await reader.ReadExactlyAsync(remaining);
            Assert.Equal(audio, remaining);
        }

        cache.Clear();

        Assert.False(cache.Has(name));
        Assert.Equal(0, cache.Size());
    }
}
