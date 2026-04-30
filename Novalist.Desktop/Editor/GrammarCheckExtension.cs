using System;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Novalist.Core.Services;

namespace Novalist.Desktop.Editor;

/// <summary>
/// Editor extension that provides grammar and spelling checking via LanguageTool.
/// Manages debounced API calls and serializes results for the JS editor.
/// </summary>
public sealed class GrammarCheckExtension : IEditorExtension
{
    private readonly GrammarCheckService _service = new();
    private CancellationTokenSource? _cts;
    private bool _enabled;
    private string _language = "en";
    private Action<string>? _executeScript;

    public string Name => "GrammarCheck";
    public int Priority => 200;

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
    /// Gets or sets a custom LanguageTool API URL. Null/empty uses the public API.
    /// </summary>
    public string? CustomApiUrl
    {
        get => _service.ApiUrl == "https://api.languagetool.org/v2/check" ? null : _service.ApiUrl;
        set => _service.ApiUrl = value ?? "https://api.languagetool.org/v2/check";
    }

    /// <summary>
    /// Sets the delegate used to execute JavaScript in the WebView.
    /// </summary>
    public void SetScriptExecutor(Action<string> executeScript)
    {
        _executeScript = executeScript;
    }

    /// <summary>
    /// Called when the JS editor requests a grammar check (after typing pause).
    /// </summary>
    public async Task CheckTextAsync(string plainText)
    {
        if (!_enabled || string.IsNullOrWhiteSpace(plainText)) return;

        // Cancel any in-flight request
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        try
        {
            var languageCode = GrammarCheckService.MapLanguageCode(_language);
            var issues = await _service.CheckAsync(plainText, languageCode, token).ConfigureAwait(false);

            if (token.IsCancellationRequested) return;

            var json = SerializeIssuesJson(issues);
            Dispatcher.UIThread.Post(() =>
            {
                _executeScript?.Invoke($"setGrammarIssues('{EscapeForSingleQuoteJs(json)}')");
            });
        }
        catch (OperationCanceledException)
        {
            // Expected when a new check supersedes this one
        }
    }

    /// <summary>
    /// Pushes current enabled state to JS (call after webview ready).
    /// </summary>
    public void PushState()
    {
        _executeScript?.Invoke($"setGrammarCheckEnabled({(_enabled ? "true" : "false")})");
    }

    private static string SerializeIssuesJson(System.Collections.Generic.List<GrammarIssue> issues)
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
                GrammarIssueType.Spelling => "spelling",
                GrammarIssueType.Style => "style",
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
