using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Novalist.Core.Services;
using Novalist.Sdk.Hooks;

namespace Novalist.Desktop.Editor;

/// <summary>
/// Editor extension that provides grammar and spelling checking via LanguageTool.
/// Manages debounced API calls and serializes results for the JS editor.
/// </summary>
public sealed class GrammarCheckExtension : IEditorExtension
{
    private readonly GrammarCheckService _service;
    private CancellationTokenSource? _cts;
    private bool _enabled;
    private string _language = "en";
    private Action<string>? _executeScript;
    private List<IGrammarCheckContributor> _contributors = [];

    public string Name => "GrammarCheck";
    public int Priority => 200;

    /// <summary>Default ctor uses the shared HTTP client; the optional overload lets
    /// tests inject a <see cref="GrammarCheckService"/> backed by a fake transport.</summary>
    public GrammarCheckExtension(GrammarCheckService? service = null) => _service = service ?? new GrammarCheckService();

    public bool Enabled
    {
        get => _enabled;
        set
        {
            _enabled = value;
            _executeScript?.Invoke($"setGrammarCheckEnabled({(value ? "true" : "false")})");
        }
    }

    public string Language
    {
        get => _language;
        set => _language = value ?? "en";
    }

    /// <summary>
    /// Optional LanguageTool Cloud API key (premium). Null/empty means no key.
    /// </summary>
    public string? CustomApiKey
    {
        get => string.IsNullOrWhiteSpace(_service.ApiKey) ? null : _service.ApiKey;
        set => _service.ApiKey = string.IsNullOrWhiteSpace(value) ? null : value;
    }

    /// <summary>
    /// Optional LanguageTool Cloud username/email (premium). Null/empty means no username.
    /// </summary>
    public string? CustomUsername
    {
        get => string.IsNullOrWhiteSpace(_service.Username) ? null : _service.Username;
        set => _service.Username = string.IsNullOrWhiteSpace(value) ? null : value;
    }

    /// <summary>
    /// True when the configured credentials have been validated and premium checks
    /// are available from the LanguageTool Cloud endpoint.
    /// </summary>
    public bool IsPremiumAvailable => _service.IsPremiumAvailable;

    /// <summary>
    /// Gets or sets a custom LanguageTool API URL. Null/empty uses the public API.
    /// </summary>
    public string? CustomApiUrl
    {
        get => _service.ApiUrl == "https://api.languagetool.org/v2/check" ? null : _service.ApiUrl;
        set => _service.ApiUrl = value ?? "https://api.languagetool.org/v2/check";
    }

    /// <summary>
    /// Whether advanced/picky checking style rules are enabled.
    /// </summary>
    public bool PickyMode
    {
        get => _service.PickyMode;
        set => _service.PickyMode = value;
    }

    /// <summary>
    /// Optional native/mother tongue language code (e.g. "de", "fr").
    /// </summary>
    public string? MotherTongue
    {
        get => _service.MotherTongue;
        set => _service.MotherTongue = value;
    }

    /// <summary>
    /// Adds a word to the user's personal dictionary.
    /// </summary>
    public Task<bool> AddToDictionaryAsync(string word, CancellationToken cancellationToken = default)
    {
        return _service.AddToDictionaryAsync(word, cancellationToken);
    }

    /// <summary>
    /// Sets the delegate used to execute JavaScript in the WebView.
    /// </summary>
    public void SetScriptExecutor(Action<string> executeScript)
    {
        _executeScript = executeScript;
    }

    /// <summary>
    /// Sets the list of extension contributors to also query.
    /// </summary>
    public void SetContributors(List<IGrammarCheckContributor> contributors)
    {
        _contributors = contributors;
    }

    /// <summary>
    /// Called when the JS editor requests a grammar check (after typing pause).
    /// Also queries any registered extension contributors and merges results.
    /// </summary>
    public async Task CheckTextAsync(string plainText)
    {
        if (!_enabled || string.IsNullOrWhiteSpace(plainText)) return;

        var sw = System.Diagnostics.Stopwatch.StartNew();
        System.Diagnostics.Debug.WriteLine($"[GrammarCheck] Starting check for plainText of length {plainText.Length}");

        // Cancel any in-flight request
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        try
        {
            var languageCode = GrammarCheckService.MapLanguageCode(_language);
            
            var swLt = System.Diagnostics.Stopwatch.StartNew();
            var issues = await _service.CheckAsync(plainText, languageCode, token).ConfigureAwait(false);
            swLt.Stop();
            System.Diagnostics.Debug.WriteLine($"[GrammarCheck] LanguageTool API returned {issues.Count} issues in {swLt.ElapsedMilliseconds}ms");

            // Also query extension contributors (AI-powered checks, etc.)
            var swContributors = System.Diagnostics.Stopwatch.StartNew();
            var contributorIssues = await QueryContributorsAsync(plainText, _language, token).ConfigureAwait(false);
            swContributors.Stop();
            issues.AddRange(contributorIssues);
            System.Diagnostics.Debug.WriteLine($"[GrammarCheck] QueryContributorsAsync returned {contributorIssues.Count} issues in {swContributors.ElapsedMilliseconds}ms");

            if (token.IsCancellationRequested)
            {
                System.Diagnostics.Debug.WriteLine($"[GrammarCheck] Check was cancelled after {sw.ElapsedMilliseconds}ms");
                return;
            }

            var json = SerializeIssuesJson(issues);
            var swJs = System.Diagnostics.Stopwatch.StartNew();
            Dispatcher.UIThread.Post(() =>
            {
                _executeScript?.Invoke($"setGrammarIssues('{EscapeForSingleQuoteJs(json)}')");
            });
            System.Diagnostics.Debug.WriteLine($"[GrammarCheck] Serialized and posted JS update in {swJs.ElapsedMilliseconds}ms. Total Check time: {sw.ElapsedMilliseconds}ms");
        }
        catch (OperationCanceledException)
        {
            System.Diagnostics.Debug.WriteLine($"[GrammarCheck] Check was cancelled via OperationCanceledException after {sw.ElapsedMilliseconds}ms");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GrammarCheck] Check failed with exception after {sw.ElapsedMilliseconds}ms: {ex}");
            // Fail safely: reset issues to ensure progress indicator stops
            Dispatcher.UIThread.Post(() =>
            {
                _executeScript?.Invoke("setGrammarIssues('[]')");
            });
        }
    }

    private async Task<List<Core.Services.GrammarIssue>> QueryContributorsAsync(string plainText, string language, CancellationToken token)
    {
        var results = new List<Core.Services.GrammarIssue>();
        System.Diagnostics.Debug.WriteLine($"[GrammarCheck] QueryContributorsAsync: _contributors count = {_contributors.Count}");
        if (_contributors.Count == 0) return results;

        var tasks = _contributors
            .Where(c => c.IsGrammarCheckEnabled)
            .Select(async c =>
            {
                try
                {
                    System.Diagnostics.Debug.WriteLine($"[GrammarCheck] Contributor {c.GrammarCheckName} starting");
                    var res = await c.CheckAsync(plainText, language, token).ConfigureAwait(false);
                    System.Diagnostics.Debug.WriteLine($"[GrammarCheck] Contributor {c.GrammarCheckName} returned {res?.Issues?.Count ?? 0} issues");
                    return res;
                }
                catch (OperationCanceledException)
                {
                    System.Diagnostics.Debug.WriteLine($"[GrammarCheck] Contributor {c.GrammarCheckName} cancelled");
                    throw;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[GrammarCheck] Contributor {c.GrammarCheckName} threw exception: {ex}");
                    // Best-effort: ignore contributor failures
                    return new GrammarCheckResult();
                }
            })
            .ToList();

        if (tasks.Count == 0) return results;

        System.Diagnostics.Debug.WriteLine($"[GrammarCheck] QueryContributorsAsync: waiting for {tasks.Count} tasks");
        await Task.WhenAll(tasks).ConfigureAwait(false);
        System.Diagnostics.Debug.WriteLine($"[GrammarCheck] QueryContributorsAsync: all tasks completed");

        foreach (var task in tasks)
        {
            if (task.IsCompletedSuccessfully && task.Result != null)
            {
                results.AddRange(task.Result.Issues.Select(MapSdkIssue));
            }
        }

        return results;
    }

    private static Core.Services.GrammarIssue MapSdkIssue(Sdk.Hooks.GrammarIssue issue)
    {
        return new Core.Services.GrammarIssue
        {
            Offset = issue.Offset,
            Length = issue.Length,
            Message = issue.Message,
            Type = issue.Type switch
            {
                Sdk.Hooks.GrammarIssueType.Spelling => Core.Services.GrammarIssueType.Spelling,
                Sdk.Hooks.GrammarIssueType.Style => Core.Services.GrammarIssueType.Style,
                _ => Core.Services.GrammarIssueType.Grammar
            },
            Replacements = issue.Replacements
        };
    }

    /// <summary>
    /// Pushes current enabled state to JS (call after webview ready).
    /// </summary>
    public void PushState()
    {
        _executeScript?.Invoke($"setGrammarCheckEnabled({(_enabled ? "true" : "false")})");
    }

    private static string SerializeIssuesJson(System.Collections.Generic.List<Core.Services.GrammarIssue> issues)
    {
        if (issues.Count == 0) return "[]";

        var sb = new StringBuilder(256);
        sb.Append('[');
        for (int i = 0; i < issues.Count; i++)
        {
            if (i > 0) sb.Append(',');
            var issue = issues[i];
            sb.Append("{\"offset\":").Append(issue.Offset);
            sb.Append(",\"length\":").Append(issue.Length);
            sb.Append(",\"type\":\"").Append(issue.Type switch
            {
                Core.Services.GrammarIssueType.Spelling => "spelling",
                Core.Services.GrammarIssueType.Style => "style",
                _ => "grammar"
            }).Append('"');
            sb.Append(",\"message\":").Append(JsonSerializer.Serialize(issue.Message));
            sb.Append(",\"replacements\":[");
            for (int j = 0; j < issue.Replacements.Count; j++)
            {
                if (j > 0) sb.Append(',');
                sb.Append(JsonSerializer.Serialize(issue.Replacements[j]));
            }
            sb.Append("]}");
        }
        sb.Append(']');
        return sb.ToString();
    }

    private static string EscapeForSingleQuoteJs(string value)
        => value.Replace("\\", "\\\\").Replace("'", "\\'");

    public void OnDocumentOpened(EditorDocumentContext context)
    {
        // Optionally trigger an initial check
    }

    public void OnDocumentClosing(EditorDocumentContext context)
    {
        _cts?.Cancel();
        _executeScript?.Invoke("setGrammarIssues('[]')");
    }
}
