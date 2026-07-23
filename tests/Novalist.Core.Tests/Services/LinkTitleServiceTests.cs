using System.Net;
using Novalist.Core.Services;
using Novalist.Core.Tests.TestHelpers;
using Xunit;

namespace Novalist.Core.Tests.Services;

public class LinkTitleServiceTests
{
    private static LinkTitleService With(HttpStatusCode status, string body)
        => new(new HttpClient(new FakeHttpMessageHandler(status, body)));

    [Fact]
    public async Task FetchTitle_ReadsTitleElement()
    {
        var sut = With(HttpStatusCode.OK, "<html><head><title>Rigging and Knots</title></head></html>");
        Assert.Equal("Rigging and Knots", await sut.FetchTitleAsync("https://example.com"));
    }

    [Fact]
    public async Task FetchTitle_DecodesEntitiesAndCollapsesWhitespace()
    {
        var sut = With(HttpStatusCode.OK, "<title>\n  Ships &amp;\tSails  \n</title>");
        Assert.Equal("Ships & Sails", await sut.FetchTitleAsync("https://example.com"));
    }

    [Fact]
    public async Task FetchTitle_HandlesAttributesAndMultilineTitle()
    {
        var sut = With(HttpStatusCode.OK, "<title lang=\"en\">A\nlong\ntitle</title>");
        Assert.Equal("A long title", await sut.FetchTitleAsync("https://example.com"));
    }

    [Theory]
    [InlineData("not a url")]
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://example.com")]
    public async Task FetchTitle_RejectsNonHttpUrls(string url)
    {
        var sut = With(HttpStatusCode.OK, "<title>Nope</title>");
        Assert.Null(await sut.FetchTitleAsync(url));
    }

    [Fact]
    public async Task FetchTitle_NonSuccessStatus_ReturnsNull()
        => Assert.Null(await With(HttpStatusCode.NotFound, "<title>x</title>")
            .FetchTitleAsync("https://example.com"));

    [Fact]
    public async Task FetchTitle_NoTitleElement_ReturnsNull()
        => Assert.Null(await With(HttpStatusCode.OK, "<html><body>nothing</body></html>")
            .FetchTitleAsync("https://example.com"));

    [Fact]
    public async Task FetchTitle_BlankTitle_ReturnsNull()
        => Assert.Null(await With(HttpStatusCode.OK, "<title>   </title>")
            .FetchTitleAsync("https://example.com"));

    [Fact]
    public async Task FetchTitle_NetworkFailure_ReturnsNull()
    {
        var sut = new LinkTitleService(
            new HttpClient(FakeHttpMessageHandler.Throwing(new HttpRequestException("offline"))));
        Assert.Null(await sut.FetchTitleAsync("https://example.com"));
    }

    [Fact]
    public async Task FetchTitle_Timeout_ReturnsNull()
    {
        var sut = new LinkTitleService(
            new HttpClient(FakeHttpMessageHandler.Throwing(new TaskCanceledException("timed out"))));
        Assert.Null(await sut.FetchTitleAsync("https://example.com"));
    }

    [Fact]
    public async Task FetchTitle_HugeBody_StillFindsEarlyTitle()
    {
        // The reader caps how much it pulls; a title in the <head> is well inside it.
        var body = "<html><head><title>Early</title></head><body>"
            + new string('x', 400_000) + "</body></html>";
        Assert.Equal("Early", await With(HttpStatusCode.OK, body).FetchTitleAsync("https://example.com"));
    }

    [Fact]
    public void DefaultConstructor_UsesSharedClient()
        => Assert.NotNull(new LinkTitleService());
}
