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
        public int Requests { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
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

    [Theory]
    [InlineData("en")]
    [InlineData("de-low")]
    [InlineData("fr")]
    public async Task Check_UsesBritishDictionariesForEnglishGrammar(string quoteLanguage)
    {
        _workspace.Settings.Settings.AutoReplacementLanguage = quoteLanguage;
        _workspace.Settings.Settings.SpellCheckLanguages = ["en-GB", "en-GB-oxendict"];

        await _rpc.CheckAsync("I realise it now.", CancellationToken.None);

        Assert.Contains("language=en-GB", _handler.LastRequestBody);
        Assert.DoesNotContain("language=en-US", _handler.LastRequestBody);
    }

    [Fact]
    public async Task Check_UsesProjectDictionaryOverridesAndRevertsToGlobal()
    {
        _workspace.Settings.Settings.AutoReplacementLanguage = "en";
        _workspace.Settings.Settings.SpellCheckLanguages = ["en-US"];
        await _workspace.Projects.CreateProjectAsync(_root, "Grammar", "Book");
        await _workspace.OpenProjectAsync(_workspace.Projects.ProjectRoot!);
        var settings = new SettingsRpc(_workspace);
        await settings.UpdateProjectAsync(new Dictionary<string, JsonElement>
        {
            ["spellCheckLanguages"] = JsonSerializer.SerializeToElement(new[] { "en-GB", "en-GB-oxendict" })
        });
        await _rpc.CheckAsync("I realise it now.", CancellationToken.None);
        Assert.Contains("language=en-GB", _handler.LastRequestBody);

        await settings.UpdateProjectAsync(new Dictionary<string, JsonElement>
        {
            ["spellCheckLanguages"] = JsonSerializer.SerializeToElement<string[]?>(null)
        });
        await _rpc.CheckAsync("I realize it now.", CancellationToken.None);
        Assert.Contains("language=en-US", _handler.LastRequestBody);
    }

    [Fact]
    public async Task AddToDictionary_WithoutCredentials_ReturnsFalse()
    {
        Assert.False(await _rpc.AddToDictionaryAsync("Frostschwur", CancellationToken.None));
    }

    [Fact]
    public async Task Harper_DoesNotUseTheConfiguredLanguageToolAccount()
    {
        _workspace.Settings.Settings.GrammarCheckProvider = "harper";
        _workspace.Settings.Settings.GrammarCheckApiKey = "saved-key";
        _workspace.Settings.Settings.GrammarCheckUsername = "saved-user";

        Assert.Empty(await _rpc.CheckAsync("This is is a test.", CancellationToken.None));
        Assert.False(await _rpc.AddToDictionaryAsync("Aelthorn", CancellationToken.None));
        Assert.Equal(0, _handler.Requests);

        _workspace.Settings.Settings.GrammarCheckProvider = "languagetool";
        await _rpc.CheckAsync("This is is a test.", CancellationToken.None);
        Assert.Equal(1, _handler.Requests);
        Assert.Contains("apiKey=saved-key", _handler.LastRequestBody);
    }

    private static object Match(int offset, int length, string category) => new
    {
        message = "Fixture issue", offset, length,
        rule = new { category = new { id = category } },
        replacements = Array.Empty<object>()
    };

    [Theory]
    [InlineData("Aelthorn")]
    [InlineData("aelthorn")]
    public async Task LearnedWord_StaysAcceptedWithoutAnAccountAfterRecheckingAndReloading(string word)
    {
        _handler.ResponseJson = JsonSerializer.Serialize(new { matches = new[] { Match(4, 8, "TYPOS") } });
        var text = $"The {word} left.";
        Assert.Single(await _rpc.CheckAsync(text, CancellationToken.None));

        await new SpellRpc(_workspace).AddWordAsync("Aelthorn");
        Assert.False(await _rpc.AddToDictionaryAsync("Aelthorn", CancellationToken.None));
        Assert.Empty(await _rpc.CheckAsync(text, CancellationToken.None));

        using var reopened = new Workspace(Path.Combine(_root, "settings"));
        await reopened.Settings.LoadAsync();
        var grammar = new GrammarRpc(reopened, new HttpClient(_handler));
        Assert.Empty(await grammar.CheckAsync(text, CancellationToken.None));

        await new SpellRpc(reopened).RemoveWordAsync("Aelthorn");
        Assert.Single(await grammar.CheckAsync(text, CancellationToken.None));
    }

    [Fact]
    public async Task LearnedWord_StillReceivesGrammarAndStyleFindingsWithoutHidingOtherMisspellings()
    {
        await new SpellRpc(_workspace).AddWordAsync("Aelthorn");
        _handler.ResponseJson = JsonSerializer.Serialize(new
        {
            matches = new[]
            {
                Match(4, 8, "TYPOS"), Match(4, 8, "GRAMMAR"), Match(4, 8, "STYLE"),
                Match(13, 5, "TYPOS"), Match(-1, 8, "TYPOS"), Match(4, int.MaxValue, "TYPOS")
            }
        });

        var issues = await _rpc.CheckAsync("The Aelthorn wrold.", CancellationToken.None);
        Assert.Equal(5, issues.Length);
        Assert.Contains(issues, i => i.Type == "grammar" && i.Offset == 4);
        Assert.Contains(issues, i => i.Type == "style" && i.Offset == 4);
        Assert.Contains(issues, i => i.Type == "spelling" && i.Offset == 13);
        Assert.DoesNotContain(issues, i => i.Type == "spelling" && i.Offset == 4 && i.Length == 8);
    }
}
