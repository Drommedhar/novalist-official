using System.Net.Sockets;
using System.Net;
using System.Net.WebSockets;
using System.Threading.Channels;

namespace Novalist.Sync.Client;

public enum SessionConnectionState { Stopped, Connecting, Connected, Reconnecting, Failed, AccessEnded, KeyRefreshRequired }
public sealed record SessionConnectionStatus(SessionConnectionState State, Exception? Error = null);

public sealed class SessionSynchronizer : IAsyncDisposable
{
    private readonly CollaborationSession session;
    private readonly Channel<bool> pending = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });
    private readonly CancellationTokenSource lifetime = new();
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly object lifetimeGate = new();
    private readonly object statusGate = new();
    private Task loop = Task.CompletedTask;
    private bool started;
    private bool disposed;
    private Exception? terminalError;
    private SessionConnectionStatus status = new(SessionConnectionState.Stopped);
    public SessionConnectionStatus Status => Volatile.Read(ref status);

    public SessionSynchronizer(CollaborationSession session)
    {
        this.session = session;
        session.Saved += Wake;
        session.Stopping += Stop;
        session.ReceiveFailed += Fail;
    }

    public async Task StartAsync(bool createProject, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (started) throw new InvalidOperationException("The live session has already started.");
            using var operation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, ct);
            SetStatus(SessionConnectionState.Connecting);
            await session.SynchronizeAsync(createProject, operation.Token);
            operation.Token.ThrowIfCancellationRequested();
            SetStatus(SessionConnectionState.Connected);
            started = true;
            loop = RunAsync(lifetime.Token);
        }
        catch (Exception error)
        {
            SetStatus(error is OperationCanceledException ? SessionConnectionState.Stopped : SessionConnectionState.Failed, error);
            throw;
        }
        finally { gate.Release(); }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        var failures = 0;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                if (failures > 0) await Task.Delay(TimeSpan.FromSeconds(Math.Min(15, Math.Pow(2, failures - 1))), ct);
                else if (!await WaitForWorkAsync(ct) && session.IsConnected) continue;
                while (pending.Reader.TryRead(out _)) { }
                try
                {
                    await session.SynchronizeAsync(createProject: false, ct);
                    ct.ThrowIfCancellationRequested();
                    failures = 0;
                    SetStatus(SessionConnectionState.Connected);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
                catch (Exception error) when (IsTransient(error))
                {
                    failures = Math.Min(5, failures + 1);
                    SetStatus(SessionConnectionState.Reconnecting, error);
                }
                catch (Exception error) when (error is not OutOfMemoryException)
                {
                    Fail(error);
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        finally { if (Status.State != SessionConnectionState.Failed) SetStatus(SessionConnectionState.Stopped); }
    }

    private bool IsTransient(Exception error)
    {
        if (error is HttpRequestException http)
            return http.StatusCode is null or HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests ||
                (int)http.StatusCode is >= 500 and <= 599;
        return error is not InvalidDataException &&
            (error is IOException or SocketException or WebSocketException or TimeoutException ||
             error is OperationCanceledException { InnerException: TimeoutException } ||
             error is InvalidOperationException && !session.IsConnected);
    }

    private async Task<bool> WaitForWorkAsync(CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(1));
        try { return await pending.Reader.WaitToReadAsync(timeout.Token); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return false; }
    }

    private void Wake() => pending.Writer.TryWrite(true);
    private void SetStatus(SessionConnectionState state, Exception? error = null)
    {
        lock (statusGate) Volatile.Write(ref status, terminalError is null ? new(state, error) : new(SessionConnectionState.Failed, terminalError));
    }

    private void Fail(Exception error)
    {
        lock (statusGate)
        {
            terminalError ??= error;
            Volatile.Write(ref status, new(SessionConnectionState.Failed, terminalError));
        }
        Stop();
    }

    private void Stop()
    {
        lock (lifetimeGate) { if (!disposed) lifetime.Cancel(); }
    }

    public async ValueTask DisposeAsync()
    {
        Stop();
        await gate.WaitAsync();
        try
        {
            if (disposed) return;
            await loop;
            session.Saved -= Wake;
            session.Stopping -= Stop;
            session.ReceiveFailed -= Fail;
            lock (lifetimeGate) { disposed = true; lifetime.Dispose(); }
        }
        finally { gate.Release(); }
    }
}
