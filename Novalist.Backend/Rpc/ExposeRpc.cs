using Novalist.Core.Services;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

/// <summary>The active book's exposé: text, length budget, live counts, export.</summary>
public sealed class ExposeRpc
{
    private readonly Workspace _workspace;

    public ExposeRpc(Workspace workspace)
    {
        _workspace = workspace;
    }

    private ExposeService Service() => new(_workspace.Projects);

    [JsonRpcMethod("expose/get")]
    public Task<ExposeState> GetAsync() => Service().GetAsync();

    [JsonRpcMethod("expose/save")]
    public Task<ExposeState> SaveAsync(string html) => Service().SaveAsync(html);

    /// <summary>Counts without saving — what the editor calls while the writer types.</summary>
    [JsonRpcMethod("expose/measure")]
    public ExposeState Measure(string html) => Service().Measure(html);

    [JsonRpcMethod("expose/setLimits")]
    public Task<ExposeState> SetLimitsAsync(int charLimit, int pageLimit)
        => Service().SetLimitsAsync(charLimit, pageLimit);

    [JsonRpcMethod("expose/export")]
    public async Task<ExportResultDto> ExportAsync(string outputPath, string title)
    {
        var written = await Service().ExportAsync(outputPath, title);
        var info = new FileInfo(outputPath);
        return new ExportResultDto(outputPath, written && info.Exists, info.Exists ? info.Length : 0);
    }
}
