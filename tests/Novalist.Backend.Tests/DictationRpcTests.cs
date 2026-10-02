using System.Text.Json;
using Nerdbank.Streams;
using Novalist.Backend.Rpc;
using Novalist.Sdk.Hooks;
using StreamJsonRpc;
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
        await _rpc.WarmUpAsync(Guid.NewGuid().ToString(), "test");
        Assert.Equal(0, _engine.Calls);
        var warm = new WarmEngine();
        _workspace.ExtensionsHost.DictationContributors.Clear();
        _workspace.ExtensionsHost.DictationContributors.Add(warm);
        Assert.True(Assert.Single(await _rpc.Providers()).SupportsWarmup);
        await _rpc.WarmUpAsync(Guid.NewGuid().ToString(), "test");
        Assert.Equal(1, warm.Warmups);
        Assert.Equal(0, warm.Calls);
        warm.Wait = true;
        var id = Guid.NewGuid().ToString();
        var pending = _rpc.WarmUpAsync(id, "test");
        _rpc.Cancel(id);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        warm.IsDictationAvailable = false;
        await Assert.ThrowsAsync<InvalidOperationException>(() => _rpc.WarmUpAsync(Guid.NewGuid().ToString(), "test"));
        Assert.Equal(2, warm.Warmups);
    }
    [Fact]
    public async Task RecognitionOptionsReachOnlyAnOptedInProviderAndPlainWarmupIsSpeechOnly()
    {
        var engine = new OptionsEngine();
        _workspace.ExtensionsHost.DictationContributors.Clear();
        _workspace.ExtensionsHost.DictationContributors.Add(engine);
        var provider = Assert.Single(await _rpc.Providers());
        Assert.True(provider.SupportsVocabulary);
        Assert.True(provider.SupportsWarmup);
        await _rpc.WarmUpAsync(Guid.NewGuid().ToString(), "test", false);
        Assert.False(engine.AutomaticDialogue);
        var terms = new[] { "Aeloria", "Großwald" };
        Assert.Equal("Aeloria.", await _rpc.TranscribeAsync(Guid.NewGuid().ToString(), "test", "AQ==", "audio/wav", "de", terms));
        Assert.Equal(terms, engine.Vocabulary);
        await _rpc.WarmUpAsync(Guid.NewGuid().ToString(), "test");
        Assert.True(engine.AutomaticDialogue);
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RecordingOptionsReachTheProviderThroughTheRealRpcTransport(bool automaticDialogue)
    {
        var engine = new OptionsEngine();
        _workspace.ExtensionsHost.DictationContributors.Clear();
        _workspace.ExtensionsHost.DictationContributors.Add(engine);
        var streams = FullDuplexStream.CreatePair();
        var serverFormatter = new SystemTextJsonFormatter();
        serverFormatter.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        using var server = new SerialDispatchJsonRpc(new HeaderDelimitedMessageHandler(streams.Item1, streams.Item1, serverFormatter));
        server.AddLocalRpcTarget(_rpc, new JsonRpcTargetOptions { DisposeOnDisconnect = false });
        server.StartListening();
        var clientFormatter = new SystemTextJsonFormatter();
        clientFormatter.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        using var client = new JsonRpc(new HeaderDelimitedMessageHandler(streams.Item2, streams.Item2, clientFormatter));
        client.StartListening();

        await client.InvokeWithParameterObjectAsync("dictation/warmUp", new { requestId = Guid.NewGuid().ToString(), providerId = "test", automaticDialogue });
        Assert.Equal(automaticDialogue, engine.AutomaticDialogue);
        var vocabulary = new[] { "Aeloria", "Großwald" };
        var transcript = await client.InvokeWithParameterObjectAsync<string>("dictation/transcribe", new {
            requestId = Guid.NewGuid().ToString(), providerId = "test", audioBase64 = "AQ==", mimeType = "audio/wav", language = "de", vocabulary });
        Assert.Equal("Aeloria.", transcript);
        Assert.Equal(vocabulary, engine.Vocabulary);

        // Calls from a host without the options must retain their defaults.
        await client.InvokeWithParameterObjectAsync("dictation/warmUp", new { requestId = Guid.NewGuid().ToString(), providerId = "test" });
        Assert.True(engine.AutomaticDialogue);
        Assert.Equal("Aeloria.", await client.InvokeWithParameterObjectAsync<string>("dictation/transcribe", new {
            requestId = Guid.NewGuid().ToString(), providerId = "test", audioBase64 = "AQ==", mimeType = "audio/wav", language = "en" }));
        Assert.Empty(engine.Vocabulary);
    }
    [Fact]
    public async Task PlainModeDoesNotWarmALegacyDialogueModelOrSendItVocabulary()
    {
        var engine = new WarmEngine();
        _workspace.ExtensionsHost.DictationContributors.Clear();
        _workspace.ExtensionsHost.DictationContributors.Add(engine);
        await _rpc.WarmUpAsync(Guid.NewGuid().ToString(), "test", false);
        Assert.Equal(0, engine.Warmups);
        Assert.False(Assert.Single(await _rpc.Providers()).SupportsVocabulary);
        await Assert.ThrowsAsync<ArgumentException>(() => _rpc.TranscribeAsync(Guid.NewGuid().ToString(), "test", "AQ==", "audio/wav", "en", ["Aeloria"]));
        Assert.Equal(0, engine.Calls);
    }
    [Fact]
    public async Task LegacyRecognitionStillReturnsItsTranscriptWithoutOptions()
    {
        _engine.Reply = "Legacy speech.";
        Assert.Equal("Legacy speech.", await Transcribe(Guid.NewGuid().ToString()));
        Assert.Equal(1, _engine.Calls);
    }
    [Fact]
    public async Task InvalidVocabularyIsRefusedBeforeAudioReachesAnEngine()
    {
        foreach (var terms in new[] { Enumerable.Repeat("Name", 129).ToArray(), new[] { "" }, new[] { new string('a', 81) },
            new[] { "A\nB" }, new[] { (string)null! }, Enumerable.Repeat(new string('a', 79), 25).ToArray() })
            await Assert.ThrowsAsync<ArgumentException>(() => _rpc.TranscribeAsync(Guid.NewGuid().ToString(), "test", "AQ==", "audio/wav", "en", terms));
        Assert.Equal(0, _engine.Calls);
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
    private sealed class OptionsEngine : Engine, IDictationOptionsContributor
    {
        public bool AutomaticDialogue;
        public IReadOnlyList<string> Vocabulary = [];
        public Task WarmUpAsync(bool automaticDialogue, CancellationToken cancellationToken = default)
        { AutomaticDialogue = automaticDialogue; return Task.CompletedTask; }
        public Task<string> TranscribeAsync(byte[] audio, string mimeType, string language, IReadOnlyList<string> vocabulary,
            CancellationToken cancellationToken = default)
        { Vocabulary = vocabulary; return Task.FromResult("Aeloria."); }
    }
    private class Engine : IDictationContributor
    {
        public int Calls;
        public string? Reply;
        public string DictationId => "test";
        public string DictationName => "Test";
        public bool IsDictationAvailable { get; set; } = true;
        public string AudioDestination => "local";
        public string FormattingDestination => "local";
        public async Task<string> TranscribeAsync(byte[] audio, string mimeType, string language, CancellationToken cancellationToken = default)
        { Calls++; if (Reply != null) return Reply; await Task.Delay(Timeout.Infinite, cancellationToken); return "Hallo."; }
        public Task<IReadOnlyList<DictationSegment>> DetectDialogueAsync(string transcript, string language, string precedingText, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<DictationSegment>>([new(transcript, "narration")]);
    }
}
