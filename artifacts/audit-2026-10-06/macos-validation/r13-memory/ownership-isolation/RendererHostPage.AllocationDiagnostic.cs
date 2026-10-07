// TEMPORARY AUDIT INSTRUMENTATION. Never include in a shipped application.
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Maui.ApplicationModel;
using Foundation;
using ObjCRuntime;
using UIKit;
using WebKit;

namespace Novalist.Mobile.Pages;

public sealed partial class RendererHostPage
{
    private readonly string _auditPage = Guid.NewGuid().ToString("N");
    private readonly object _auditGate = new();
    private readonly List<object> _auditEvents = [];
    private int _auditEvaluatorFault;
    private int _auditAssetGateArmed;
    private int _auditAssetReads;
    private int _auditProjectGeneration;
    private TaskCompletionSource? _auditAssetGate;
    private static readonly string AuditDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "NovalistAudit");

    private void AuditRecord(string kind, string? detail = null)
    {
        lock (_auditGate)
        {
            _auditEvents.Add(new { kind, detail, utc = DateTimeOffset.UtcNow, ticks = Stopwatch.GetTimestamp() });
            if (_auditEvents.Count > 100) _auditEvents.RemoveAt(0);
        }
    }

    private Task<string?> AuditEvaluateDeliveryAsync(string script)
    {
        switch (Interlocked.Exchange(ref _auditEvaluatorFault, 0))
        {
            case 1:
                AuditRecord("evaluator-failure-consumed");
                return Task.FromException<string?>(new IOException("Synthetic audit evaluator failure."));
            case 3:
                AuditRecord("evaluator-native-failure-consumed");
                return EvaluateScriptAsync("throw new Error('Synthetic native evaluation failure')");
            case 2:
                AuditRecord("evaluator-timeout-consumed");
                return new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously).Task;
            default:
                return EvaluateScriptAsync(script);
        }
    }

    private async Task AuditAfterAssetReadAsync()
    {
        Interlocked.Increment(ref _auditAssetReads);
        if (Interlocked.Exchange(ref _auditAssetGateArmed, 0) == 0) return;
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _auditAssetGate = gate;
        AuditRecord("asset-reply-held", _auditProjectGeneration.ToString());
        await gate.Task.ConfigureAwait(false);
        AuditRecord("asset-reply-released", _auditProjectGeneration.ToString());
    }

    private async Task AuditControlLoopAsync()
    {
        Directory.CreateDirectory(AuditDirectory);
        AuditRecord("control-started");
        await _rendererReady.Task.ConfigureAwait(false);
        AuditRecord("renderer-ready");
        await File.WriteAllTextAsync(Path.Combine(AuditDirectory, "ready.json"),
            JsonSerializer.Serialize(new { page = _auditPage, ready = true, utc = DateTimeOffset.UtcNow }));
        while (Volatile.Read(ref _disposed) == 0)
        {
            await Task.Delay(100).ConfigureAwait(false);
            var commandFile = Path.Combine(AuditDirectory, "command.json");
            if (!File.Exists(commandFile)) continue;
            string id = "unread";
            object? result = null;
            string? errorType = null;
            var started = DateTimeOffset.UtcNow;
            try
            {
                var contents = await File.ReadAllTextAsync(commandFile).ConfigureAwait(false);
                File.Delete(commandFile);
                using var command = JsonDocument.Parse(contents);
                var root = command.RootElement;
                id = root.GetProperty("id").GetString() ?? "missing";
                result = await AuditExecuteAsync(root).ConfigureAwait(false);
            }
            catch (Exception exception) { errorType = exception.GetType().Name; }
            var response = JsonSerializer.Serialize(new {
                id, page = _auditPage, started, finished = DateTimeOffset.UtcNow,
                ok = errorType == null, errorType, result
            });
            var temporary = Path.Combine(AuditDirectory, "response.tmp");
            await File.WriteAllTextAsync(temporary, response).ConfigureAwait(false);
            File.Move(temporary, Path.Combine(AuditDirectory, "response.json"), true);
        }
    }

    private async Task<object?> AuditExecuteAsync(JsonElement command)
    {
        switch (command.GetProperty("op").GetString())
        {
            case "string-allocation-only":
            case "string-owned-allocation-only":
                return await MainThread.InvokeOnMainThreadAsync(() => {
                    var counts = new List<ulong>();
                    var disposed = new List<bool>();
                    for (var index = 0; index < 12; index++) {
                        var text = new string('a', 10_190_000) + index.ToString();
                        var source = command.GetProperty("op").GetString() == "string-owned-allocation-only"
                            ? ObjCRuntime.Runtime.GetNSObject<NSString>(NSString.CreateNative(text), owns: true)!
                            : new NSString(text);
                        counts.Add((ulong)source.RetainCount);
                        source.Dispose();
                        disposed.Add(source.Handle == IntPtr.Zero);
                    }
                    return new { allocations = 12, retainCountsBeforeDispose = counts, handlesCleared = disposed };
                });
            case "eval":
                return await MainThread.InvokeOnMainThreadAsync(() =>
                    EvaluateScriptAsync(command.GetProperty("script").GetString()!))
                    .WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            case "state":
                return await MainThread.InvokeOnMainThreadAsync(AuditState);
            case "evaluator-failure":
                Interlocked.Exchange(ref _auditEvaluatorFault, 1);
                AuditRecord("evaluator-failure-armed");
                return "armed";
            case "evaluator-native-failure":
                Interlocked.Exchange(ref _auditEvaluatorFault, 3);
                AuditRecord("evaluator-native-failure-armed");
                return "armed";
            case "evaluator-timeout":
                Interlocked.Exchange(ref _auditEvaluatorFault, 2);
                AuditRecord("evaluator-timeout-armed");
                return "armed";
            case "backend-failure":
                AuditRecord("backend-dispose");
                _bridge.Dispose();
                return "disposed";
            case "asset-hold":
                Interlocked.Exchange(ref _auditAssetGateArmed, 1);
                return "armed";
            case "asset-release":
                return _auditAssetGate?.TrySetResult() == true;
            case "reload":
                await MainThread.InvokeOnMainThreadAsync(() => {
                    var window = Window ?? throw new InvalidOperationException("No window");
                    Dispose();
                    window.Page = new RendererHostPage();
                });
                return "reloaded";
            default:
                throw new InvalidOperationException("Unknown synthetic audit operation.");
        }
    }

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern int AuditIntMessage(nint receiver, nint selector);

    private object AuditState()
    {
        int? webProcessId = null;
        if (_web.Handler?.PlatformView is WKWebView web)
        {
            var selector = new Selector("_webProcessIdentifier");
            if (web.RespondsToSelector(selector)) webProcessId = AuditIntMessage(web.Handle, selector.Handle);
        }
        var controller = (Window?.Handler?.PlatformView as UIWindow)?.RootViewController;
        while (controller?.PresentedViewController is { } presented) controller = presented;
        var alert = controller as UIAlertController;
        object[] events;
        lock (_auditGate) events = _auditEvents.ToArray();
        return new {
            page = _auditPage, bridgeFailed = _bridgeFailed, disposed = _disposed,
            rendererReady = _rendererReady.Task.IsCompletedSuccessfully,
            bridgeCancellation = _cts.IsCancellationRequested,
            evaluatorFault = _auditEvaluatorFault, assetReads = _auditAssetReads,
            assetHeld = _auditAssetGate is { Task.IsCompleted: false },
            projectGeneration = _auditProjectGeneration, webProcessId,
            nativeEditingPausedAlert = alert?.Title == "Editing paused",
            nativeReloadAvailable = alert?.Actions.Any(action => action.Title == "Reload") == true,
            nativeLaterAvailable = alert?.Actions.Any(action => action.Title == "Later") == true,
            events
        };
    }
}
