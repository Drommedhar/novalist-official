using System.Text.Json;
using Microsoft.Maui.ApplicationModel;

namespace Novalist.Mobile.Pages;

public sealed partial class RendererHostPage
{
    private async Task SendDocumentRequestAsync(int id, object parameters, CancellationToken ct)
    {
        await _rendererReady.Task.WaitAsync(ct);
        var data = JsonSerializer.Serialize(parameters);
        var script = $"window.__novalistCollaborationRequest?.({id},{data}) ?? 'unavailable'";
        var response = await MainThread.InvokeOnMainThreadAsync(() => EvaluateScriptAsync(script)).WaitAsync(ct);
        if (response?.Trim('"') != "accepted")
            throw new InvalidOperationException("The mobile shared document host is unavailable.");
    }
}
