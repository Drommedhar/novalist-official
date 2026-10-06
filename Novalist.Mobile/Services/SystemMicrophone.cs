using System.Text.Json;
using Microsoft.Maui.ApplicationModel;
using Novalist.Backend.Dictation;

namespace Novalist.Mobile.Services;

public sealed class SystemMicrophone
{
    private readonly AppleSpeechBridge _speech = new();
    public async Task<JsonElement> StartAsync(CancellationToken token)
    {
        var permission = await MainThread.InvokeOnMainThreadAsync(Permissions.RequestAsync<Permissions.Microphone>);
        if (permission != PermissionStatus.Granted) throw new UnauthorizedAccessException("Microphone access denied.");
        return await RequestAsync("microphoneStart", token);
    }
    public Task<JsonElement> ReadAsync(CancellationToken token) => RequestAsync("microphoneRead", token);
    public Task<JsonElement> StopAsync() => RequestAsync("microphoneStop", CancellationToken.None);
    public async Task StopOnDisposeAsync()
    {
        try { await StopAsync(); }
        // aislop-ignore-next-line ai-slop/csharp-console-leftover -- Records shutdown failure type without exposing captured audio or transcript.
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[SystemMicrophone] stop failed: {ex.GetType().Name}"); }
    }
    private async Task<JsonElement> RequestAsync(string operation, CancellationToken token)
    {
        var json = await _speech.RequestAsync(new { operation }, token);
        return JsonSerializer.Deserialize<JsonElement>(json);
    }
}
