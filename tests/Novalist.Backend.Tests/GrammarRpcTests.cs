using System.Net;
using System.Text;
using System.Text.Json;
using Novalist.Backend;
using Novalist.Backend.Rpc;
using Xunit;

namespace Novalist.Backend.Tests;

public sealed class GrammarRpcTests : IDisposable
{
    private sealed class StubHandler : HttpMessageHandler
    {
        public string ResponseJson { get; set; } = "{}";
        public string? LastRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestBody = request.Content == null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(ResponseJson, Encoding.UTF8, "application/json")
            };
        }
    }

    private readonly string _root;
    private readonly Workspace _workspace;
    private readonly StubHandler _handler = new();
    private readonly GrammarRpc _rpc;

    public GrammarRpcTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "nl-gr-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _workspace = new Workspace(Path.Combine(_root, "settings"));
        _workspace.Settings.LoadAsync().GetAwaiter().GetResult();
        _rpc = new GrammarRpc(_workspace, new HttpClient(_handler));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    [Fact]
    public async Task Check_MapsIssues_FromLanguageToolResponse()
    {
        _handler.ResponseJson = JsonSerializer.Serialize(new
        {
            matches = new[]
            {
                new
                {
                    message = "Possible spelling mistake",
                    offset = 4,
                    length = 5,
                    rule = new { issueType = "misspelling", category = new { id = "TYPOS" } },
                    replacements = new[] { new { value = "world" }, new { value = "word" } }
                }
            }
        });

        var issues = await _rpc.CheckAsync("The wrold turns.", CancellationToken.None);

        var issue = Assert.Single(issues);
        Assert.Equal(4, issue.Offset);
        Assert.Equal(5, issue.Length);
        Assert.Equal("spelling", issue.Type);
        Assert.Contains("world", issue.Replacements);
    }

    [Fact]
    public async Task Check_Disabled_ReturnsEmptyWithoutRequest()
    {
        _workspace.Settings.Settings.GrammarCheckEnabled = false;

        var issues = await _rpc.CheckAsync("whatever", CancellationToken.None);

        Assert.Empty(issues);
        Assert.Null(_handler.LastRequestBody);
    }

    [Fact]
    public async Task Check_UsesConfiguredApiUrlAndOptions()
    {
        _workspace.Settings.Settings.GrammarCheckApiUrl = "https://stub.example/v2/check";
        _workspace.Settings.Settings.GrammarCheckPickyMode = true;
        _handler.ResponseJson = """{"matches": []}""";

        var issues = await _rpc.CheckAsync("Text.", CancellationToken.None);

        Assert.Empty(issues);
        Assert.NotNull(_handler.LastRequestBody);
        Assert.Contains("level=picky", _handler.LastRequestBody);
    }

    [Fact]
    public async Task AddToDictionary_WithoutCredentials_ReturnsFalse()
    {
        Assert.False(await _rpc.AddToDictionaryAsync("Frostschwur", CancellationToken.None));
    }
}
