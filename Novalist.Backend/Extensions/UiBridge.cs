using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Novalist.Sdk.Models.Wizards;
using Novalist.Sdk.Services;

namespace Novalist.Backend.Extensions;

/// <summary>
/// Bridges imperative host-service capabilities (toasts, busy-progress dialogs,
/// interactive wizards) to the renderer over JSON-RPC. The backend is the RPC
/// server, so it pushes <c>ui/*</c> notifications to the renderer and correlates
/// interactive round-trips by an opaque token.
///
/// This replaces the Avalonia dialog/overlay wiring the old desktop shell used
/// (MainWindow.BusyProgressFactory, App.WizardLauncher, MainWindowViewModel's
/// NotificationRequested handler).
/// </summary>
public sealed class UiBridge
{
    /// <summary>Sends a <c>ui/*</c> JSON-RPC notification to the renderer with a
    /// single positional payload. Set by <see cref="BackendHost"/> once the RPC
    /// endpoint is attached; a no-op until then.</summary>
    internal Func<string, object?, Task> Notifier { get; set; } = static (_, _) => Task.CompletedTask;

    private readonly ConcurrentDictionary<string, RpcBusyProgress> _progress = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, WizardSession> _wizards = new(StringComparer.Ordinal);

    private void Send(string method, object? payload) => _ = Notifier(method, payload);

    // ── Notifications ───────────────────────────────────────────────

    /// <summary>Pushes a toast message to the renderer.</summary>
    public void ShowNotification(string message) => Send("ui/showNotification", message);

    // ── Busy progress ───────────────────────────────────────────────

    /// <summary>Opens an RPC-backed busy-progress dialog and returns the handle
    /// the extension drives. Wired as <see cref="HostServices.BusyProgressFactory"/>.</summary>
    public IBusyProgress CreateProgress(BusyProgressOptions options)
    {
        var token = Guid.NewGuid().ToString("N");
        var handle = new RpcBusyProgress(this, token);
        _progress[token] = handle;
        Send("ui/progress/open", new ProgressOpenDto(
            token, options.Title, options.InitialStatus, options.IsIndeterminate,
            options.ShowProgressBar, options.AllowCancel, options.CancelLabel, options.IsModal));
        return handle;
    }

    /// <summary>Renderer → host: the user clicked Cancel on a progress dialog.</summary>
    public void CancelProgress(string token)
    {
        if (_progress.TryGetValue(token, out var handle))
            handle.RaiseCancelled();
    }

    private void ProgressUpdate(string token, string field, object? value)
        => Send("ui/progress/update", new ProgressUpdateDto(token, field, value));

    private void ProgressClose(string token)
    {
        _progress.TryRemove(token, out _);
        Send("ui/progress/close", token);
    }

    // ── Wizards ─────────────────────────────────────────────────────

    /// <summary>Runs a wizard interactively in the renderer and awaits its
    /// result. Wired as <see cref="HostServices.WizardLauncher"/>.</summary>
    public Task<WizardResult?> RunWizardAsync(WizardDefinition definition, WizardResult? seed)
    {
        var token = Guid.NewGuid().ToString("N");
        var tcs = new TaskCompletionSource<WizardResult?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _wizards[token] = new WizardSession(definition, tcs);
        Send("ui/wizard/open", new WizardOpenDto(token, WizardMapper.ToDto(definition), seed));
        return tcs.Task;
    }

    /// <summary>Renderer → host: fetch dynamic choices for a choice step whose
    /// options are provided by a runtime callback.</summary>
    public async Task<IReadOnlyList<WizardChoiceDto>> WizardChoicesAsync(string token, string stepId, WizardResult partial)
    {
        if (!_wizards.TryGetValue(token, out var session))
            return [];
        if (WizardMapper.FindStep(session.Definition.Steps, stepId) is not ChoiceStep step
            || step.DynamicChoicesProvider == null)
            return [];
        var choices = await step.DynamicChoicesProvider(partial ?? new WizardResult());
        return choices.Select(c => new WizardChoiceDto(c.Value, c.Label, c.Description)).ToList();
    }

    /// <summary>Renderer → host: validate a step before advancing. Returns null
    /// when valid, or an error message to surface inline.</summary>
    public async Task<string?> WizardValidateAsync(string token, string stepId, WizardResult partial)
    {
        if (!_wizards.TryGetValue(token, out var session))
            return null;
        var step = WizardMapper.FindStep(session.Definition.Steps, stepId);
        if (step?.Validator == null)
            return null;
        return await step.Validator(partial ?? new WizardResult());
    }

    /// <summary>Renderer → host: the wizard finished (result set) or was cancelled
    /// (result null). Resolves the pending <see cref="RunWizardAsync"/> task.</summary>
    public void CompleteWizard(string token, WizardResult? result)
    {
        if (_wizards.TryRemove(token, out var session))
            session.Completion.TrySetResult(result);
    }

    private sealed record WizardSession(WizardDefinition Definition, TaskCompletionSource<WizardResult?> Completion);

    // ── RPC-backed IBusyProgress ────────────────────────────────────

    private sealed class RpcBusyProgress : IBusyProgress
    {
        private readonly UiBridge _bridge;
        private readonly string _token;
        private readonly CancellationTokenSource _cts = new();
        private int _closed;

        public RpcBusyProgress(UiBridge bridge, string token)
        {
            _bridge = bridge;
            _token = token;
        }

        public CancellationToken CancellationToken => _cts.Token;
        public bool IsClosed => Volatile.Read(ref _closed) == 1;
        public event Action? Cancelled;

        public void SetStatus(string status) => Update("status", status);
        public void SetProgress(double value) => Update("progress", Math.Clamp(value, 0, 1));
        public void SetTitle(string title) => Update("title", title);
        public void SetIndeterminate(bool isIndeterminate) => Update("indeterminate", isIndeterminate);
        public void SetDetails(IReadOnlyList<string>? lines) => Update("details", lines ?? []);

        private void Update(string field, object? value)
        {
            if (IsClosed) return;
            _bridge.ProgressUpdate(_token, field, value);
        }

        internal void RaiseCancelled()
        {
            if (IsClosed) return;
            try { _cts.Cancel(); } catch (ObjectDisposedException) { /* already disposed */ }
            Cancelled?.Invoke();
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _closed, 1) == 1) return;
            _bridge.ProgressClose(_token);
            _cts.Dispose();
        }
    }
}

// ── DTOs pushed to / received from the renderer ─────────────────────

/// <summary>Payload for <c>ui/progress/open</c>.</summary>
public sealed record ProgressOpenDto(
    string Token, string Title, string InitialStatus, bool IsIndeterminate,
    bool ShowProgressBar, bool AllowCancel, string? CancelLabel, bool IsModal);

/// <summary>Payload for <c>ui/progress/update</c>. <paramref name="Value"/> is a
/// string, number, bool, or string[] depending on <paramref name="Field"/>.</summary>
public sealed record ProgressUpdateDto(string Token, string Field, object? Value);

/// <summary>Payload for <c>ui/wizard/open</c>.</summary>
public sealed record WizardOpenDto(string Token, WizardDefinitionDto Definition, WizardResult? Seed);
