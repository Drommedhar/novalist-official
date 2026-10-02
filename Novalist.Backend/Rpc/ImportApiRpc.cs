using Novalist.Backend.ImportApi;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

public sealed class ImportApiRpc(LocalImportApi api)
{
    [JsonRpcMethod("importApi/start")]
    public Task<ImportApiStatus> StartAsync(string sectionTitle) => api.StartAsync(sectionTitle);

    [JsonRpcMethod("importApi/status")]
    public Task<ImportApiStatus> StatusAsync() => api.StatusAsync();

    [JsonRpcMethod("importApi/stop")]
    public Task StopAsync() => api.StopAsync();
}
