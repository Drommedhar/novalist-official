using System.Net;
using Novalist.Core.Tests.TestHelpers;
using Novalist.Desktop.Services;
using Xunit;

namespace Novalist.Desktop.Tests.Services;

// MapAssetServer is a static singleton (starts once), so all assertions run in
// one test against a single running instance.
public class MapAssetServerTests
{
    [Fact]
    public async Task ServesAssets_BookFiles_404s_AndRejectsTraversal()
    {
        using var assets = new TempDir();
        using var book = new TempDir();
        File.WriteAllText(Path.Combine(assets.Path, "map.html"), "<html>map</html>");
        File.WriteAllText(Path.Combine(assets.Path, "app.js"), "console.log(1)");
        File.WriteAllBytes(Path.Combine(book.Path, "pic.png"), new byte[] { 1, 2, 3 });

        string? bookRoot = book.Path;
        MapAssetServer.EnsureStarted(assets.Path, () => bookRoot);
        // Idempotent — a second call is a no-op.
        MapAssetServer.EnsureStarted(assets.Path, () => bookRoot);

        using var http = new HttpClient();

        // Root -> map.html with html content type.
        var root = await http.GetAsync(MapAssetServer.BaseUrl);
        Assert.Equal(HttpStatusCode.OK, root.StatusCode);
        Assert.Contains("map", await root.Content.ReadAsStringAsync());
        Assert.Contains("text/html", root.Content.Headers.ContentType!.ToString());

        // JS asset -> javascript content type.
        var js = await http.GetAsync(MapAssetServer.BaseUrl + "app.js");
        Assert.Equal(HttpStatusCode.OK, js.StatusCode);
        Assert.Contains("javascript", js.Content.Headers.ContentType!.ToString());

        // Book file served from the book root.
        var pic = await http.GetAsync(MapAssetServer.BookBaseUrl + "pic.png");
        Assert.Equal(HttpStatusCode.OK, pic.StatusCode);
        Assert.Equal("image/png", pic.Content.Headers.ContentType!.ToString());

        // Missing asset -> 404.
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync(MapAssetServer.BaseUrl + "nope.js")).StatusCode);

        // Book request with no active book root -> 404.
        bookRoot = null;
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync(MapAssetServer.BookBaseUrl + "pic.png")).StatusCode);
    }

    [Fact]
    public void SafeJoin_RejectsTraversal_AllowsInRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "msroot");
        Assert.Null(MapAssetServer.SafeJoin(root, "../../etc/passwd"));      // escapes root -> null
        var ok = MapAssetServer.SafeJoin(root, "sub/file.txt");
        Assert.NotNull(ok);
        Assert.Contains("file.txt", ok);
    }
}
