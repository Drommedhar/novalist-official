using System.Text;
using System.Text.Json;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Storage;
using Nerdbank.Streams;
using Novalist.Backend;
using Novalist.Core.Services;
using Novalist.Mobile.Services;
#if IOS
using CoreGraphics;
using UIKit;
using UniformTypeIdentifiers;
using WebKit;
#endif

namespace Novalist.Mobile.Pages;

/// <summary>
/// Phase 1 + 2: hosts the real React renderer (built by `npm run build:mobile`
/// into Resources/Raw/app) in a HybridWebView, replacing Electron's main +
/// child-process + preload.
///
/// Two channels share the one HybridWebView raw-message pipe, told apart by the
/// first byte of each JS->native message:
///   - RPC transport: base64 of LSP-framed JSON-RPC bytes, piped to the in-process
///     <see cref="BackendHost"/> over a FullDuplexStream pair. rpc/client.ts sees a
///     normal MessagePort (mobile/shim.ts), so it is unchanged.
///   - Host bridge: JSON `{id,method,args}` (starts with '{') implementing the
///     native window.novalist surface with MAUI Essentials.
/// </summary>
public sealed partial class RendererHostPage : ContentPage, IDisposable
{
    private readonly HybridWebView _web;
    private readonly BackendHost _host;
    private readonly IFileService _files = new CoordinatedFileService(new IosFileAccessCoordinator());
    private readonly ExportFiles _exports = new(FileSystem.Current.CacheDirectory);
    private readonly SystemMicrophone _microphone = new();
    private readonly Stream _bridge;
    private readonly CancellationTokenSource _cts = new();

    public RendererHostPage()
    {
        var (backendEnd, nativeEnd) = FullDuplexStream.CreatePair();
        _bridge = nativeEnd;
        // UnavailableProcessRunner: the sandbox forbids launching `git`, so Git
        // degrades to "unavailable" rather than throwing (Phase 3).
        // IosStoredPathResolver: a path in settings is not an address here - the
        // container moves with every update and the writer's own folders are
        // locked until their grant is resumed. Without it the recent-projects
        // list reads as a list of deleted projects and erases itself.
        _host = new BackendHost(
            FileSystem.Current.AppDataDirectory,
            new UnavailableProcessRunner(),
            new IosStoredPathResolver(),
            _files);
        _host.Attach(backendEnd, backendEnd);

        _web = new HybridWebView
        {
            HybridRoot = "app",
            DefaultFile = "index.mobile.html",
            VerticalOptions = LayoutOptions.Fill,
            HorizontalOptions = LayoutOptions.Fill,
        };
        _web.RawMessageReceived += OnRawMessageReceived;
        Content = _web;

#if IOS
        // Lock zoom once the WKWebView exists: prevents the iOS focus-zoom trap
        // (tapping a contenteditable auto-zooms the viewport with no way back).
        _web.HandlerChanged += (_, _) => LockWebViewZoom();
#endif

        _ = PumpBackendToWebAsync(_cts.Token);
    }
private void OnRawMessageReceived(object? sender, HybridWebViewRawMessageReceivedEventArgs e)
    {
        var message = e.Message;
        if (string.IsNullOrEmpty(message)) return;

        // '{' => host-bridge JSON; otherwise base64 RPC frame bytes.
        if (message[0] == '{')
        {
            _ = HandleHostCallAsync(message);
            return;
        }

        try
        {
            var bytes = Convert.FromBase64String(message);
            _bridge.Write(bytes, 0, bytes.Length);
            _bridge.Flush();
        }
        catch (Exception ex)
        {
            // aislop-ignore-next-line ai-slop/csharp-console-leftover -- Reports bridge failure type without exposing request payloads.
            System.Diagnostics.Debug.WriteLine($"[RendererHostPage] inbound bridge write failed: {ex.GetType().Name}");
        }
    }

    // native -> JS: backend response/notification bytes -> base64 -> the shim's
    // RPC receiver. EvaluateJavaScript must run on the UI thread.
    private async Task PumpBackendToWebAsync(CancellationToken ct)
    {
        var buffer = new byte[64 * 1024];
        while (!ct.IsCancellationRequested)
        {
            int read;
            try
            {
                read = await _bridge.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine($"[MobileBridge] Backend read failed: {exception.GetType().Name}");
                break;
            }
            if (read <= 0) break;

            var payload = Convert.ToBase64String(buffer, 0, read);
            await EvalOnMainAsync($"window.__novalistRecv('{payload}')").ConfigureAwait(false);
        }
    }

    // ---- Host bridge (window.novalist) --------------------------------------

    private async Task HandleHostCallAsync(string json)
    {
        var id = 0;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            id = root.GetProperty("id").GetInt32();
            var method = root.GetProperty("method").GetString() ?? "";
            var args = root.TryGetProperty("args", out var a) ? a : default;
            var result = await InvokeHostAsync(method, args).ConfigureAwait(false);
            await SendHostResultAsync(id, ok: true, result, error: null).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await SendHostResultAsync(id, ok: false, result: null, error: ex.Message).ConfigureAwait(false);
        }
    }

    private static readonly JsonSerializerOptions CamelCase =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private Task SendHostResultAsync(int id, bool ok, object? result, string? error)
    {
        // camelCase so the shim reads {id, ok, result, error}.
        var payload = JsonSerializer.Serialize(new HostResult(id, ok, result, error), CamelCase);
        var b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(payload));
        return EvalOnMainAsync($"window.__novalistHostResult('{b64}')");
    }

    private Task EvalOnMainAsync(string js) =>
        MainThread.InvokeOnMainThreadAsync(async () =>
        {
            try { await _web.EvaluateJavaScriptAsync(js); }
            catch (Exception ex)
            {
                // aislop-ignore-next-line ai-slop/csharp-console-leftover -- Reports evaluation failure type without exposing script or response contents.
                System.Diagnostics.Debug.WriteLine($"[RendererHostPage] eval failed: {ex.GetType().Name}");
            }
        });

    private sealed record HostResult(int Id, bool Ok, object? Result, string? Error);

    public void Dispose()
    {
        _ = _microphone.StopOnDisposeAsync();
        _cts.Cancel();
#if IOS
        // The probe outlives the page otherwise and would keep calling back.
        if (_probe != null) _probe.LayoutChanged = null;
#endif
        _web.RawMessageReceived -= OnRawMessageReceived;
        _host.Dispose();
        _bridge.Dispose();
        _cts.Dispose();
    }
}
