using Novalist.Backend.Extensions;
using StreamJsonRpc;
using StreamJsonRpc.Protocol;

namespace Novalist.Backend;

/// <summary>
/// A JSON-RPC endpoint that runs one request at a time.
/// </summary>
/// <remarks>
/// <para>
/// Every one of the backend's seventy-odd facades is handed the same
/// <see cref="Workspace"/>, and none of the services behind it takes a lock:
/// they are written as though requests arrive one after another. They do not.
/// StreamJsonRpc dispatches concurrently, so any two overlapping requests run
/// against one set of collections, and the results were exactly what that
/// implies - a list being rebuilt while another request counted it, and a
/// discovery still running while another asked what it had found.
/// </para>
/// <para>
/// Both reached the writer as something quietly refusing to work: a Dashboard
/// that said its figures could not be worked out, and a Settings page certain
/// no extensions were installed while five of them sat on disk. Each was fixed
/// where it surfaced. This is the reason they kept surfacing.
/// </para>
/// <para>
/// The cost is real and worth stating: a long request holds everything else up
/// behind it. An export, a backup or a manuscript import will delay the next
/// scene save until it finishes. That is a pause; the alternative is a read of
/// half-written state, which is a wrong answer or a crash. A pause is the
/// better failure, and the services can be made properly concurrent one at a
/// time later without this having to be undone.
/// </para>
/// </remarks>
internal sealed class SerialDispatchJsonRpc : JsonRpc
{
    /// <summary>
    /// Only protocol names compiled into the backend are safe to repeat in a
    /// diagnostic log. The incoming request method is untrusted text too: a
    /// caller can put a chapter title there just as easily as in an argument.
    /// </summary>
    private static readonly HashSet<string> DiagnosticMethods = typeof(SerialDispatchJsonRpc)
        .Assembly
        .GetTypes()
        .SelectMany(type => type.GetMethods())
        .SelectMany(method => method.GetCustomAttributes(
            typeof(JsonRpcMethodAttribute), inherit: true)
            .Cast<JsonRpcMethodAttribute>())
        .Select(attribute => attribute.Name)
        .Where(name => !string.IsNullOrEmpty(name))
        .Select(name => name!)
        .ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// The calls that must not queue, because something already holding the
    /// gate is waiting for them.
    /// </summary>
    /// <remarks>
    /// The backend cannot show a dialog, so it asks the interface to and awaits
    /// the answer as a second request - a wizard step, a folder picker, the
    /// cancel button on a progress dialog. Queue those behind the handler that
    /// is waiting for them and the app hangs for good rather than merely
    /// pausing. None of them touch the project; they resolve a task the caller
    /// is already holding.
    ///
    /// system/ping and system/shutdown are here for a different reason: one
    /// reports whether the backend is alive, and the other asks it to stop.
    /// Neither is worth anything if a long export can silence it.
    /// </remarks>
    private static readonly HashSet<string> Reentrant = new(StringComparer.Ordinal)
    {
        "ui/wizard/choices",
        "ui/wizard/validate",
        "ui/wizard/complete",
        "ui/pick/complete",
        "ui/progress/cancel",
        "system/ping",
        "system/shutdown",
        // Stopping a reading is the same shape as cancelling a progress dialog:
        // a control call for something already in flight. Reading a scene aloud
        // is a run of voices/speak calls, each of which does not return until
        // its passage has been spoken - so queued, the request to stop could not
        // arrive until the thing it was meant to interrupt had finished. Pressing
        // Stop cleared the highlight and the writer went on listening to the
        // whole scene. It touches no project state; it tells the speech engine to
        // be quiet.
        "voices/stop",
        // Same shape again: a render holds the gate for as long as the engine
        // takes to speak its window, and the request to stop it must not be
        // queued behind the thing it is stopping.
        "narration/renderStop",
        // Asking how the audiobook render is going, and stopping it. The render
        // itself runs off the queue, but a poll that answers only between other
        // requests makes the progress bar stutter through the one thing it
        // exists to report - and Stop must never wait on the work it stops.
        "audiobook/status",
        "audiobook/stop",
    };

    /// <summary>
    /// Families that run outside the workspace, and must not be able to hold it.
    /// </summary>
    /// <remarks>
    /// These shell out to another program or stream whole directories, and they
    /// work from the filesystem rather than from the collections this gate
    /// exists to protect - so queueing behind them buys nothing and costs a
    /// great deal.
    ///
    /// Not a theoretical cost. With everything queued, the scheduled backup
    /// held the gate for four and a half seconds at startup and put every
    /// screen that far behind; worse, a <c>git</c> call that hung took the
    /// whole backend with it, where before it had only hung itself. A single
    /// stuck subprocess must not be able to freeze the application, and git
    /// talks to remotes, locks and credential prompts for a living.
    /// </remarks>
    private static readonly string[] Unsynchronised =
    [
        "git/",
        "backup/",
        "export/",
        // Preparing a speech engine builds a Python environment and downloads
        // several gigabytes of model. Queued, it held the gate for the whole of
        // that: every scene save, every view, every keystroke that needed the
        // backend waited behind a download. It touches nothing in the project -
        // it works in the extension's own folder - so there is nothing here for
        // the gate to protect.
        "voiceEngines/prepare",
        // Designing a voice is the same wait wearing a different name. The
        // first one loads a model the reading has not needed yet - gigabytes,
        // fetched then - and every one after it is tens of seconds inside a
        // model that cannot be interrupted. Only "prepare" was listed here, so
        // the wait nobody had been warned about was the one that held
        // everything: no scene saved and no view opened for the whole download.
        // It writes only into the voice store, which nothing else touches while
        // a design is in flight.
        "voiceEngines/design",
        "voiceEngines/audition",
        "narration/designNarrator",
        "narration/auditionLine",
        // The grammar check posts the whole scene to a language server and
        // waits up to thirty seconds for an answer. It reads a setting and
        // touches nothing else in the project, so there is nothing here for
        // the gate to protect - and holding the gate for that long meant
        // opening a scene in a book imported from Scrivener, where a scene can
        // be five thousand words, queued every other request behind one HTTP
        // round trip. The screen a writer was waiting for was waiting on the
        // sentence they had just stopped typing.
        "grammar/",
        // Estimating a render compiles the whole book, the same way an export
        // does - and "export/" is on this list for exactly that reason.
        "audiobook/estimate"
    ];

    private readonly SemaphoreSlim _gate = new(1, 1);

    public SerialDispatchJsonRpc(IJsonRpcMessageHandler handler) : base(handler)
    {
    }

    /// <summary>Whether this method skips the queue. Exposed for the tests that
    /// pin the list, since getting it wrong is a hang rather than a wrong answer.</summary>
    internal static bool IsReentrant(string? method)
    {
        if (method == null) return false;
        if (Reentrant.Contains(method)) return true;
        foreach (var family in Unsynchronised)
        {
            if (method.StartsWith(family, StringComparison.Ordinal)) return true;
        }
        return false;
    }

    protected override async ValueTask<JsonRpcMessage> DispatchRequestAsync(
        JsonRpcRequest request,
        TargetMethod targetMethod,
        CancellationToken cancellationToken)
    {
        if (IsReentrant(request.Method))
        {
            return await base.DispatchRequestAsync(request, targetMethod, cancellationToken)
                .ConfigureAwait(false);
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await base.DispatchRequestAsync(request, targetMethod, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Records RPC failures at the point StreamJsonRpc converts an exception
    /// into an error response. Request arguments, error messages and stacks are
    /// deliberately excluded: all three can contain manuscript content or
    /// paths. Method names are repeated only when they are part of the RPC
    /// vocabulary compiled into this backend assembly.
    /// </summary>
    protected override JsonRpcError.ErrorDetail CreateErrorDetails(
        JsonRpcRequest request, Exception exception)
    {
        var type = exception.GetType().FullName ?? exception.GetType().Name;
        Log.Error($"rpc failed method={DiagnosticMethodName(request.Method)} type={type}.");
        return base.CreateErrorDetails(request, exception);
    }

    internal static string DiagnosticMethodName(string? method)
        => method != null && DiagnosticMethods.Contains(method) ? method : "unknown";
}
