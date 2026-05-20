using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using Novalist.Core.Services;
using Novalist.Core.Tests.TestHelpers;
using Xunit;

namespace Novalist.Core.Tests.Services;

public class UpdateServiceTests : IDisposable
{
    private readonly Func<OSPlatform, bool> _origOs = UpdateService.IsOsPlatform;
    private readonly Func<Architecture> _origArch = UpdateService.OsArchitecture;

    public void Dispose()
    {
        UpdateService.IsOsPlatform = _origOs;
        UpdateService.OsArchitecture = _origArch;
    }

    private static void OnlyOs(OSPlatform os) => UpdateService.IsOsPlatform = p => p == os;

    private static UpdateService WithJson(string json)
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        });
        return new UpdateService(new HttpClient(handler));
    }

    private static string Release(string tag, params string[] assetNames)
    {
        var assets = string.Join(",", assetNames.Select(n =>
            $$"""{ "name": "{{n}}", "browser_download_url": "http://dl/{{n}}", "size": 10 }"""));
        return $$"""{ "tag_name": "{{tag}}", "html_url": "http://h", "body": "notes", "assets": [ {{assets}} ] }""";
    }

    [Fact]
    public async Task CheckForUpdate_NullRelease_ReturnsNull()
    {
        var sut = WithJson("null");
        Assert.Null(await sut.CheckForUpdateAsync());
    }

    [Fact]
    public async Task CheckForUpdate_EmptyTag_ReturnsNull()
    {
        var sut = WithJson("""{ "tag_name": "" }""");
        Assert.Null(await sut.CheckForUpdateAsync());
    }

    [Fact]
    public async Task CheckForUpdate_NotNewer_ReturnsNull()
    {
        OnlyOs(OSPlatform.Windows);
        var sut = WithJson(Release("v0.0.0", "novalist-windows.exe"));
        Assert.Null(await sut.CheckForUpdateAsync());
    }

    [Fact]
    public async Task CheckForUpdate_SameVersion_ReturnsNull()
    {
        OnlyOs(OSPlatform.Windows);
        // Remote tag equal to the current host version exercises the all-parts-equal
        // path in IsNewer (loop completes, returns false).
        var current = Novalist.Core.VersionInfo.Version.Split('-')[0];
        var sut = WithJson(Release("v" + current, "novalist-windows.exe"));
        Assert.Null(await sut.CheckForUpdateAsync());
    }

    [Fact]
    public async Task CheckForUpdate_Windows_PicksExeAsset()
    {
        OnlyOs(OSPlatform.Windows);
        var sut = WithJson(Release("v99.0.0", "novalist-linux.AppImage", "novalist-windows.exe"));
        var info = await sut.CheckForUpdateAsync();
        Assert.Equal("novalist-windows.exe", info!.AssetName);
        Assert.Equal("99.0.0", info.Version);
        Assert.Equal("v99.0.0", info.TagName);
    }

    [Fact]
    public async Task CheckForUpdate_NoMatchingAsset_ReturnsNull()
    {
        OnlyOs(OSPlatform.Windows);
        var sut = WithJson(Release("v99.0.0", "novalist-linux.AppImage"));
        Assert.Null(await sut.CheckForUpdateAsync());
    }

    [Fact]
    public async Task CheckForUpdate_NoAssetsAtAll_ReturnsNull()
    {
        OnlyOs(OSPlatform.Windows);
        var sut = WithJson("""{ "tag_name": "v99.0.0", "assets": [] }""");
        Assert.Null(await sut.CheckForUpdateAsync());
    }

    [Fact]
    public async Task CheckForUpdate_MacArm64_PicksArmAsset()
    {
        OnlyOs(OSPlatform.OSX);
        UpdateService.OsArchitecture = () => Architecture.Arm64;
        var sut = WithJson(Release("v99.0.0", "novalist-macos-x64.dmg", "novalist-macos-arm64.dmg"));
        Assert.Equal("novalist-macos-arm64.dmg", (await sut.CheckForUpdateAsync())!.AssetName);
    }

    [Fact]
    public async Task CheckForUpdate_MacX64_PicksX64Asset()
    {
        OnlyOs(OSPlatform.OSX);
        UpdateService.OsArchitecture = () => Architecture.X64;
        var sut = WithJson(Release("v99.0.0", "novalist-macos-x64.dmg", "novalist-macos-arm64.dmg"));
        Assert.Equal("novalist-macos-x64.dmg", (await sut.CheckForUpdateAsync())!.AssetName);
    }

    [Fact]
    public async Task CheckForUpdate_Mac_NoArchMatch_FallsBackToFirst()
    {
        OnlyOs(OSPlatform.OSX);
        UpdateService.OsArchitecture = () => Architecture.Arm64;
        var sut = WithJson(Release("v99.0.0", "novalist-macos-universal.dmg"));
        Assert.Equal("novalist-macos-universal.dmg", (await sut.CheckForUpdateAsync())!.AssetName);
    }

    [Fact]
    public async Task CheckForUpdate_Linux_PicksAppImage()
    {
        OnlyOs(OSPlatform.Linux);
        var sut = WithJson(Release("v99.0.0", "novalist.AppImage"));
        Assert.Equal("novalist.AppImage", (await sut.CheckForUpdateAsync())!.AssetName);
    }

    [Fact]
    public async Task CheckForUpdate_UnknownOs_ReturnsNull()
    {
        UpdateService.IsOsPlatform = _ => false; // no platform matches
        var sut = WithJson(Release("v99.0.0", "novalist-windows.exe"));
        Assert.Null(await sut.CheckForUpdateAsync());
    }

    [Fact]
    public async Task Download_ExistingCorrectSize_Skips()
    {
        using var dir = new TempDir();
        var update = new UpdateInfo { AssetName = "a.bin", AssetSize = 4, DownloadUrl = "http://dl/a.bin" };
        await File.WriteAllBytesAsync(Path.Combine(dir.Path, "a.bin"), new byte[4]);
        // HTTP would throw if called; skip path must not call it.
        var sut = new UpdateService(new HttpClient(FakeHttpMessageHandler.Throwing(new HttpRequestException())), dir.Path);

        var path = await sut.DownloadUpdateAsync(update);
        Assert.Equal(Path.Combine(dir.Path, "a.bin"), path);
    }

    [Fact]
    public async Task Download_WrongSize_RedownloadsWithProgress()
    {
        using var dir = new TempDir();
        await File.WriteAllBytesAsync(Path.Combine(dir.Path, "a.bin"), new byte[1]); // wrong size -> deleted
        var payload = new byte[8];
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(payload)
        });
        var sut = new UpdateService(new HttpClient(handler), dir.Path);
        var update = new UpdateInfo { AssetName = "a.bin", AssetSize = 8, DownloadUrl = "http://dl/a.bin" };

        double last = 0;
        var path = await sut.DownloadUpdateAsync(update, new Progress<double>(p => last = p));

        Assert.Equal(8, new FileInfo(path).Length);
    }

    [Fact]
    public async Task Download_FreshFile_Downloads()
    {
        using var dir = new TempDir();
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(new byte[16])
        });
        var sut = new UpdateService(new HttpClient(handler), dir.Path);
        var update = new UpdateInfo { AssetName = "b.bin", AssetSize = 16, DownloadUrl = "http://dl/b.bin" };

        var path = await sut.DownloadUpdateAsync(update);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task Download_HttpError_Throws()
    {
        using var dir = new TempDir();
        var sut = new UpdateService(new HttpClient(new FakeHttpMessageHandler(HttpStatusCode.NotFound, "")), dir.Path);
        var update = new UpdateInfo { AssetName = "c.bin", AssetSize = 1, DownloadUrl = "http://dl/c.bin" };
        await Assert.ThrowsAsync<HttpRequestException>(() => sut.DownloadUpdateAsync(update));
    }
}
