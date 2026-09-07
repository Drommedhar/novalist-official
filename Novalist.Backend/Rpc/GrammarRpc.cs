using Novalist.Core.Services;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

/// <summary>Server and extension grammar checks. Harper runs locally in the renderer.</summary>
public sealed class GrammarRpc
{
    private readonly Workspace _workspace;
    private readonly GrammarCheckService _service;

    public GrammarRpc(Workspace workspace, HttpClient? http = null)
    {
        _workspace = workspace;
        _service = new GrammarCheckService(http);
    }

    private void Configure()
    {
        var effective = _workspace.Settings.Effective;
        if (!string.IsNullOrWhiteSpace(effective.GrammarCheckApiUrl))
        {
            _service.ApiUrl = effective.GrammarCheckApiUrl;
        }
        _service.ApiKey = effective.GrammarCheckApiKey;
        _service.Username = effective.GrammarCheckUsername;
        _service.PickyMode = effective.GrammarCheckPickyMode;
        _service.MotherTongue = effective.GrammarCheckMotherTongue;
    }

    [JsonRpcMethod("grammar/check")]
    public async Task<GrammarIssueDto[]> CheckAsync(string text, CancellationToken cancellationToken)
    {
        if (!_workspace.Settings.Effective.GrammarCheckEnabled)
        {
            return [];
        }
        var effective = _workspace.Settings.Effective;
        var results = new List<GrammarIssueDto>();
        if (effective.GrammarCheckProvider != "harper")
        {
            Configure();
            var language = GrammarCheckService.ResolveLanguageCode(
                effective.AutoReplacementLanguage, effective.SpellCheckLanguages);
            var coreIssues = await _service.CheckAsync(text, language, cancellationToken);
            results.AddRange(coreIssues.Select(i => new GrammarIssueDto(
                i.Message, i.Offset, i.Length,
                i.Type.ToString().ToLowerInvariant(), i.Replacements.Take(5).ToArray())));
        }

        // Merge in extension-contributed grammar issues (best-effort; matches the
        // desktop's GrammarCheckExtension.QueryContributorsAsync behavior).
        results.AddRange(await QueryContributorsAsync(text, cancellationToken));
        // The free endpoint cannot store a personal dictionary. Respect the
        // writer's local words on every response, without hiding grammar or
        // style findings about those same words.
        var words = _workspace.Settings.Settings.SpellCheckCustomWords.ToHashSet(StringComparer.OrdinalIgnoreCase);
        results.RemoveAll(issue => issue.Type == "spelling"
            && issue.Offset >= 0 && issue.Length > 0 && issue.Offset <= text.Length - issue.Length
            && words.Contains(text.Substring(issue.Offset, issue.Length)));
        return results.ToArray();
    }

    /// <summary>Runs every enabled extension grammar contributor and flattens
    /// their issues. Never force-creates the extension host, and swallows
    /// individual contributor faults so one bad rule cannot blank the check.</summary>
    private async Task<IReadOnlyList<GrammarIssueDto>> QueryContributorsAsync(string text, CancellationToken cancellationToken)
    {
        var host = _workspace.ExtensionHostOrNull;
        if (host == null) return [];
        var contributors = host.GrammarCheckContributors
            .Where(c => c.IsGrammarCheckEnabled)
            .ToList();
        if (contributors.Count == 0) return [];

        var uiLanguage = _workspace.Settings.Effective.Language;
        var merged = new List<GrammarIssueDto>();
        foreach (var contributor in contributors)
        {
            try
            {
                var result = await contributor.CheckAsync(text, uiLanguage, cancellationToken);
                merged.AddRange(result.Issues.Select(i => new GrammarIssueDto(
                    i.Message,
                    i.Offset,
                    i.Length,
                    i.Type.ToString().ToLowerInvariant(),
                    i.Replacements.Take(5).ToArray())));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Log only the shape — never the message (could echo story content).
                Novalist.Backend.Extensions.Log.Warn($"[Grammar] contributor {contributor.GetType().Name} threw {ex.GetType().Name}");
            }
        }
        return merged;
    }

    [JsonRpcMethod("grammar/addToDictionary")]
    public async Task<bool> AddToDictionaryAsync(string word, CancellationToken cancellationToken)
    {
        // A previously configured LanguageTool account must not receive words
        // after the writer has selected the offline provider.
        if (_workspace.Settings.Effective.GrammarCheckProvider == "harper") return false;
        Configure();
        return await _service.AddToDictionaryAsync(word, cancellationToken);
    }
}

public sealed record GrammarIssueDto(
    string Message,
    int Offset,
    int Length,
    string Type,
    IReadOnlyList<string> Replacements);
