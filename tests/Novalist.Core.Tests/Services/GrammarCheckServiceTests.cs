using System.Net;
using Novalist.Core.Services;
using Novalist.Core.Tests.TestHelpers;
using Xunit;

namespace Novalist.Core.Tests.Services;

public class GrammarCheckServiceTests
{
    private static GrammarCheckService WithResponse(HttpStatusCode status, string body)
        => new(new HttpClient(new FakeHttpMessageHandler(status, body)));

    [Theory]
    [InlineData("en", "en-US")]
    [InlineData("de", "de-DE")]
    [InlineData("de-low", "de-DE")]
    [InlineData("de-guillemet", "de-DE")]
    [InlineData("fr", "fr")]
    [InlineData("es", "es")]
    [InlineData("pt", "pt-BR")]
    [InlineData("it", "it")]
    [InlineData("nl", "nl")]
    [InlineData("pl", "pl-PL")]
    [InlineData("ru", "ru-RU")]
    [InlineData("uk", "uk-UA")]
    [InlineData("ja", "ja-JP")]
    [InlineData("zh", "zh-CN")]
    [InlineData("unknown", "en-US")]
    public void MapLanguageCode(string app, string expected)
        => Assert.Equal(expected, GrammarCheckService.MapLanguageCode(app));

    [Fact]
    public void ApiUrl_BlankResetsToDefault()
    {
        var sut = new GrammarCheckService();
        sut.ApiUrl = "https://custom/api/";
        Assert.Equal("https://custom/api", sut.ApiUrl); // trailing slash trimmed
        sut.ApiUrl = "  ";
        Assert.Equal("https://api.languagetool.org/v2/check", sut.ApiUrl);
    }

    [Fact]
    public async Task CheckAsync_EmptyText_ReturnsEmpty()
    {
        var sut = new GrammarCheckService();
        Assert.Empty(await sut.CheckAsync("   ", "en-US"));
    }

    [Fact]
    public async Task CheckAsync_HttpException_ReturnsEmpty()
    {
        var sut = new GrammarCheckService(new HttpClient(FakeHttpMessageHandler.Throwing(new HttpRequestException("down"))));
        Assert.Empty(await sut.CheckAsync("text", "en-US"));
    }

    [Fact]
    public async Task CheckAsync_NonSuccessStatus_ReturnsEmpty()
    {
        var sut = WithResponse(HttpStatusCode.InternalServerError, "");
        Assert.Empty(await sut.CheckAsync("text", "en-US"));
    }

    [Fact]
    public async Task CheckAsync_MalformedJson_ReturnsEmpty()
    {
        var sut = WithResponse(HttpStatusCode.OK, "{ not json");
        Assert.Empty(await sut.CheckAsync("text", "en-US"));
    }

    [Fact]
    public async Task CheckAsync_NoMatchesProperty_ReturnsEmpty()
    {
        var sut = WithResponse(HttpStatusCode.OK, "{}");
        Assert.Empty(await sut.CheckAsync("text", "en-US"));
    }

    [Fact]
    public async Task CheckAsync_ParsesMatches_WithTypesAndReplacements()
    {
        const string json = """
        {
          "matches": [
            { "message": "spelling", "offset": 0, "length": 3,
              "rule": { "category": { "id": "TYPOS" } },
              "replacements": [ { "value": "cat" }, { "value": "" } ] },
            { "message": "style", "offset": 5, "length": 2,
              "rule": { "category": { "id": "STYLE" } },
              "replacements": [] },
            { "message": "grammar", "offset": 8, "length": 1,
              "rule": { "category": { "id": "OTHER" } } },
            { "message": "no-rule", "offset": 9, "length": 1 }
          ]
        }
        """;
        var sut = WithResponse(HttpStatusCode.OK, json);

        var issues = await sut.CheckAsync("some text here", "en-US");

        Assert.Equal(4, issues.Count);
        Assert.Equal(GrammarIssueType.Spelling, issues[0].Type);
        Assert.Equal(new[] { "cat" }, issues[0].Replacements); // empty value skipped
        Assert.Equal(GrammarIssueType.Style, issues[1].Type);
        Assert.Equal(GrammarIssueType.Grammar, issues[2].Type);   // unknown category
        Assert.Equal(GrammarIssueType.Grammar, issues[3].Type);   // no rule property
    }

    [Fact]
    public async Task CheckAsync_LimitsReplacementsToFive()
    {
        const string json = """
        {
          "matches": [
            { "message": "m", "offset": 0, "length": 1,
              "rule": { "category": { "id": "TYPOS" } },
              "replacements": [
                {"value":"a"},{"value":"b"},{"value":"c"},
                {"value":"d"},{"value":"e"},{"value":"f"},{"value":"g"}
              ] }
          ]
        }
        """;
        var sut = WithResponse(HttpStatusCode.OK, json);
        var issues = await sut.CheckAsync("x", "en-US");
        Assert.Equal(5, issues[0].Replacements.Count);
    }
}
