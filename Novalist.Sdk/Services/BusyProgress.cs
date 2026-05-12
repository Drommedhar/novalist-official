using System;
using System.Threading;

namespace Novalist.Sdk.Services;

/// <summary>
/// Options for showing a busy-progress dialog. Pass to
/// <see cref="IHostServices.ShowBusyProgress"/>.
/// </summary>
public sealed class BusyProgressOptions
{
    /// <summary>Dialog title shown at the top.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Initial status message shown beneath the title.</summary>
    public string InitialStatus { get; init; } = string.Empty;

    /// <summary>
    /// When true the bar animates indefinitely (marquee). When false the bar
    /// fills from 0 to 1 based on <see cref="IBusyProgress.SetProgress"/>.
    /// Default: true (indeterminate).
    /// </summary>
    public bool IsIndeterminate { get; init; } = true;

    /// <summary>When false the progress bar is hidden entirely.</summary>
    public bool ShowProgressBar { get; init; } = true;

    /// <summary>
    /// When true a Cancel button is shown. Clicking it triggers the
    /// <see cref="IBusyProgress.CancellationToken"/>.
    /// </summary>
    public bool AllowCancel { get; init; }

    /// <summary>
    /// Optional cancel button label. Defaults to the host's standard Cancel string.
    /// </summary>
    public string? CancelLabel { get; init; }

    /// <summary>
    /// When true the dialog is modal (blocks input on the main window).
    /// When false it appears as a non-blocking floating panel. Default: true.
    /// </summary>
    public bool IsModal { get; init; } = true;
}

/// <summary>
/// Handle to a currently-shown busy-progress dialog. Dispose to close it.
/// Methods are safe to call from any thread; the host marshals to the UI thread.
/// </summary>
public interface IBusyProgress : IDisposable
{
    /// <summary>Update the status text shown beneath the title.</summary>
    void SetStatus(string status);

    /// <summary>
    /// Update the progress bar. Value is clamped to [0, 1].
    /// Ignored when the dialog is indeterminate.
    /// </summary>
    void SetProgress(double value);

    /// <summary>
    /// Update the title text. Useful for multi-stage operations.
    /// </summary>
    void SetTitle(string title);

    /// <summary>
    /// Switch the bar between indeterminate (marquee) and determinate (0..1)
    /// at runtime.
    /// </summary>
    void SetIndeterminate(bool isIndeterminate);

    /// <summary>
    /// Set a list of secondary detail lines shown beneath the status text.
    /// Each line is rendered as its own row (use for ETA, counts, active
    /// per-item status, etc.). Pass null or empty to hide the detail block.
    /// </summary>
    void SetDetails(System.Collections.Generic.IReadOnlyList<string>? lines);

    /// <summary>
    /// Cancellation token that fires when the user clicks Cancel.
    /// Always present even when <see cref="BusyProgressOptions.AllowCancel"/>
    /// is false (in which case it never fires).
    /// </summary>
    CancellationToken CancellationToken { get; }

    /// <summary>True after <see cref="IDisposable.Dispose"/> has been called.</summary>
    bool IsClosed { get; }

    /// <summary>Fired on the UI thread when the user clicks Cancel.</summary>
    event Action? Cancelled;
}
