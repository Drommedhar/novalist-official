using Novalist.Core.Services;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

public sealed class FolderImportRpc(Workspace workspace)
{
    private FolderImportService? _import;
    private string? _sessionId;

    [JsonRpcMethod("folderImport/scan")]
    public async Task<FolderImportScanDto> ScanAsync(string root)
    {
        var service = new FolderImportService(workspace.Projects, workspace.FileService);
        await Task.Run(() => service.Scan(root));
        _import = service;
        _sessionId = Guid.NewGuid().ToString();
        return new FolderImportScanDto(_sessionId, service.Total, service.Unsupported, service.Folders.Count - 1);
    }

    [JsonRpcMethod("folderImport/folders")]
    public FolderImportPageDto Folders(string sessionId, string query = "", int offset = 0, int limit = 30)
    {
        var matching = Require(sessionId).Folders.Where(folder => folder.Path.Length > 0
            && folder.Path.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();
        return new FolderImportPageDto(matching.Length,
            [.. matching.Skip(Math.Max(0, offset)).Take(Math.Clamp(limit, 1, 100))]);
    }

    [JsonRpcMethod("folderImport/start")]
    public FolderImportProgress Start(string sessionId, string defaultTarget,
        Dictionary<string, string> overrides, string sectionTitle, string? tag = null)
        => Require(sessionId).Configure(defaultTarget, overrides, sectionTitle, tag);

    [JsonRpcMethod("folderImport/batch")]
    public Task<FolderImportProgress> BatchAsync(string sessionId) => Require(sessionId).ImportBatchAsync();

    [JsonRpcMethod("folderImport/release")]
    public void Release(string sessionId)
    {
        if (sessionId != _sessionId) return;
        _import = null;
        _sessionId = null;
    }

    [JsonRpcMethod("folderImport/exportSchema")]
    public Task ExportSchemaAsync(string outputPath)
    {
        if (!workspace.Projects.IsProjectLoaded) throw new InvalidOperationException("Open a project before exporting an import schema.");
        var schema = new FolderImportSchema(workspace.Projects.ActiveBook, new EntityService(workspace.Projects).GetCustomEntityTypes());
        return workspace.FileService.WriteTextAsync(outputPath, schema.Export());
    }

    private FolderImportService Require(string sessionId)
        => _import != null && sessionId == _sessionId ? _import
            : throw new InvalidOperationException("Choose the folder again before importing.");
}

public sealed record FolderImportScanDto(string SessionId, int Total, int Unsupported, int Folders);
public sealed record FolderImportPageDto(int Total, ImportFolder[] Items);
