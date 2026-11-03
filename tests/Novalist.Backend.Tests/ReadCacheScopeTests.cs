using System.Text.Json;
using Nerdbank.Streams;
using Novalist.Backend.Rpc;
using Novalist.Backend.Tests.TestHelpers;
using Novalist.Core.Models;
using Novalist.Core.Services;
using StreamJsonRpc;
using Xunit;

namespace Novalist.Backend.Tests;

[Collection("BackendStatics")]
public sealed class ReadCacheScopeTests
{
    [Theory]
    [InlineData("scenes/read")]
    [InlineData("spell/dictionary")]
    [InlineData("scenes/getAnnotations")]
    [InlineData("entities/list")]
    [InlineData("entities/get")]
    [InlineData("entities/history")]
    [InlineData("entities/groups")]
    [InlineData("entities/customProps")]
    [InlineData("entities/getMatchSettings")]
    [InlineData("entities/getAiPolicy")]
    [InlineData("entities/getReaderPolicy")]
    [InlineData("entities/relationshipSuggestions")]
    [InlineData("entities/getStateOverrides")]
    [InlineData("entities/resolveState")]
    [InlineData("attachments/list")]
    [InlineData("arcs/get")]
    [InlineData("arcs/all")]
    [InlineData("context/analyze")]
    [InlineData("dashboard/get")]
    [InlineData("dashboard/overview")]
    [InlineData("dashboard/echoPhrases")]
    [InlineData("analytics/book")]
    [InlineData("analytics/castAbsence")]
    [InlineData("analytics/tension")]
    [InlineData("analytics/sceneFieldCurve")]
    public async Task AuditedReadRequestUsesCacheAcrossAwait(string method)
    {
        using var pair = new RpcPair();
        pair.Server.AddLocalRpcMethod(method, new Func<Task<bool>>(async () =>
        {
            await Task.Yield();
            return pair.Cache.Enabled;
        }));
        pair.Start();

        Assert.True(await pair.Client.InvokeAsync<bool>(method));
        Assert.Equal(1, pair.Cache.CachedEntries);
        Assert.Equal(1, pair.Cache.CachedExits);
        Assert.True(pair.Cache.EnteredUnderGate);
        Assert.False(Assert.Single(pair.AfterRequestScopes));
        Assert.True(Assert.Single(pair.AfterRequestFreshScopes));
    }

    [Theory]
    [InlineData("entities/update")]
    [InlineData("entities/setLocked")]
    [InlineData("entities/setCustomProp")]
    [InlineData("entities/setAiPolicy")]
    [InlineData("entities/setReaderPolicy")]
    [InlineData("entities/setMatchSettings")]
    [InlineData("entities/setStateOverrides")]
    [InlineData("entities/setOverride")]
    [InlineData("entities/restoreRevision")]
    [InlineData("scenes/write")]
    [InlineData("scenes/resolveConflict")]
    [InlineData("scenes/setAnnotations")]
    [InlineData("attachments/add")]
    [InlineData("arcs/save")]
    [InlineData("project/open")]
    [InlineData("project/reconcile")]
    [InlineData("project/switchBook")]
    [InlineData("entities/getFutureMutation")]
    [InlineData("system/ping")]
    public async Task MutationsAndUnauditedRequestsAlwaysUseFreshReads(string method)
    {
        using var pair = new RpcPair();
        pair.Server.AddLocalRpcMethod(method, new Func<Task<bool>>(async () =>
        {
            await Task.Yield();
            if (method != "system/ping") Assert.True(pair.Cache.ExplicitlyFresh);
            return pair.Cache.Enabled;
        }));
        pair.Start();

        Assert.False(await pair.Client.InvokeAsync<bool>(method));
        Assert.Equal(0, pair.Cache.CachedEntries);
    }

    [Theory]
    [InlineData("success")]
    [InlineData("failure")]
    [InlineData("cancel")]
    public async Task ReadScopeEndsBeforeCleanupAndFollowingMutation(string outcome)
    {
        using var pair = new RpcPair();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        pair.Server.AddLocalRpcMethod("entities/get", new Func<CancellationToken, Task<bool>>(async cancellationToken =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
            if (outcome == "failure") throw new IOException("Read failed");
            return pair.Cache.Enabled;
        }));
        pair.Server.AddLocalRpcMethod("scenes/write", new Func<bool>(() => pair.Cache.Enabled));
        pair.Server.AddLocalRpcMethod("system/ping", new Func<bool>(() => pair.Cache.Enabled));
        pair.Start();
        using var cancellation = new CancellationTokenSource();
        var read = pair.Client.InvokeWithCancellationAsync<bool>("entities/get", [], cancellation.Token);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(await pair.Client.InvokeAsync<bool>("system/ping"));
            var mutation = pair.Client.InvokeAsync<bool>("scenes/write");
            if (outcome == "cancel") cancellation.Cancel();
            else release.TrySetResult();
            if (outcome == "cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read);
            else if (outcome == "failure") await Assert.ThrowsAsync<RemoteInvocationException>(() => read);
            else Assert.True(await read);
            Assert.False(await mutation.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal(1, pair.Cache.CachedExits);
            Assert.All(pair.AfterRequestScopes, enabled => Assert.False(enabled));
            Assert.All(pair.AfterRequestFreshScopes, enabled => Assert.True(enabled));
        }
        finally { release.TrySetResult(); }
    }

    [Fact]
    public async Task SceneOpenedCallbacksUseFreshReadsInsideCachedSceneRequest()
    {
        using var temp = new TempDir();
        var files = new CacheFiles();
        using var workspace = new Workspace(temp.Combine("settings"), fileService: files);
        await workspace.Projects.CreateProjectAsync(temp.Path, "Novel", "Book");
        var chapter = await workspace.Projects.CreateChapterAsync("Chapter");
        var scene = await workspace.Projects.CreateSceneAsync(chapter.Guid, "Scene");
        _ = workspace.ExtensionsHost;
        bool? callbackUsesCache = null;
        workspace.HostServices!.SceneOpened += _ => callbackUsesCache = files.Cache.Enabled;

        using (files.UseCachedReads())
        {
            await new ScenesRpc(workspace).ReadAsync(chapter.Guid, scene.Id);
            Assert.True(files.Cache.Enabled);
        }

        Assert.False(callbackUsesCache);
    }

    [Fact]
    public async Task DashboardBaselineWritesUseFreshReadsInsideCachedDashboardRequest()
    {
        using var temp = new TempDir();
        var files = new CacheFiles();
        using var workspace = new Workspace(temp.Combine("settings"), fileService: files);
        await workspace.Projects.CreateProjectAsync(temp.Path, "Novel", "Book");
        workspace.Projects.ProjectSettings.WordCountGoals.DailyBaselineDate = "2000-01-01";
        files.WriteScopes.Clear();

        using (files.UseCachedReads())
        {
            await new DashboardRpc(workspace).GetAsync(1);
            Assert.True(files.Cache.Enabled);
        }

        Assert.NotEmpty(files.WriteScopes);
        Assert.All(files.WriteScopes, enabled => Assert.False(enabled));
    }

    [Fact]
    public async Task DashboardHistoryReadsKeepCachedScopeWhenBaselineIsCurrent()
    {
        using var temp = new TempDir();
        var files = new CacheFiles();
        using var workspace = new Workspace(temp.Combine("settings"), fileService: files);
        await workspace.Projects.CreateProjectAsync(temp.Path, "Novel", "Book");
        workspace.Projects.ProjectSettings.WordCountGoals.DailyBaselineDate = DateTime.Today.ToString("yyyy-MM-dd");
        var history = Path.Combine(workspace.Projects.ProjectRoot!, ".novalist", "word-history.jsonl");
        Directory.CreateDirectory(Path.GetDirectoryName(history)!);
        await File.WriteAllTextAsync(history, "");
        files.ReadScopes.Clear();
        files.WriteScopes.Clear();

        using (files.UseCachedReads())
            await new DashboardRpc(workspace).GetAsync(1, includeEchoPhrases: false);

        Assert.True(Assert.Single(files.ReadScopes));
        Assert.Empty(files.WriteScopes);
    }

    [Fact]
    public async Task BackendHostScopesRealEntityReadsAndKeepsUpdatesFresh()
    {
        using var temp = new TempDir();
        var files = new CacheFiles();
        using var workspace = new Workspace(temp.Combine("settings"), fileService: files);
        await workspace.Projects.CreateProjectAsync(temp.Path, "Novel", "Book");
        await new EntityService(workspace.Projects).SaveCharacterAsync(new CharacterData { Id = "selected", Name = "Before" });
        var (serverStream, clientStream) = FullDuplexStream.CreatePair();
        using var host = new BackendHost(workspace);
        host.Attach(serverStream, serverStream);
        using var client = new JsonRpc(new HeaderDelimitedMessageHandler(clientStream, clientStream, new SystemTextJsonFormatter()));
        client.StartListening();
        files.ReadScopes.Clear();

        var selected = await client.InvokeAsync<JsonElement>("entities/get", "character", "selected");

        Assert.Equal("Before", selected.GetProperty("name").GetString());
        Assert.True(Assert.Single(files.ReadScopes));
        files.ReadScopes.Clear();

        var updated = await client.InvokeAsync<JsonElement>("entities/update", "character", "selected",
            new Dictionary<string, string> { ["name"] = "After" });

        Assert.Equal("After", updated.GetProperty("name").GetString());
        Assert.NotEmpty(files.ReadScopes);
        Assert.All(files.ReadScopes, enabled => Assert.False(enabled));
    }

    private sealed class RpcPair : IDisposable
    {
        private readonly SemaphoreSlim _gate = new(1, 1);
        public CacheProbe Cache { get; }
        public SerialDispatchJsonRpc Server { get; }
        public JsonRpc Client { get; }
        public List<bool> AfterRequestScopes { get; } = [];
        public List<bool> AfterRequestFreshScopes { get; } = [];

        public RpcPair()
        {
            Cache = new CacheProbe(() => _gate.CurrentCount == 0);
            var (server, client) = FullDuplexStream.CreatePair();
            Server = new SerialDispatchJsonRpc(
                new HeaderDelimitedMessageHandler(server, server, new SystemTextJsonFormatter()),
                _gate, () =>
                {
                    AfterRequestScopes.Add(Cache.Enabled);
                    AfterRequestFreshScopes.Add(Cache.ExplicitlyFresh);
                    return Task.CompletedTask;
                }, fileReadCache: Cache);
            Client = new JsonRpc(new HeaderDelimitedMessageHandler(client, client, new SystemTextJsonFormatter()));
        }

        public void Start()
        {
            Server.StartListening();
            Client.StartListening();
        }

        public void Dispose()
        {
            Client.Dispose();
            Server.Dispose();
            _gate.Dispose();
        }
    }

    private sealed class CacheProbe(Func<bool>? gateHeld = null) : IFileReadCache
    {
        private readonly AsyncLocal<bool?> _enabled = new();
        public bool Enabled => _enabled.Value is true;
        public bool ExplicitlyFresh => _enabled.Value is false;
        public int CachedEntries { get; private set; }
        public int CachedExits { get; private set; }
        public bool EnteredUnderGate { get; private set; }

        public IDisposable UseCachedReads()
        {
            var previous = _enabled.Value;
            _enabled.Value = true;
            CachedEntries++;
            EnteredUnderGate = gateHeld?.Invoke() ?? false;
            return new Scope(() => { _enabled.Value = previous; CachedExits++; });
        }

        public IDisposable UseFreshReads()
        {
            var previous = _enabled.Value;
            _enabled.Value = false;
            return new Scope(() => _enabled.Value = previous);
        }

        private sealed class Scope(Action restore) : IDisposable
        {
            public void Dispose() => restore();
        }
    }

    private sealed class CacheFiles : FileService, IFileService, IFileReadCache
    {
        public CacheProbe Cache { get; } = new();
        public List<bool> WriteScopes { get; } = [];
        public List<bool> ReadScopes { get; } = [];
        public IDisposable UseCachedReads() => Cache.UseCachedReads();
        public IDisposable UseFreshReads() => Cache.UseFreshReads();

        Task IFileService.WriteTextAsync(string path, string content)
        {
            WriteScopes.Add(Cache.Enabled);
            return WriteTextAsync(path, content);
        }

        Task<string> IFileService.ReadTextAsync(string path)
        {
            ReadScopes.Add(Cache.Enabled);
            return ReadTextAsync(path);
        }
    }
}
