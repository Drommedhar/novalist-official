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
        // The grammar check posts the whole scene to a language server and
        // waits up to thirty seconds for an answer. It reads a setting and
        // touches nothing else in the project, so there is nothing here for
        // the gate to protect - and holding the gate for that long meant
        // opening a scene in a book imported from Scrivener, where a scene can
        // be five thousand words, queued every other request behind one HTTP
        // round trip. The screen a writer was waiting for was waiting on the
        // sentence they had just stopped typing.
        "grammar/"
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
}
