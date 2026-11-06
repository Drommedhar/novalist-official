using Novalist.Backend.Rpc;
using Novalist.Backend.Speech;
using Xunit;

namespace Novalist.Backend.Tests;

public sealed class AsyncVoicesRpcTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "nl-async-voices-" + Guid.NewGuid().ToString("N"));
    private readonly Workspace _workspace;
    public AsyncVoicesRpcTests() => _workspace = new Workspace(_root);
    public void Dispose() { _workspace.Dispose(); Directory.Delete(_root, true); }

    [Fact]
    public async Task MobileVoicesWaitForInitializationAndActualSpeechCompletion()
    {
        var engine = new Engine();
        var rpc = new VoicesRpc(_workspace, asyncVoices: engine);
        var listing = rpc.ListAsync();
        Assert.False(listing.IsCompleted);
        engine.Voices.SetResult([new("offline-en", "English", "en-US")]);
        Assert.Equal("offline-en", Assert.Single(await listing).Id);
        var speaking = rpc.SpeakAsync("Hello", "offline-en", 1.2);
        Assert.Equal(("Hello", "offline-en", 1.2), engine.Spoken);
        Assert.False(speaking.IsCompleted);
        rpc.Stop();
        Assert.False(await speaking);
        Assert.True(engine.Stopped);
    }

    [Fact]
    public async Task MobileWithNoVoicesDoesNotPretendToSpeak()
    {
        var engine = new Engine();
        engine.Voices.SetResult([]);
        var rpc = new VoicesRpc(_workspace, asyncVoices: engine);
        Assert.Empty(await rpc.ListAsync());
        Assert.False(await rpc.SpeakAsync("Hello"));
        Assert.Null(engine.Spoken);
    }

    [Fact]
    public async Task StopWhileVoicesInitializePreventsLateSpeech()
    {
        var engine = new Engine();
        var rpc = new VoicesRpc(_workspace, asyncVoices: engine);
        var speaking = rpc.SpeakAsync("Hello");
        rpc.Stop();
        engine.Voices.SetResult([new("offline-en", "English", "en-US")]);
        Assert.False(await speaking);
        Assert.Null(engine.Spoken);
    }

    [Fact]
    public async Task AsyncEndpointPreservesTheDesktopVoiceList()
    {
        var rpc = new VoicesRpc(_workspace, new EmptyDesktopEngine());
        Assert.Empty(await rpc.ListAsync());
    }

    private sealed class EmptyDesktopEngine : ISystemVoices
    {
        public bool Available => false;
        public IReadOnlyList<SystemVoice> List() => throw new NotSupportedException();
        public void Speak(string text, string? voiceId, double rate) => throw new NotSupportedException();
        public bool WaitUntilDone() => throw new NotSupportedException();
        public void Stop() => throw new NotSupportedException();
    }

    private sealed class Engine : IAsyncSystemVoices
    {
        public TaskCompletionSource<IReadOnlyList<SystemVoice>> Voices { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _speech = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public (string, string?, double)? Spoken { get; private set; }
        public bool Stopped { get; private set; }
        public Task<IReadOnlyList<SystemVoice>> ListAsync() => Voices.Task;
        public Task<bool> SpeakAsync(string text, string? voiceId, double rate)
        {
            Spoken = (text, voiceId, rate);
            return _speech.Task;
        }
        public void Stop() { Stopped = true; _speech.TrySetResult(false); }
    }
}
