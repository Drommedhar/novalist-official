using System.Text.Json;
using Nerdbank.Streams;
using Novalist.Backend;
using StreamJsonRpc;
using Xunit;

namespace Novalist.Backend.Tests;

/// <summary>
/// The backend answers one request at a time.
///
/// Every facade shares one <see cref="Workspace"/> and none of the services
/// behind it takes a lock - they are written as though requests arrive one
/// after another, and StreamJsonRpc dispatched them concurrently. Two bugs
/// reached a writer that way: a Dashboard that said its figures could not be
/// worked out, and Settings certain no extensions were installed while five sat
/// on disk. Both were a read of state something else was still rebuilding.
/// </summary>
public sealed class SerialDispatchTests : IDisposable
{
    private readonly string _root;

    public SerialDispatchTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "nl-serial-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    /// <summary>A target whose calls report whether any two ever overlapped.</summary>
    private sealed class Overlap
    {
        private int _inside;

        public int Peak { get; private set; }
        public TaskCompletionSource HeldSlowEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseHeldSlow { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        [JsonRpcMethod("spec/slow")]
        public async Task<int> SlowAsync()
        {
            var now = Interlocked.Increment(ref _inside);
            if (now > Peak) Peak = now;
            await Task.Delay(40);
            Interlocked.Decrement(ref _inside);
            return now;
        }

        [JsonRpcMethod("system/ping")]
        public async Task<int> PingAsync()
        {
            var now = Interlocked.Increment(ref _inside);
            if (now > Peak) Peak = now;
            await Task.Delay(40);
            Interlocked.Decrement(ref _inside);
            return now;
        }

        [JsonRpcMethod("spec/heldSlow")]
        public async Task<int> HeldSlowAsync()
        {
            var now = Interlocked.Increment(ref _inside);
            if (now > Peak) Peak = now;
            HeldSlowEntered.TrySetResult();
            await ReleaseHeldSlow.Task;
            Interlocked.Decrement(ref _inside);
            return now;
        }
    }

    private static (JsonRpc client, Overlap target, IDisposable server) Pair(bool serial)
    {
        var streams = FullDuplexStream.CreatePair();
        var formatter = new SystemTextJsonFormatter();
        formatter.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        var handler = new HeaderDelimitedMessageHandler(streams.Item1, streams.Item1, formatter);
        var target = new Overlap();
        JsonRpc server = serial ? new SerialDispatchJsonRpc(handler) : new JsonRpc(handler);
        server.AddLocalRpcTarget(target, new JsonRpcTargetOptions { DisposeOnDisconnect = false });
        server.StartListening();

        var clientFormatter = new SystemTextJsonFormatter();
        clientFormatter.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        var client = new JsonRpc(
            new HeaderDelimitedMessageHandler(streams.Item2, streams.Item2, clientFormatter));
        client.StartListening();
        return (client, target, server);
    }

    [Fact]
    public async Task TwoRequestsAtOnce_DoNotOverlap()
    {
        var (client, target, server) = Pair(serial: true);
        using (client)
        using (server)
        {
            await Task.WhenAll(
                client.InvokeAsync<int>("spec/slow"),
                client.InvokeAsync<int>("spec/slow"),
                client.InvokeAsync<int>("spec/slow"));
        }

        Assert.Equal(1, target.Peak);
    }

    [Fact]
    public async Task WithoutTheGate_TheyDoOverlap()
    {
        // The guard on the guard: a test that passes because the calls happened
        // to be quick would prove nothing about the gate.
        var (client, target, server) = Pair(serial: false);
        using (client)
        using (server)
        {
            await Task.WhenAll(
                client.InvokeAsync<int>("spec/slow"),
                client.InvokeAsync<int>("spec/slow"),
                client.InvokeAsync<int>("spec/slow"));
        }

        Assert.True(target.Peak > 1, "the plain endpoint should have run them together");
    }

    [Fact]
    public async Task AskingWhetherTheBackendIsAlive_DoesNotQueue()
    {
        // system/ping answers whether the backend is up. Queued behind a long
        // export it would report a healthy backend as dead.
        var (client, target, server) = Pair(serial: true);
        using (client)
        using (server)
        {
            var slow = client.InvokeAsync<int>("spec/heldSlow");
            try
            {
                // Do not ask the scheduler to make two short calls happen to
                // overlap. Hold the serialized call open after proving it has
                // entered, then require ping to answer while it is still there.
                await target.HeldSlowEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                await client.InvokeAsync<int>("system/ping").WaitAsync(TimeSpan.FromSeconds(5));
            }
            finally
            {
                target.ReleaseHeldSlow.TrySetResult();
            }
            await slow.WaitAsync(TimeSpan.FromSeconds(5));
        }

        Assert.True(target.Peak > 1, "system/ping should have run alongside the slow call");
    }

    [Fact]
    public void TheCallsThatMustNotQueue_AreTheOnesThatAnswerAWaitingHandler()
    {
        // Pinned deliberately. The backend cannot show a dialog, so it asks the
        // interface to and awaits the reply as a second request; queue one of
        // these behind the handler waiting for it and the app hangs for good
        // rather than merely pausing. Dropping an entry from this list is a
        // freeze, which is why it is asserted rather than left to review.
        foreach (var method in new[]
                 {
                     "ui/wizard/choices",
                     "ui/wizard/validate",
                     "ui/wizard/complete",
                     "ui/pick/complete",
                     "ui/progress/cancel",
                     "system/ping",
                     "system/shutdown",
                     // A reading is a run of voices/speak calls that each last as
                     // long as the passage they speak. Queued behind one of them,
                     // Stop could not be heard until the passage had finished -
                     // which is to say Stop did not stop anything.
                     "voices/stop",
                     "narration/renderStop",
                     // These run outside the workspace - another program, or
                     // whole directories of files. Queueing behind them buys
                     // nothing, and a git call that hangs would otherwise take
                     // the whole backend with it rather than only itself.
                     "git/status",
                     "git/changedScenes",
                     "backup/create",
                     "export/run",
                     // Posts the scene to a language server and waits up to
                     // thirty seconds. Queued, one grammar check held every
                     // other screen in the app behind it.
                     "grammar/check",
                     // Builds a Python environment and downloads gigabytes of
                     // model. Queued, it froze the application for the duration.
                     "voiceEngines/prepare"
                 })
        {
            Assert.True(SerialDispatchJsonRpc.IsReentrant(method), method + " must skip the queue");
        }

        // And everything that touches the project's own state must not.
        foreach (var method in new[] { "dashboard/get", "extensions/load", "scenes/write", "entities/list" })
        {
            Assert.False(SerialDispatchJsonRpc.IsReentrant(method), method + " must queue");
        }

        Assert.False(SerialDispatchJsonRpc.IsReentrant(null));
    }
}
