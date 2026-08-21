using System.IO.Compression;
using System.Net;
using System.Text;
using Novalist.Backend;
using Novalist.Backend.Extensions;
using Novalist.Backend.Rpc;
using Novalist.Backend.Tests.TestHelpers;
using Xunit;

namespace Novalist.Backend.Tests;

/// <summary>
/// Exercises the extension-store RPC facade end-to-end against a faked GitHub
/// (no real network): browse, readme, releases, update-check, and download +
/// install of a real sample extension that then loads live in the host.
/// </summary>
// Serialized with the other extension tests: the disabled case moves
// NOVALIST_EXTENSIONS_DISABLED, which is process-wide.
[Collection("BackendStatics")]
public sealed class ExtensionStoreRpcTests : IDisposable
{
    private const string SampleId = "com.novalist.sample";
    private const string Repo = "owner/sample";
    private const string GalleryUrl =
        "https://raw.githubusercontent.com/Drommedhar/novalist-extension-gallery/main/gallery.json";
    private const string ZipUrl = "https://dl.test/com.novalist.sample.zip";

    private readonly TempDir _root = new();
    private readonly string _extDir;
    private readonly string _dlDir;
    private readonly Workspace _workspace;

    public ExtensionStoreRpcTests()
    {
        _extDir = _root.Combine("Extensions");
        _dlDir = _root.Combine("Downloads");
        _workspace = new Workspace(_root.Combine("settings"));
        _workspace.Settings.LoadAsync().GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _workspace.Dispose();
        _root.Dispose();
    }

    // ── Fake network ────────────────────────────────────────────────

    private sealed class Handler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _router;
        public Handler(Func<HttpRequestMessage, HttpResponseMessage> router) => _router = router;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(_router(request));
        }
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode code = HttpStatusCode.OK)
        => new(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Text(string body)
        => new(HttpStatusCode.OK) { Content = new StringContent(body) };

    private static HttpResponseMessage Bytes(byte[] body)
        => new(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };

    private static string IndexJson(string id = SampleId, string repo = Repo)
        => $$"""[ { "id": "{{id}}", "name": "Sample", "description": "A sample", "author": "Tests", "repo": "{{repo}}", "tags": ["writing"] } ]""";

    private static string ReleasesJson(string tag = "v1.0.0", string id = SampleId, string zipUrl = ZipUrl)
        => $$"""
        [ { "tag_name": "{{tag}}", "body": "release notes", "prerelease": false, "draft": false,
            "published_at": "2024-01-01T00:00:00Z",
            "assets": [ { "name": "{{id}}.zip", "browser_download_url": "{{zipUrl}}", "size": 0 } ] } ]
        """;

    private static string ManifestJson(string? minHost = null)
        => minHost == null
            ? $$"""{ "id": "{{SampleId}}", "name": "Sample", "icon": "icon.png", "entryAssembly": "Novalist.Sdk.Example.dll" }"""
            : $$"""{ "id": "{{SampleId}}", "name": "Sample", "minHostVersion": "{{minHost}}", "entryAssembly": "Novalist.Sdk.Example.dll" }""";

    /// <summary>Builds a real installable extension ZIP (manifest + sample DLL).</summary>
    private static byte[] SampleZip()
    {
        var dll = Path.Combine(AppContext.BaseDirectory, "Novalist.Sdk.Example.dll");
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var manifest = zip.CreateEntry("extension.json");
            using (var w = new StreamWriter(manifest.Open()))
                w.Write($$"""{ "id": "{{SampleId}}", "name": "Sample", "version": "1.0.0", "entryAssembly": "Novalist.Sdk.Example.dll" }""");
            var asset = zip.CreateEntry("Novalist.Sdk.Example.dll");
            using var assetStream = asset.Open();
            using var dllStream = File.OpenRead(dll);
            dllStream.CopyTo(assetStream);
        }
        return ms.ToArray();
    }

    private ExtensionStoreRpc Rpc(Func<HttpRequestMessage, HttpResponseMessage> router, bool withOverride = true)
    {
        if (withOverride)
            _workspace.ExtensionsLoaderOverride = new ExtensionLoader(_extDir);
        return new ExtensionStoreRpc(_workspace, new HttpClient(new Handler(router)), _dlDir);
    }

    /// <summary>Default happy-path router covering index, releases, compat manifest,
    /// readme, and the ZIP download.</summary>
    private HttpResponseMessage Route(HttpRequestMessage req, string? minHost = null)
    {
        var url = req.RequestUri!.ToString();
        if (url == GalleryUrl) return Json(IndexJson());
        if (url.EndsWith("/releases")) return Json(ReleasesJson());
        if (url.EndsWith("/readme")) return Text("# Sample\nHello");
        if (url.EndsWith("/extension.json")) return Text(ManifestJson(minHost));
        if (url.EndsWith(".zip")) return Bytes(SampleZip());
        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private void WriteStoreMeta(string id, string installedVersion)
    {
        var dir = Path.Combine(_extDir, id);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "store-meta.json"),
            $$"""{ "installedFromGallery": true, "repo": "{{Repo}}", "installedVersion": "{{installedVersion}}" }""");
    }

    // ── Browse ──────────────────────────────────────────────────────

    [Fact]
    public async Task Index_ListsEntry_WithCompatibleRelease()
    {
        var rpc = Rpc(r => Route(r));
        var entries = await rpc.IndexAsync();

        var e = Assert.Single(entries);
        Assert.Equal(SampleId, e.Id);
        Assert.Equal("Tests", e.Author);
        Assert.Equal("1.0.0", e.LatestVersion);
        Assert.Equal("v1.0.0", e.ReleaseTag);
        Assert.Equal("icon.png", e.Icon);
        Assert.True(e.IsCompatible);
        Assert.False(e.IsInstalled);
        Assert.False(e.HasUpdate);
        Assert.Contains("writing", e.Tags);
    }

    [Fact]
    public async Task Index_ReleaseFetchThrows_EntryStillListedIncompatible()
    {
        var rpc = Rpc(r =>
        {
            var url = r.RequestUri!.ToString();
            if (url == GalleryUrl) return Json(IndexJson());
            if (url.EndsWith("/releases")) return Json("", HttpStatusCode.InternalServerError);
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var e = Assert.Single(await rpc.IndexAsync());
        Assert.False(e.IsCompatible);
        Assert.Null(e.LatestVersion);
    }

    [Fact]
    public async Task Index_InstalledOlder_FlagsUpdate()
    {
        WriteStoreMeta(SampleId, "0.9.0-beta"); // dash-suffixed to exercise version parsing
        var rpc = Rpc(r => Route(r));

        var e = Assert.Single(await rpc.IndexAsync());
        Assert.True(e.IsInstalled);
        Assert.Equal("0.9.0-beta", e.InstalledVersion);
        Assert.True(e.HasUpdate);
    }

    [Fact]
    public async Task Index_InstalledSameOrNewer_NoUpdate()
    {
        WriteStoreMeta(SampleId, "1.0.0");
        var same = Assert.Single(await Rpc(r => Route(r)).IndexAsync());
        Assert.True(same.IsInstalled);
        Assert.False(same.HasUpdate);

        WriteStoreMeta(SampleId, "2.5.0"); // remote older than installed
        var newer = Assert.Single(await new ExtensionStoreRpc(
            _workspace, new HttpClient(new Handler(r => Route(r))), _dlDir).IndexAsync());
        Assert.False(newer.HasUpdate);
    }

    [Fact]
    public async Task Index_NoLoaderOverride_UsesDefaultDirectory()
    {
        // Empty gallery so no store-meta lookups touch the real extensions dir.
        var rpc = new ExtensionStoreRpc(
            _workspace, new HttpClient(new Handler(_ => Json("[]"))), _dlDir);
        Assert.Empty(await rpc.IndexAsync());
    }

    [Fact]
    public async Task Readme_ReturnsMarkdown_AndReusesCachedGallery()
    {
        var rpc = Rpc(r => Route(r));
        // First call builds the gallery; second reuses the cached instance.
        _ = await rpc.IndexAsync();
        var readme = await rpc.ReadmeAsync(Repo);
        Assert.Contains("# Sample", readme);
    }

    [Fact]
    public async Task Releases_ReturnsPublishedReleases()
    {
        var rpc = Rpc(r => Route(r));
        var releases = await rpc.ReleasesAsync(SampleId, Repo);
        var rel = Assert.Single(releases);
        Assert.Equal("v1.0.0", rel.TagName);
        Assert.Equal("1.0.0", rel.Version);
        Assert.Equal("release notes", rel.Body);
    }

    [Fact]
    public async Task CheckUpdates_ReturnsAvailableUpdate()
    {
        WriteStoreMeta(SampleId, "0.9.0");
        var rpc = Rpc(r => Route(r));

        var updates = await rpc.CheckUpdatesAsync();
        var u = Assert.Single(updates);
        Assert.Equal(SampleId, u.ExtensionId);
        Assert.Equal("0.9.0", u.InstalledVersion);
        Assert.Equal("1.0.0", u.AvailableVersion);
    }

    // ── Install / Update ────────────────────────────────────────────

    /// <summary>The Mac App Store build asks the gallery nothing and installs
    /// nothing: it could not load what came back. The router throws, so a request
    /// that slipped through fails the test rather than passing quietly.</summary>
    [Fact]
    public async Task Store_Disabled_MakesNoRequestAndRefusesInstall()
    {
        var prev = Environment.GetEnvironmentVariable("NOVALIST_EXTENSIONS_DISABLED");
        try
        {
            Environment.SetEnvironmentVariable("NOVALIST_EXTENSIONS_DISABLED", "1");
            var rpc = Rpc(_ => throw new InvalidOperationException("no request expected"));

            Assert.Empty(await rpc.IndexAsync());

            var install = await rpc.InstallAsync(SampleId, Repo);
            Assert.False(install.Success);
            Assert.Equal("disabled", install.Error);

            var update = await rpc.UpdateAsync(SampleId, Repo);
            Assert.False(update.Success);
            Assert.Equal("disabled", update.Error);

            Assert.False(Directory.Exists(Path.Combine(_extDir, SampleId)));
        }
        finally
        {
            Environment.SetEnvironmentVariable("NOVALIST_EXTENSIONS_DISABLED", prev);
        }
    }


    [Fact]
    public async Task Install_DownloadsInstallsAndLoadsLive_WithProgress()
    {
        var progressEvents = new List<string>();
        _workspace.UiBridge.Notifier = (method, _) => { progressEvents.Add(method); return Task.CompletedTask; };
        var rpc = Rpc(r => Route(r));

        var result = await rpc.InstallAsync(SampleId, Repo);

        Assert.True(result.Success);
        Assert.Null(result.Error);
        // The extension is now installed on disk AND loaded live in the host.
        Assert.True(File.Exists(Path.Combine(_extDir, SampleId, "extension.json")));
        Assert.True(File.Exists(Path.Combine(_extDir, SampleId, "store-meta.json")));
        var loaded = _workspace.ExtensionsHost.Extensions.Single(e => e.Manifest.Id == SampleId);
        Assert.True(loaded.IsLoaded);
        // Progress bridge drove open + update + close notifications.
        Assert.Contains("ui/progress/open", progressEvents);
        Assert.Contains("ui/progress/close", progressEvents);
    }

    [Fact]
    public async Task Update_UsesSameDownloadInstallFlow()
    {
        var rpc = Rpc(r => Route(r));
        var result = await rpc.UpdateAsync(SampleId, Repo);
        Assert.True(result.Success);
        Assert.True(_workspace.ExtensionsHost.Extensions.Single(e => e.Manifest.Id == SampleId).IsLoaded);
    }

    [Fact]
    public async Task Install_IdNotInGallery_UsesRepoFallback()
    {
        // Gallery index does not contain the id; ResolveEntryAsync falls back to
        // a minimal entry built from the id + repo.
        var rpc = Rpc(r =>
        {
            var url = r.RequestUri!.ToString();
            if (url == GalleryUrl) return Json("[]");
            return Route(r);
        });

        var result = await rpc.InstallAsync(SampleId, Repo);
        Assert.True(result.Success);
    }

    [Fact]
    public async Task Install_Incompatible_ReturnsIncompatible()
    {
        var rpc = Rpc(r => Route(r, minHost: "999.0.0"));
        var result = await rpc.InstallAsync(SampleId, Repo);
        Assert.False(result.Success);
        Assert.Equal("incompatible", result.Error);
    }

    [Fact]
    public async Task Install_Cancelled_ReturnsCancelled()
    {
        var rpc = Rpc(r =>
        {
            if (r.RequestUri!.ToString().EndsWith(".zip"))
                throw new TaskCanceledException();
            return Route(r);
        });
        var result = await rpc.InstallAsync(SampleId, Repo);
        Assert.False(result.Success);
        Assert.Equal("cancelled", result.Error);
    }

    [Fact]
    public async Task Install_DownloadFails_ReturnsError()
    {
        var rpc = Rpc(r =>
        {
            if (r.RequestUri!.ToString().EndsWith(".zip"))
                return Json("", HttpStatusCode.InternalServerError);
            return Route(r);
        });
        var result = await rpc.InstallAsync(SampleId, Repo);
        Assert.False(result.Success);
        Assert.False(string.IsNullOrEmpty(result.Error));
        Assert.NotEqual("cancelled", result.Error);
    }

    [Fact]
    public async Task Install_AppliesGitHubTokenFromSettings()
    {
        _workspace.Settings.Settings.GitHubToken = "tok123";
        string? auth = null;
        var rpc = Rpc(r =>
        {
            auth ??= r.Headers.Authorization?.ToString();
            return Route(r);
        });

        var result = await rpc.InstallAsync(SampleId, Repo);
        Assert.True(result.Success);
        Assert.Equal("Bearer tok123", auth);
    }
}
