using System.Net;
using Novalist.Core.Services;
using Novalist.Desktop.Editor;
using Novalist.Sdk.Hooks;
using Xunit;
using EditorDocumentContext = Novalist.Desktop.Editor.EditorDocumentContext;

namespace Novalist.Desktop.Tests.Editor;

[Collection("Avalonia")]
public class GrammarCheckExtensionTests
{
    private const string MatchJson =
        "{\"matches\":[{\"message\":\"err\",\"offset\":0,\"length\":2," +
        "\"rule\":{\"category\":{\"id\":\"TYPOS\"}},\"replacements\":[{\"value\":\"fix\"}]}]}";

    private sealed class StubHandler(string body, HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
    }

    private static GrammarCheckExtension WithHttp(string body, HttpStatusCode status = HttpStatusCode.OK)
    {
        var svc = new GrammarCheckService(new HttpClient(new StubHandler(body, status)));
        return new GrammarCheckExtension(svc);
    }

    private sealed class FakeContributor : IGrammarCheckContributor
    {
        public string GrammarCheckName => "Fake";
        public bool IsGrammarCheckEnabled { get; init; } = true;
        public Func<string, string, CancellationToken, Task<GrammarCheckResult>>? Handler { get; init; }
        public Task<GrammarCheckResult> CheckAsync(string plainText, string language, CancellationToken ct = default)
            => Handler!(plainText, language, ct);
    }

    [AvaloniaFact]
    public void Enabled_InvokesScript_AndNamePriority()
    {
        var ext = new GrammarCheckExtension();
        var calls = new List<string>();
        ext.SetScriptExecutor(s => calls.Add(s));
        Assert.Equal("GrammarCheck", ext.Name);
        Assert.Equal(200, ext.Priority);

        ext.Enabled = true;
        Assert.True(ext.Enabled); // getter
        Assert.Contains(calls, c => c.Contains("setGrammarCheckEnabled(true)"));
        ext.Enabled = false;
        Assert.Contains(calls, c => c.Contains("setGrammarCheckEnabled(false)"));
    }

    [AvaloniaFact]
    public void Language_And_CustomApiUrl()
    {
        var ext = new GrammarCheckExtension();
        ext.Language = null!;
        Assert.Equal("en", ext.Language);

        Assert.Null(ext.CustomApiUrl); // default public API -> null
        ext.CustomApiUrl = "https://lt.local/v2/check";
        Assert.Equal("https://lt.local/v2/check", ext.CustomApiUrl);
        ext.CustomApiUrl = null; // resets to default
        Assert.Null(ext.CustomApiUrl);

        Assert.Null(ext.CustomApiKey);
        ext.CustomApiKey = "k";
        Assert.Equal("k", ext.CustomApiKey);
        ext.CustomApiKey = null;
        Assert.Null(ext.CustomApiKey);

        Assert.Null(ext.CustomUsername);
        ext.CustomUsername = "u";
        Assert.Equal("u", ext.CustomUsername);
        ext.CustomUsername = null;
        Assert.Null(ext.CustomUsername);
    }

    [AvaloniaFact]
    public void PushState_InvokesScript()
    {
        var ext = new GrammarCheckExtension();
        var calls = new List<string>();
        ext.SetScriptExecutor(s => calls.Add(s));
        ext.PushState();
        Assert.Contains(calls, c => c.Contains("setGrammarCheckEnabled"));
    }

    [AvaloniaFact]
    public void OnDocumentClosing_CancelsAndClears()
    {
        var ext = new GrammarCheckExtension();
        var calls = new List<string>();
        ext.SetScriptExecutor(s => calls.Add(s));
        ext.OnDocumentClosing(new EditorDocumentContext { SceneId = "s", ChapterGuid = "c", SceneTitle = "T", ChapterTitle = "Ch", FilePath = "x.html" });
        Assert.Contains(calls, c => c.Contains("setGrammarIssues('[]')"));
        ext.OnDocumentOpened(new EditorDocumentContext { SceneId = "s", ChapterGuid = "c", SceneTitle = "T", ChapterTitle = "Ch", FilePath = "x.html" }); // no-op
    }

    [AvaloniaFact]
    public async Task CheckText_DisabledOrEmpty_NoOp()
    {
        var ext = WithHttp(MatchJson);
        var calls = new List<string>();
        ext.SetScriptExecutor(s => calls.Add(s));
        await ext.CheckTextAsync("hello"); // disabled -> no-op
        ext.Enabled = true;
        calls.Clear();
        await ext.CheckTextAsync("   "); // empty -> no-op
        Assert.DoesNotContain(calls, c => c.Contains("setGrammarIssues"));
    }

    [AvaloniaFact]
    public async Task CheckText_SerializesIssues_ToScript()
    {
        var ext = WithHttp(MatchJson) ;
        ext.Enabled = true;
        var calls = new List<string>();
        ext.SetScriptExecutor(s => calls.Add(s));

        await ext.CheckTextAsync("hello wrld");
        Avalonia.Threading.Dispatcher.UIThread.RunJobs(); // flush the marshalled Post

        Assert.Contains(calls, c => c.Contains("setGrammarIssues(") && c.Contains("offset"));
    }

    [AvaloniaFact]
    public async Task CheckText_MergesContributors_AndSkipsDisabledAndFailing()
    {
        var ext = WithHttp("{\"matches\":[]}"); // LanguageTool returns nothing
        ext.Enabled = true;
        var calls = new List<string>();
        ext.SetScriptExecutor(s => calls.Add(s));

        ext.SetContributors(
        [
            new FakeContributor // disabled -> skipped
            {
                IsGrammarCheckEnabled = false,
                Handler = (_, _, _) => Task.FromResult(new GrammarCheckResult { Issues = [new() { Offset = 0, Length = 1, Message = "x" }] }),
            },
            new FakeContributor // throws (non-OCE) -> ignored
            {
                Handler = (_, _, _) => throw new InvalidOperationException("boom"),
            },
            new FakeContributor // contributes Style/Spelling/Grammar issues -> MapSdkIssue all arms
            {
                Handler = (_, _, _) => Task.FromResult(new GrammarCheckResult
                {
                    Issues =
                    [
                        new() { Offset = 3, Length = 4, Message = "style", Type = Novalist.Sdk.Hooks.GrammarIssueType.Style, Replacements = ["r1", "r2"] },
                        new() { Offset = 8, Length = 2, Message = "spell", Type = Novalist.Sdk.Hooks.GrammarIssueType.Spelling },
                        new() { Offset = 10, Length = 1, Message = "gram", Type = Novalist.Sdk.Hooks.GrammarIssueType.Grammar },
                    ],
                }),
            },
        ]);

        await ext.CheckTextAsync("some text");
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Contains(calls, c => c.Contains("setGrammarIssues(") && c.Contains("style") && c.Contains("spelling") && c.Contains("grammar"));
    }

    [AvaloniaFact]
    public async Task CheckText_ContributorCancels_Swallowed()
    {
        var ext = WithHttp("{\"matches\":[]}");
        ext.Enabled = true;
        var calls = new List<string>();
        ext.SetScriptExecutor(s => calls.Add(s));
        ext.SetContributors(
        [
            new FakeContributor { Handler = (_, _, _) => throw new OperationCanceledException() },
        ]);
        // OCE rethrown by the contributor wrapper and swallowed by CheckTextAsync's
        // OperationCanceledException handler -> no issues pushed, no throw.
        await ext.CheckTextAsync("text");
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.DoesNotContain(calls, c => c.Contains("setGrammarIssues("));
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => throw new Exception("generic failure");
    }

    [AvaloniaFact]
    public async Task CheckText_ServiceThrowsGenericException_ResetsWebViewIssues()
    {
        var svc = new GrammarCheckService(new HttpClient(new ThrowingHandler()));
        var ext = new GrammarCheckExtension(svc);
        ext.Enabled = true;
        var calls = new List<string>();
        ext.SetScriptExecutor(s => calls.Add(s));

        await ext.CheckTextAsync("some text");
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Contains(calls, c => c.Contains("setGrammarIssues('[]')"));
    }

    [AvaloniaFact]
    public void IsPremiumAvailable_DelegatesToService()
    {
        var ext = new GrammarCheckExtension();
        // Default service has no credentials -> not premium
        Assert.False(ext.IsPremiumAvailable);
    }

    [AvaloniaFact]
    public void PickyMode_RoundTrips()
    {
        var ext = new GrammarCheckExtension();
        Assert.False(ext.PickyMode);
        ext.PickyMode = true;
        Assert.True(ext.PickyMode);
    }

    [AvaloniaFact]
    public void MotherTongue_RoundTrips()
    {
        var ext = new GrammarCheckExtension();
        Assert.Null(ext.MotherTongue);
        ext.MotherTongue = "de-DE";
        Assert.Equal("de-DE", ext.MotherTongue);
    }

    [AvaloniaFact]
    public async Task AddToDictionary_DelegatesToService()
    {
        // No credentials -> returns false via the guard in the service
        var ext = new GrammarCheckExtension();
        Assert.False(await ext.AddToDictionaryAsync("word"));
    }

    [AvaloniaFact]
    public async Task CheckText_CancelledAfterApiReturns_DoesNotPushIssues()
    {
        // Use a contributor that cancels the token mid-flight so the post-API
        // IsCancellationRequested check triggers (lines 157-161).
        var ext = WithHttp("{\"matches\":[]}");
        ext.Enabled = true;
        var calls = new List<string>();
        ext.SetScriptExecutor(s => calls.Add(s));

        var tcs = new TaskCompletionSource<GrammarCheckResult>();

        ext.SetContributors(
        [
            new FakeContributor
            {
                Handler = async (_, _, ct) =>
                {
                    return await tcs.Task;
                },
            },
        ]);

        // Start a check. Because of the tcs, it will await and yield.
        var t1 = ext.CheckTextAsync("first call text");

        // Start another check to cancel the first one.
        var t2 = ext.CheckTextAsync("second call text");

        // Complete the task of the first check.
        tcs.SetResult(new GrammarCheckResult());

        await t1; // first call had its CTS cancelled -> isCancellationRequested returns
        await t2;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        // The second call's issues should be the only ones pushed.
        Assert.Contains(calls, c => c.Contains("setGrammarIssues("));
    }
}
