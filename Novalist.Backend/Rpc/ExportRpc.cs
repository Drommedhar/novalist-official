using Novalist.Core.Services;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

/// <summary>Manuscript export to the seven supported formats.</summary>
public sealed class ExportRpc
{
    private readonly Workspace _workspace;

    public ExportRpc(Workspace workspace)
    {
        _workspace = workspace;
    }

    [JsonRpcMethod("export/formats")]
    public string[] Formats() => Enum.GetNames<ExportFormat>();

    [JsonRpcMethod("export/run")]
    public async Task<ExportResultDto> RunAsync(
        string format,
        string outputPath,
        string title,
        string author,
        bool includeTitlePage,
        string[] selectedChapterGuids)
    {
        var service = new ExportService(_workspace.Projects, new EntityService(_workspace.Projects));
        var options = new ExportOptions
        {
            Format = Enum.Parse<ExportFormat>(format),
            Title = title,
            Author = author,
            IncludeTitlePage = includeTitlePage,
            SelectedChapterGuids = selectedChapterGuids.ToList()
        };
        if (options.Format == ExportFormat.Codex)
        {
            await service.ExportCodexAsync(options, outputPath);
        }
        else
        {
            await service.ExportAsync(options, outputPath);
        }
        var info = new FileInfo(outputPath);
        return new ExportResultDto(outputPath, info.Exists, info.Exists ? info.Length : 0);
    }
}

public sealed record ExportResultDto(string OutputPath, bool Success, long SizeBytes);
