using Novalist.Core.Services;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

/// <summary>Grammar checking (LanguageTool) for the editor round-trip.</summary>
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
        Configure();
        var language = GrammarCheckService.MapLanguageCode(
            _workspace.Settings.Effective.AutoReplacementLanguage);
        var issues = await _service.CheckAsync(text, language, cancellationToken);
        return issues
            .Select(i => new GrammarIssueDto(
                i.Message,
                i.Offset,
                i.Length,
                i.Type.ToString().ToLowerInvariant(),
                i.Replacements.Take(5).ToArray()))
            .ToArray();
    }

    [JsonRpcMethod("grammar/addToDictionary")]
    public async Task<bool> AddToDictionaryAsync(string word, CancellationToken cancellationToken)
    {
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
