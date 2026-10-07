using Novalist.Mobile.Services;
using Xunit;

namespace Novalist.Core.Tests.Services;

public sealed class MobileAssetReaderTests
{
    [Fact]
    public void ReadsExactBinaryContentWithoutTextConversion()
    {
        var path = Path.GetTempFileName();
        try
        {
            byte[] content = [0, 127, 128, 255];
            File.WriteAllBytes(path, content);
            Assert.Equal("data:video/mp4;base64," + Convert.ToBase64String(content),
                MobileAssetReader.ReadDataUri(path, "video/mp4"));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void RejectsOversizedPreviewBeforeReadingIt()
    {
        var path = Path.GetTempFileName();
        try
        {
            using (var stream = File.OpenWrite(path)) stream.SetLength(MobileAssetReader.MaximumBytes + 1L);
            var error = Assert.Throws<IOException>(() => MobileAssetReader.ReadDataUri(path, "video/mp4"));
            Assert.Contains("16 MiB limit", error.Message);
            Assert.Equal(MobileAssetReader.MaximumBytes + 1L, new FileInfo(path).Length);
        }
        finally { File.Delete(path); }
    }
}
