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
    private readonly TemporaryAccessRegistry _manuscriptAccess = new(ReportScopeReleaseFailure);
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
        _web.HandlerChanged += (_, _) =>
        {
            LockWebViewZoom();
            ObserveWebProcess();
        };
#endif

        _ = PumpBackendToWebAsync(_cts.Token);
    }
    private void OnRawMessageReceived(object? sender, HybridWebViewRawMessageReceivedEventArgs e)
    {
        var message = e.Message;
        if (string.IsNullOrEmpty(message) || _disposed != 0) return;

        // '{' => host-bridge JSON; otherwise base64 RPC frame bytes.
        if (message[0] == '{')
        {
            _ = HandleHostCallAsync(message);
            return;
        }

        try
        {
            if (_bridgeFailed != 0) throw new IOException("The editor connection stopped.");
            var bytes = Convert.FromBase64String(message);
            _bridge.Write(bytes, 0, bytes.Length);
            _bridge.Flush();
        }
        catch (Exception ex)
        {
            _ = FailBridgeAsync(ex);
        }
    }

    // native -> JS: backend response/notification bytes -> base64 -> the shim's
    // RPC receiver. EvaluateJavaScript must run on the UI thread.
    private async Task PumpBackendToWebAsync(CancellationToken ct)
    {
        try
        {
            await _rendererReady.Task.WaitAsync(ct).ConfigureAwait(false);
            var buffer = new byte[64 * 1024];
            while (!ct.IsCancellationRequested)
            {
                var read = await _bridge.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false);
                if (read <= 0) throw new EndOfStreamException("The editor connection closed.");
                var payload = Convert.ToBase64String(buffer, 0, read);
                await DeliverCallbackAsync($"window.__novalistRecv?.('{payload}')").ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception exception) { await FailBridgeAsync(exception).ConfigureAwait(false); }
    }

    private async Task HandleHostCallAsync(string json)
    {
        var id = 0;
        object? result = null;
        Exception? failure = null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            id = root.GetProperty("id").GetInt32();
            var method = root.GetProperty("method").GetString() ?? "";
            var args = root.TryGetProperty("args", out var a) ? a : default;
            result = await InvokeHostAsync(method, args).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            failure = ex;
        }
        try
        {
            await SendHostResultAsync(id, failure == null, result, failure?.Message,
                failure is UnauthorizedAccessException ? "permission-denied" : null).ConfigureAwait(false);
        }
        catch (Exception exception) { await FailBridgeAsync(exception).ConfigureAwait(false); }
    }

    private static readonly JsonSerializerOptions CamelCase =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private Task SendHostResultAsync(int id, bool ok, object? result, string? error, string? errorCode = null)
    {
        // camelCase so the shim reads {id, ok, result, error}.
        var payload = JsonSerializer.Serialize(new HostResult(id, ok, result, error, errorCode), CamelCase);
        var b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(payload));
        return DeliverCallbackAsync($"window.__novalistHostResult?.('{b64}')");
    }

    private Task EvalOnMainAsync(string js) =>
        MainThread.InvokeOnMainThreadAsync(async () =>
        {
            try { await EvaluateScriptAsync(js); }
            catch (Exception ex)
            {
                // aislop-ignore-next-line ai-slop/csharp-console-leftover -- Reports evaluation failure type without exposing script or response contents.
                System.Diagnostics.Debug.WriteLine($"[RendererHostPage] eval failed: {ex.GetType().Name}");
            }
        });

    private sealed record HostResult(int Id, bool Ok, object? Result, string? Error, string? ErrorCode);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _manuscriptAccess.Dispose();
        DisposeLifecycle();
        _ = _microphone.StopOnDisposeAsync();
        _cts.Cancel();
#if IOS
        // The probe outlives the page otherwise and would keep calling back.
        if (_probe != null) _probe.LayoutChanged = null;
        DisposeWebProcessObserver();
#endif
        _web.RawMessageReceived -= OnRawMessageReceived;
        _host.Dispose();
        IosStoredPathResolver.ActiveProjectPath = null;
        SecurityScopedFolders.ReleaseExcept(null);
        _bridge.Dispose();
        _cts.Dispose();
    }

    private static void ReportScopeReleaseFailure(Exception error)
    {
        // aislop-ignore-next-line ai-slop/csharp-console-leftover -- Records only the native failure type, without manuscript paths or contents.
        System.Diagnostics.Debug.WriteLine($"[MobileScope] Temporary release failed: {error.GetType().Name}");
    }
}
