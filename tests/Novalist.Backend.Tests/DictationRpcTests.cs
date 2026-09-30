using Novalist.Backend.Rpc;
using Novalist.Sdk.Hooks;
using Xunit;

namespace Novalist.Backend.Tests;

[Collection("BackendStatics")]
public sealed class DictationRpcTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "nl-dictation-" + Guid.NewGuid().ToString("N"));
    private readonly Workspace _workspace;
    private readonly DictationRpc _rpc;
    private readonly Engine _engine = new();
    public DictationRpcTests()
    {
        _workspace = new Workspace(_root);
        _workspace.ExtensionsHost.DictationContributors.Add(_engine);
        _rpc = new DictationRpc(_workspace);
    }
    public void Dispose()
    {
        _workspace.Dispose();
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
    [Fact]
    public async Task DisabledProviderNeverReceivesAudio()
    {
        _engine.IsDictationAvailable = false;
        Assert.False(Assert.Single(await _rpc.Providers()).Available);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Transcribe(Guid.NewGuid().ToString()));
        Assert.Equal(0, _engine.Calls);
    }
    [Fact]
    public async Task WarmupIsOptionalAndDoesNotSendAudio()
    {
        Assert.False(Assert.Single(await _rpc.Providers()).SupportsWarmup);
        await _rpc.WarmUpAsync(Guid.NewGuid().ToString(), "test", default);
        Assert.Equal(0, _engine.Calls);
        var warm = new WarmEngine();
        _workspace.ExtensionsHost.DictationContributors.Clear();
        _workspace.ExtensionsHost.DictationContributors.Add(warm);
        Assert.True(Assert.Single(await _rpc.Providers()).SupportsWarmup);
        await _rpc.WarmUpAsync(Guid.NewGuid().ToString(), "test", default);
        Assert.Equal(1, warm.Warmups);
        Assert.Equal(0, warm.Calls);
        warm.Wait = true;
        var id = Guid.NewGuid().ToString();
        var pending = _rpc.WarmUpAsync(id, "test", default);
        _rpc.Cancel(id);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        warm.IsDictationAvailable = false;
        await Assert.ThrowsAsync<InvalidOperationException>(() => _rpc.WarmUpAsync(Guid.NewGuid().ToString(), "test", default));
        Assert.Equal(2, warm.Warmups);
    }
    [Theory]
    [InlineData("dictation/transcribe")]
    [InlineData("dictation/format")]
    [InlineData("dictation/cancel")]
    [InlineData("dictation/warmUp")]
    public void SpeechDoesNotBlockSceneSavesOrItsOwnCancellation(string method)
        => Assert.True(SerialDispatchJsonRpc.IsReentrant(method));
    [Fact]
    public async Task CancellingOneRequestDoesNotCancelAnother()
    {
        var firstId = Guid.NewGuid().ToString();
        var secondId = Guid.NewGuid().ToString();
        var first = Transcribe(firstId);
        var second = Transcribe(secondId);
        _rpc.Cancel(firstId);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        Assert.False(second.IsCompleted);
        _rpc.Cancel(secondId);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second);
    }
    [Fact]
    public async Task RefusesUnsupportedLanguageAndOversizedAudioBeforeCallingProvider()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _rpc.TranscribeAsync(Guid.NewGuid().ToString(), "test", "AQ==", "audio/wav", "xx", default));
        await Assert.ThrowsAsync<ArgumentException>(() => _rpc.TranscribeAsync(Guid.NewGuid().ToString(), "test", new string('A', 12 * 1024 * 1024), "audio/wav", "en", default));
        Assert.Equal(0, _engine.Calls);
    }
    private Task<string> Transcribe(string id) => _rpc.TranscribeAsync(id, "test", "AQ==", "audio/wav", "de", default);
    private sealed class WarmEngine : Engine, IDictationWarmupContributor
    {
        public int Warmups;
        public bool Wait;
        public async Task WarmUpAsync(CancellationToken cancellationToken = default)
        {
            Warmups++;
            if (Wait) await Task.Delay(Timeout.Infinite, cancellationToken);
        }
    }
    private class Engine : IDictationContributor
    {
        public int Calls;
        public string DictationId => "test";
        public string DictationName => "Test";
        public bool IsDictationAvailable { get; set; } = true;
        public string AudioDestination => "local";
        public string FormattingDestination => "local";
        public async Task<string> TranscribeAsync(byte[] audio, string mimeType, string language, CancellationToken cancellationToken = default)
        { Calls++; await Task.Delay(Timeout.Infinite, cancellationToken); return "Hallo."; }
        public Task<IReadOnlyList<DictationSegment>> DetectDialogueAsync(string transcript, string language, string precedingText, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<DictationSegment>>([new(transcript, "narration")]);
    }
}
