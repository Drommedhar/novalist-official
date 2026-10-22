#if NOVALIST_STEAMWORKS
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Steamworks;

namespace Novalist.Sync.Client;

internal sealed partial class SteamNativeRelay : SteamPeerRelay
{
    private static readonly SemaphoreSlim runtimeGate = new(1, 1);
    private static SteamRuntimeStatus? activeStatus;
    private readonly object nativeGate = new();
    private readonly CancellationTokenSource stopping = new();
    private readonly ConcurrentDictionary<HSteamNetConnection, Connection> connections = new();
    private readonly Callback<SteamNetConnectionStatusChangedCallback_t> callback;
    private readonly HSteamListenSocket listenSocket;
    private readonly TcpListener? localListener;
    private readonly int hostPort;
    private readonly ulong remoteId;
    private Task accepting = Task.CompletedTask;
    private Task polling = Task.CompletedTask;
    private bool disposed;
    public override int LocalPort { get; }
    public override ulong SteamId { get; }
    public override Exception? Failure { get; protected set; }

    private SteamNativeRelay(int hostPort, ulong remoteId, SteamRuntimeStatus status)
    {
        this.hostPort = hostPort;
        this.remoteId = remoteId;
        SteamId = status.SteamId;
        callback = Callback<SteamNetConnectionStatusChangedCallback_t>.Create(OnStateChanged);
        try
        {
            SteamNetworkingUtils.InitRelayNetworkAccess();
            if (remoteId == 0)
            {
                listenSocket = SteamNetworkingSockets.CreateListenSocketP2P(0, 0, []);
                if (listenSocket == HSteamListenSocket.Invalid) throw new IOException("Steam could not create a peer listener.");
                LocalPort = hostPort;
            }
            else
            {
                localListener = new(IPAddress.Loopback, 0);
                localListener.Start();
                LocalPort = ((IPEndPoint)localListener.LocalEndpoint).Port;
                accepting = AcceptLocalAsync(localListener, stopping.Token);
            }
            polling = PollAsync(stopping.Token);
        }
        catch { localListener?.Stop(); callback.Dispose(); stopping.Dispose(); throw; }
    }

    internal new static SteamRuntimeStatus Status()
    {
        if (!runtimeGate.Wait(0)) return activeStatus ?? new(false, "Steam is starting another collaboration session.");
        try
        {
            var result = Initialize();
            if (result.Available) SteamAPI.Shutdown();
            return result;
        }
        finally { runtimeGate.Release(); }
    }

    internal static Task<SteamPeerRelay> CreateAsync(int hostPort, ulong remoteId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (remoteId == 0 && hostPort is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(hostPort));
        if (!runtimeGate.Wait(0)) throw new InvalidOperationException("End the current Steam session first.");
        try
        {
            activeStatus = Initialize();
            if (!activeStatus.Available) throw new InvalidOperationException(activeStatus.Reason);
            return Task.FromResult<SteamPeerRelay>(new SteamNativeRelay(hostPort, remoteId, activeStatus));
        }
        catch
        {
            if (activeStatus?.Available == true) SteamAPI.Shutdown();
            activeStatus = null;
            runtimeGate.Release();
            throw;
        }
    }

    private static SteamRuntimeStatus Initialize()
    {
        var appIdText = Environment.GetEnvironmentVariable("NOVALIST_STEAM_APP_ID") ?? Environment.GetEnvironmentVariable("SteamAppId");
        if (!uint.TryParse(appIdText, out var appId) || appId == 0)
            return new(false, "Launch Novalist through Steam, or configure NOVALIST_STEAM_APP_ID for your own registered app.");
        Environment.SetEnvironmentVariable("SteamAppId", appIdText);
        try
        {
            if (SteamAPI.InitEx(out var error) != ESteamAPIInitResult.k_ESteamAPIInitResult_OK)
                return new(false, "Steam initialization failed: " + error);
            if (!SteamUser.BLoggedOn() || SteamUtils.GetAppID().m_AppId != appId)
            {
                SteamAPI.Shutdown();
                return new(false, "Sign in to Steam with an account that has access to this Novalist app.");
            }
            return new(true, "", SteamUser.GetSteamID().m_SteamID, SteamFriends.GetPersonaName());
        }
        catch (Exception error) when (error is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException)
        {
            return new(false, "The Steamworks SDK native library is missing or incompatible: " + error.Message);
        }
    }

    private async Task AcceptLocalAsync(TcpListener listener, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var socket = await listener.AcceptTcpClientAsync(ct);
                socket.NoDelay = true;
                lock (nativeGate)
                {
                    var identity = new SteamNetworkingIdentity();
                    identity.SetSteamID(new CSteamID(remoteId));
                    var handle = SteamNetworkingSockets.ConnectP2P(ref identity, 0, 0, []);
                    if (handle == HSteamNetConnection.Invalid) { socket.Dispose(); throw new IOException("Steam could not connect to the host."); }
                    StartConnection(handle, socket);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception error) when (error is IOException or SocketException)
        {
            Failure = error;
            stopping.Cancel();
        }
    }

    private void OnStateChanged(SteamNetConnectionStatusChangedCallback_t change)
    {
        var handle = change.m_hConn;
        if (change.m_info.m_eState == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connecting &&
            listenSocket != HSteamListenSocket.Invalid && change.m_info.m_hListenSocket == listenSocket)
        {
            if (SteamNetworkingSockets.AcceptConnection(handle) == EResult.k_EResultOK) StartConnection(handle, null);
            else SteamNetworkingSockets.CloseConnection(handle, 0, "Session unavailable", false);
        }
        if (!connections.TryGetValue(handle, out var connection)) return;
        switch (change.m_info.m_eState)
        {
            case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected: connection.Connected.TrySetResult(); break;
            case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer:
            case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ProblemDetectedLocally:
                connection.Stop(); break;
        }
    }

    private void StartConnection(HSteamNetConnection handle, TcpClient? socket)
    {
        var connection = new Connection(handle, socket, stopping.Token);
        connections[handle] = connection;
        connection.Work = BridgeAsync(connection);
    }

    private async Task PollAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(5));
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                lock (nativeGate)
                {
                    SteamAPI.RunCallbacks();
                    foreach (var connection in connections.Values) Receive(connection);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception error)
        {
            Failure = error;
            stopping.Cancel();
        }
    }

    private static void Receive(Connection connection)
    {
        var messages = new IntPtr[16];
        var count = SteamNetworkingSockets.ReceiveMessagesOnConnection(connection.Handle, messages, messages.Length);
        if (count < 0) { connection.Stop(); return; }
        for (var index = 0; index < count; index++)
        {
            try
            {
                var message = SteamNetworkingMessage_t.FromIntPtr(messages[index]);
                if (message.m_cbSize is < 1 or > 49152) { connection.Stop(); continue; }
                var data = new byte[message.m_cbSize];
                Marshal.Copy(message.m_pData, data, 0, data.Length);
                if (!connection.Incoming.Writer.TryWrite(data)) connection.Stop();
            }
            finally { SteamNetworkingMessage_t.Release(messages[index]); }
        }
    }

    public override async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true;
        stopping.Cancel();
        localListener?.Stop();
        await Task.WhenAll(accepting, polling);
        await Task.WhenAll(connections.Values.Select(connection => connection.Work));
        lock (nativeGate)
        {
            if (listenSocket != HSteamListenSocket.Invalid) SteamNetworkingSockets.CloseListenSocket(listenSocket);
            callback.Dispose();
            SteamAPI.Shutdown();
        }
        stopping.Dispose();
        activeStatus = null;
        runtimeGate.Release();
    }
}
#endif
