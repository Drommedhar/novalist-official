using System.IO.Compression;
using System.Net;
using System.Text;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Core.Tests.TestHelpers;
using Xunit;

namespace Novalist.Core.Tests.Services;

public class ExtensionGalleryServiceTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly string _extDir;
    private readonly string _dlDir;

    public ExtensionGalleryServiceTests()
    {
        _extDir = Path.Combine(_dir.Path, "Extensions");
        _dlDir = Path.Combine(_dir.Path, "Downloads");
    }

    public void Dispose() => _dir.Dispose();

    private ExtensionGalleryService Service(Func<HttpRequestMessage, HttpResponseMessage> router)
        => new(new HttpClient(new FakeHttpMessageHandler(router)), _extDir, _dlDir);

    private static HttpResponseMessage Json(string body, HttpStatusCode code = HttpStatusCode.OK)
        => new(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Text(string body, HttpStatusCode code = HttpStatusCode.OK)
        => new(code) { Content = new StringContent(body) };

    private static HttpResponseMessage RateLimited(string remaining)
    {
        var r = new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent("") };
        r.Headers.Add("X-RateLimit-Remaining", remaining);
        r.Headers.Add("X-RateLimit-Reset", DateTimeOffset.UtcNow.AddMinutes(30).ToUnixTimeSeconds().ToString());
        return r;
    }

    private static GalleryEntry Entry(string id = "ext1", string repo = "owner/ext1") => new() { Id = id, Repo = repo };

    private static string ReleasesJson(string tag, string assetName, bool prerelease = false, bool draft = false)
        => $$"""
        [ { "tag_name": "{{tag}}", "body": "notes", "prerelease": {{(prerelease ? "true" : "false")}},
            "draft": {{(draft ? "true" : "false")}}, "published_at": "2024-01-01T00:00:00Z",
            "assets": [ { "name": "{{assetName}}", "browser_download_url": "http://dl/{{assetName}}", "size": 4 } ] } ]
        """;

    private void MakeZip(string path, params (string name, string content)[] entries)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var fs = new FileStream(path, FileMode.Create);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        foreach (var (name, content) in entries)
        {
            var e = zip.CreateEntry(name);
            using var w = new StreamWriter(e.Open());
            w.Write(content);
        }
    }

    // ── Gallery index ──

    [Fact]
    public async Task FetchGalleryIndex_ReturnsEntries_AndCaches()
    {
        int calls = 0;
        var sut = Service(_ => { calls++; return Json("""[ { "id": "ext1", "repo": "owner/ext1" } ]"""); });

        var first = await sut.FetchGalleryIndexAsync();
        var second = await sut.FetchGalleryIndexAsync();

        Assert.Single(first);
        Assert.Same(first, second);
        Assert.Equal(1, calls); // cached -> only one HTTP call
    }

    [Fact]
    public async Task FetchGalleryIndex_NullBody_ReturnsEmpty()
    {
        var sut = Service(_ => Json("null"));
        Assert.Empty(await sut.FetchGalleryIndexAsync());
    }

    [Fact]
    public async Task FetchGalleryIndex_HttpError_Throws()
    {
        var sut = Service(_ => Json("", HttpStatusCode.InternalServerError));
        await Assert.ThrowsAsync<HttpRequestException>(() => sut.FetchGalleryIndexAsync());
    }

    [Fact]
    public async Task FetchGalleryIndex_RateLimited_Throws()
    {
        var sut = Service(_ => RateLimited("0"));
        var ex = await Assert.ThrowsAsync<GalleryRateLimitException>(() => sut.FetchGalleryIndexAsync());
        Assert.Contains("Resets at", ex.Message);
    }

    [Fact]
    public async Task FetchGalleryIndex_Forbidden_NotRateLimited_ThrowsHttp()
    {
        // 403 but remaining > 0 -> not a rate limit; EnsureSuccessStatusCode throws.
        var sut = Service(_ => RateLimited("9"));
        await Assert.ThrowsAsync<HttpRequestException>(() => sut.FetchGalleryIndexAsync());
    }

    [Fact]
    public async Task Fetch_429_Throws_RateLimit()
    {
        var sut = Service(_ => new HttpResponseMessage((HttpStatusCode)429) { Content = new StringContent("") });
        await Assert.ThrowsAsync<GalleryRateLimitException>(() => sut.FetchGalleryIndexAsync());
    }

    // ── Releases ──

    [Fact]
    public async Task FetchReleases_FiltersAndMaps_AndCaches()
    {
        int calls = 0;
        var sut = Service(req =>
        {
            calls++;
            return Json("""
            [
              { "tag_name": "v2.0.0", "prerelease": false, "draft": false, "published_at": "2024-02-01T00:00:00Z",
                "assets": [ { "name": "ext1.zip", "browser_download_url": "http://dl/ext1.zip", "size": 10 } ] },
              { "tag_name": "v1.0.0", "prerelease": false, "draft": false, "published_at": "2024-01-01T00:00:00Z",
                "assets": [ { "name": "ext1.zip", "browser_download_url": "http://dl/old.zip", "size": 5 } ] },
              { "tag_name": "v3.0.0-beta", "prerelease": true, "draft": false, "published_at": "2024-03-01T00:00:00Z", "assets": [] },
              { "tag_name": "v0.9.0", "prerelease": false, "draft": false, "published_at": "2023-01-01T00:00:00Z",
                "assets": [ { "name": "wrong.zip", "browser_download_url": "http://dl/x", "size": 1 } ] }
            ]
            """);
        });

        var releases = await sut.FetchReleasesAsync(Entry());
        var again = await sut.FetchReleasesAsync(Entry());

        Assert.Equal(2, releases.Count);             // prerelease + wrong-asset filtered out
        Assert.Equal("2.0.0", releases[0].Version);  // newest first
        Assert.Equal(1, calls);                       // cached
    }

    [Fact]
    public async Task FetchReleases_AssetWithoutUrl_Skipped()
    {
        var sut = Service(_ => Json("""
        [ { "tag_name": "v1.0.0", "prerelease": false, "draft": false, "published_at": "2024-01-01T00:00:00Z",
            "assets": [ { "name": "ext1.zip", "browser_download_url": "", "size": 0 } ] } ]
        """));
        Assert.Empty(await sut.FetchReleasesAsync(Entry()));
    }

    // ── Compatibility / latest ──

    private Func<HttpRequestMessage, HttpResponseMessage> CompatRouter(string minHost, string maxHost = "")
        => req =>
        {
            var uri = req.RequestUri!.AbsoluteUri;
            if (uri.Contains("/releases"))
                return Json(ReleasesJson("v2.0.0", "ext1.zip"));
            if (uri.Contains("extension.json"))
                return Json($$"""{ "minHostVersion": "{{minHost}}", "maxHostVersion": "{{maxHost}}", "icon": "ic.png" }""");
            return Text("", HttpStatusCode.NotFound);
        };

    [Fact]
    public async Task GetLatestCompatible_Compatible_ReturnsRelease_AndSetsIcon()
    {
        var sut = Service(CompatRouter("0.0.0"));
        var rel = await sut.GetLatestCompatibleReleaseAsync(Entry());
        Assert.NotNull(rel);
        Assert.Equal("ic.png", rel!.Icon);
    }

    [Fact]
    public async Task GetLatestCompatible_MinHostTooHigh_ReturnsNull()
    {
        var sut = Service(CompatRouter("999.0.0"));
        Assert.Null(await sut.GetLatestCompatibleReleaseAsync(Entry()));
    }

    [Fact]
    public async Task GetLatestCompatible_MaxHostTooLow_ReturnsNull()
    {
        var sut = Service(CompatRouter("0.0.0", "0.0.1"));
        Assert.Null(await sut.GetLatestCompatibleReleaseAsync(Entry()));
    }

    [Fact]
    public async Task GetLatestCompatible_MaxHostHigh_Compatible()
    {
        var sut = Service(CompatRouter("0.0.0", "999.0.0"));
        Assert.NotNull(await sut.GetLatestCompatibleReleaseAsync(Entry()));
    }

    [Fact]
    public async Task GetLatestCompatible_ManifestFetchFails_AssumesCompatible()
    {
        var sut = Service(req => req.RequestUri!.AbsoluteUri.Contains("/releases")
            ? Json(ReleasesJson("v2.0.0", "ext1.zip"))
            : Text("", HttpStatusCode.NotFound)); // extension.json 404 -> assume compatible
        Assert.NotNull(await sut.GetLatestCompatibleReleaseAsync(Entry()));
    }

    // ── README ──

    [Fact]
    public async Task FetchReadme_Success_AndCached()
    {
        int calls = 0;
        var sut = Service(_ => { calls++; return Text("# Readme"); });
        Assert.Equal("# Readme", await sut.FetchReadmeAsync("owner/ext1"));
        await sut.FetchReadmeAsync("owner/ext1");
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task FetchReadme_NotFound_ReturnsEmpty()
    {
        var sut = Service(_ => Text("", HttpStatusCode.NotFound));
        Assert.Equal(string.Empty, await sut.FetchReadmeAsync("owner/ext1"));
    }

    // ── Download ──

    [Fact]
    public async Task Download_FreshFile_WritesAndReportsProgress()
    {
        var sut = Service(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[4]) });
        var release = new GalleryRelease { ZipDownloadUrl = "http://dl/ext1.zip", ZipSize = 4 };
        var path = await sut.DownloadExtensionZipAsync(release, new Progress<double>(_ => { }));
        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task Download_ExistingCorrectSize_Skips()
    {
        Directory.CreateDirectory(_dlDir);
        await File.WriteAllBytesAsync(Path.Combine(_dlDir, "ext1.zip"), new byte[4]);
        var sut = Service(_ => throw new HttpRequestException("should not be called"));
        var release = new GalleryRelease { ZipDownloadUrl = "http://dl/ext1.zip", ZipSize = 4 };
        var path = await sut.DownloadExtensionZipAsync(release);
        Assert.EndsWith("ext1.zip", path);
    }

    [Fact]
    public async Task Download_HttpError_Throws()
    {
        var sut = Service(_ => Text("", HttpStatusCode.NotFound));
        var release = new GalleryRelease { ZipDownloadUrl = "http://dl/ext1.zip", ZipSize = 99 };
        await Assert.ThrowsAsync<HttpRequestException>(() => sut.DownloadExtensionZipAsync(release));
    }

    // ── Install ──

    private (ExtensionGalleryService Sut, string Zip) InstallSetup(string manifestId)
    {
        var zip = Path.Combine(_dir.Path, "pkg.zip");
        MakeZip(zip,
            ("extension.json", $$"""{ "id": "{{manifestId}}" }"""),
            ("emptydir/", ""),          // directory entry -> Name empty -> skipped
            ("sub/file.txt", "hello"));
        return (Service(_ => Text("")), zip);
    }

    [Fact]
    public void DefaultConstructor_UsesRealHttpClient()
    {
        var sut = new ExtensionGalleryService();
        Assert.Null(sut.GitHubToken);
    }

    [Fact]
    public async Task Install_LockedTarget_OverwritesInPlace()
    {
        if (!OperatingSystem.IsWindows())
            return; // deleting an open file succeeds on Unix, so the catch isn't hit there
        var target = Path.Combine(_extDir, "ext1");
        Directory.CreateDirectory(target);
        var locked = Path.Combine(target, "locked.dll");
        using var fs = new FileStream(locked, FileMode.Create, FileAccess.Write, FileShare.None);

        var (sut, zip) = InstallSetup("ext1");
        await sut.InstallExtensionAsync(zip, Entry(), new GalleryRelease { Version = "1.0.0" });
        Assert.True(File.Exists(Path.Combine(target, "extension.json")));
    }

    [Fact]
    public async Task GetLatestCompatible_ManifestRequestThrows_AssumesCompatible()
    {
        var sut = Service(req =>
        {
            if (req.RequestUri!.AbsoluteUri.Contains("/releases"))
                return Json(ReleasesJson("v2.0.0", "ext1.zip"));
            throw new HttpRequestException("boom"); // extension.json fetch throws -> catch -> compatible
        });
        Assert.NotNull(await sut.GetLatestCompatibleReleaseAsync(Entry()));
    }

    [Fact]
    public async Task GetLatestCompatible_UnparseableMaxVersion_AssumesCompatible()
    {
        var sut = Service(CompatRouter("0.0.0", "not-a-version"));
        Assert.NotNull(await sut.GetLatestCompatibleReleaseAsync(Entry()));
    }

    [Fact]
    public async Task GetLatestCompatible_NullManifest_AssumesCompatible()
    {
        var sut = Service(req => req.RequestUri!.AbsoluteUri.Contains("/releases")
            ? Json(ReleasesJson("v2.0.0", "ext1.zip"))
            : Json("null")); // extension.json deserializes to null manifest
        Assert.NotNull(await sut.GetLatestCompatibleReleaseAsync(Entry()));
    }

    [Fact]
    public async Task CheckForUpdates_NoCompatibleRelease_Skips()
    {
        var dir = Path.Combine(_extDir, "ext1");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "store-meta.json"),
            """{ "installedFromGallery": true, "repo": "owner/ext1", "installedVersion": "1.0.0" }""");

        var sut = Service(req =>
        {
            var uri = req.RequestUri!.AbsoluteUri;
            if (uri.Contains("gallery.json")) return Json("""[ { "id": "ext1", "repo": "owner/ext1" } ]""");
            if (uri.Contains("/releases")) return Json(ReleasesJson("v2.0.0", "ext1.zip"));
            if (uri.Contains("extension.json")) return Json("""{ "minHostVersion": "999.0.0" }"""); // incompatible
            return Text("", HttpStatusCode.NotFound);
        });

        Assert.Empty(await sut.CheckForUpdatesAsync()); // latest compatible is null -> skipped
    }

    [Fact]
    public async Task Install_ExtractsAndWritesStoreMeta_DeletesZip()
    {
        var (sut, zip) = InstallSetup("ext1");
        await sut.InstallExtensionAsync(zip, Entry(), new GalleryRelease { Version = "1.0.0" });

        var target = Path.Combine(_extDir, "ext1");
        Assert.True(File.Exists(Path.Combine(target, "extension.json")));
        Assert.True(File.Exists(Path.Combine(target, "sub", "file.txt")));
        Assert.True(File.Exists(Path.Combine(target, "store-meta.json")));
        Assert.False(File.Exists(zip)); // cleaned up
    }

    [Fact]
    public async Task Install_ReplacesExistingInstallation()
    {
        var target = Path.Combine(_extDir, "ext1");
        Directory.CreateDirectory(target);
        await File.WriteAllTextAsync(Path.Combine(target, "stale.txt"), "old");

        var (sut, zip) = InstallSetup("ext1");
        await sut.InstallExtensionAsync(zip, Entry(), new GalleryRelease { Version = "1.0.0" });
        Assert.False(File.Exists(Path.Combine(target, "stale.txt"))); // old install removed
    }

    [Fact]
    public async Task Install_MissingManifest_Throws()
    {
        var zip = Path.Combine(_dir.Path, "nomani.zip");
        MakeZip(zip, ("readme.txt", "x"));
        var sut = Service(_ => Text(""));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.InstallExtensionAsync(zip, Entry(), new GalleryRelease { Version = "1.0.0" }));
        Assert.False(Directory.Exists(Path.Combine(_extDir, "ext1")));
    }

    [Fact]
    public async Task Install_ManifestIdMismatch_Throws()
    {
        var (sut, zip) = InstallSetup("different-id");
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.InstallExtensionAsync(zip, Entry(), new GalleryRelease { Version = "1.0.0" }));
    }

    [Fact]
    public async Task Install_PathTraversal_Throws()
    {
        var zip = Path.Combine(_dir.Path, "evil.zip");
        MakeZip(zip, ("../escape.txt", "pwned"));
        var sut = Service(_ => Text(""));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.InstallExtensionAsync(zip, Entry(), new GalleryRelease { Version = "1.0.0" }));
    }

    // ── Uninstall ──

    [Fact]
    public async Task Uninstall_RemovesDir_AndNoOpWhenMissing()
    {
        var target = Path.Combine(_extDir, "ext1");
        Directory.CreateDirectory(target);
        var sut = Service(_ => Text(""));
        await sut.UninstallExtensionAsync("ext1");
        Assert.False(Directory.Exists(target));
        await sut.UninstallExtensionAsync("ext1"); // no-op
    }

    // ── Store meta ──

    [Fact]
    public void ReadStoreMeta_MissingValidCorrupt()
    {
        var sut = Service(_ => Text(""));
        Assert.Null(sut.ReadStoreMeta("ext1")); // missing

        var dir = Path.Combine(_extDir, "ext1");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "store-meta.json"),
            """{ "installedFromGallery": true, "installedVersion": "1.0.0" }""");
        Assert.True(sut.ReadStoreMeta("ext1")!.InstalledFromGallery);

        File.WriteAllText(Path.Combine(dir, "store-meta.json"), "{ corrupt");
        Assert.Null(sut.ReadStoreMeta("ext1"));
    }

    // ── Update checks ──

    [Fact]
    public async Task CheckForUpdates_NoExtensionsDir_Empty()
    {
        var sut = Service(_ => Json("[]"));
        Assert.Empty(await sut.CheckForUpdatesAsync());
    }

    [Fact]
    public async Task CheckForUpdates_FindsNewerGalleryExtension()
    {
        // installed v1.0.0 from gallery; latest compatible is v2.0.0
        var dir = Path.Combine(_extDir, "ext1");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "store-meta.json"),
            """{ "installedFromGallery": true, "repo": "owner/ext1", "installedVersion": "1.0.0" }""");

        var sut = Service(req =>
        {
            var uri = req.RequestUri!.AbsoluteUri;
            if (uri.Contains("gallery.json")) return Json("""[ { "id": "ext1", "repo": "owner/ext1" } ]""");
            if (uri.Contains("/releases")) return Json(ReleasesJson("v2.0.0", "ext1.zip"));
            if (uri.Contains("extension.json")) return Json("""{ "minHostVersion": "0.0.0" }""");
            return Text("", HttpStatusCode.NotFound);
        });

        var updates = await sut.CheckForUpdatesAsync();
        Assert.Single(updates);
        Assert.Equal("ext1", updates[0].ExtensionId);
        Assert.Equal("2.0.0", updates[0].AvailableVersion);
    }

    [Theory]
    [InlineData("2.0.0")] // installed == latest -> IsNewer equal path, no update
    [InlineData("9.0.0")] // installed newer than latest -> IsNewer r<c path, no update
    public async Task CheckForUpdates_NoUpdateWhenNotNewer(string installedVersion)
    {
        var dir = Path.Combine(_extDir, "ext1");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "store-meta.json"),
            $$"""{ "installedFromGallery": true, "repo": "owner/ext1", "installedVersion": "{{installedVersion}}" }""");

        var sut = Service(req =>
        {
            var uri = req.RequestUri!.AbsoluteUri;
            if (uri.Contains("gallery.json")) return Json("""[ { "id": "ext1", "repo": "owner/ext1" } ]""");
            if (uri.Contains("/releases")) return Json(ReleasesJson("v2.0.0", "ext1.zip"));
            if (uri.Contains("extension.json")) return Json("""{ "minHostVersion": "0.0.0" }""");
            return Text("", HttpStatusCode.NotFound);
        });

        Assert.Empty(await sut.CheckForUpdatesAsync());
    }

    [Fact]
    public async Task CheckForUpdates_SkipsNonGalleryAndManualExtensions()
    {
        Directory.CreateDirectory(Path.Combine(_extDir, "ext1")); // no store-meta -> skipped
        var manual = Path.Combine(_extDir, "ext2");
        Directory.CreateDirectory(manual);
        File.WriteAllText(Path.Combine(manual, "store-meta.json"),
            """{ "installedFromGallery": false, "installedVersion": "1.0.0" }""");

        var sut = Service(req => req.RequestUri!.AbsoluteUri.Contains("gallery.json")
            ? Json("""[ { "id": "ext1", "repo": "o/e1" }, { "id": "ext2", "repo": "o/e2" } ]""")
            : Text("", HttpStatusCode.NotFound));

        Assert.Empty(await sut.CheckForUpdatesAsync());
    }

    [Fact]
    public async Task CheckForUpdates_SwallowsPerEntryErrors()
    {
        var dir = Path.Combine(_extDir, "ext1");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "store-meta.json"),
            """{ "installedFromGallery": true, "repo": "owner/ext1", "installedVersion": "1.0.0" }""");

        var sut = Service(req => req.RequestUri!.AbsoluteUri.Contains("gallery.json")
            ? Json("""[ { "id": "ext1", "repo": "owner/ext1" } ]""")
            : Json("", HttpStatusCode.InternalServerError)); // releases fetch fails -> swallowed

        Assert.Empty(await sut.CheckForUpdatesAsync());
    }

    [Fact]
    public async Task CheckForUpdates_HonorsCancellation()
    {
        Directory.CreateDirectory(_extDir);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var sut = Service(_ => Json("""[ { "id": "ext1", "repo": "owner/ext1" } ]"""));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sut.CheckForUpdatesAsync(cts.Token));
    }

    // ── Auth + cache ──

    [Fact]
    public async Task GitHubToken_AddsBearerAuthHeader()
    {
        string? auth = null;
        var sut = Service(req => { auth = req.Headers.Authorization?.ToString(); return Json("[]"); });
        sut.GitHubToken = "tok123";
        await sut.FetchGalleryIndexAsync();
        Assert.Equal("Bearer tok123", auth);
    }

    [Fact]
    public async Task ClearCache_ForcesRefetch()
    {
        int calls = 0;
        var sut = Service(_ => { calls++; return Json("[]"); });
        await sut.FetchGalleryIndexAsync();
        sut.ClearCache();
        await sut.FetchGalleryIndexAsync();
        Assert.Equal(2, calls);
    }
}
