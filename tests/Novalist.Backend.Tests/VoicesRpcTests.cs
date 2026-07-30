using Novalist.Backend;
using Novalist.Backend.Rpc;
using Novalist.Backend.Speech;
using Xunit;

namespace Novalist.Backend.Tests;

/// <summary>
/// The voices RPC, over a fake engine - the real one is COM and cannot be
/// unit-tested, which is exactly why the decisions live outside it.
/// </summary>
public sealed class VoicesRpcTests : IDisposable
{
    private sealed class FakeEngine : ISystemVoices
    {
        public bool Available { get; set; } = true;
        public List<SystemVoice> Voices { get; } = [];
        public (string Text, string? VoiceId, double Rate)? Spoken { get; private set; }
        public int Stops { get; private set; }

        public IReadOnlyList<SystemVoice> List() => Voices;
        public void Speak(string text, string? voiceId, double rate) => Spoken = (text, voiceId, rate);
        public void Stop() => Stops++;
    }

    private readonly string _root;
    private readonly Workspace _workspace;
    private readonly FakeEngine _engine = new();
    private readonly VoicesRpc _rpc;

    public VoicesRpcTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "nl-voice-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _workspace = new Workspace(Path.Combine(_root, "settings"));
        _rpc = new VoicesRpc(_workspace, _engine);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    [Fact]
    public void WithNoSystemEngineTheListIsEmpty()
    {
        // Empty is the signal to keep using the browser's own voices, which is
        // what every platform but Windows does.
        _engine.Available = false;
        _engine.Voices.Add(new SystemVoice("id", "Ignored", "en-GB"));

        Assert.Empty(_rpc.List());
    }

    [Fact]
    public void TheVoicesComeBackWithTheirLanguage()
    {
        _engine.Voices.Add(new SystemVoice("id-katja", "Katja", "de-DE"));

        var listed = Assert.Single(_rpc.List());
        Assert.Equal("id-katja", listed.Id);
        Assert.Equal("Katja", listed.Name);
        Assert.Equal("de-DE", listed.Language);
    }

    [Fact]
    public void SpeakingWithNoEngineIsRefusedRatherThanSilentlyDoingNothing()
    {
        _engine.Available = false;

        // False is what tells the renderer to fall back to the browser.
        Assert.False(_rpc.Speak("Es war eine dunkle Nacht."));
        Assert.Null(_engine.Spoken);
    }

    [Fact]
    public void AChosenVoiceIsTheOneUsed()
    {
        _engine.Voices.Add(new SystemVoice("id-hazel", "Hazel", "en-GB"));
        _engine.Voices.Add(new SystemVoice("id-katja", "Katja", "de-DE"));

        Assert.True(_rpc.Speak("Hello", "id-hazel", 1.5));

        Assert.Equal("id-hazel", _engine.Spoken?.VoiceId);
        Assert.Equal(1.5, _engine.Spoken?.Rate);
        Assert.Equal("Hello", _engine.Spoken?.Text);
    }

    [Fact]
    public void WithNoChoiceNothingIsForcedOnTheEngine()
    {
        _engine.Voices.Add(new SystemVoice("id-hazel", "Hazel", "en-GB"));

        // The project language is English by default, and the fake's only voice
        // speaks it - so it is picked rather than left to the engine.
        _rpc.Speak("Hello");

        Assert.Equal("id-hazel", _engine.Spoken?.VoiceId);
    }

    [Fact]
    public void AVoiceNothingSpeaksIsLeftToTheEngine()
    {
        _engine.Voices.Add(new SystemVoice("id-katja", "Katja", "de-DE"));
        // Nothing speaks the default project language, and guessing German for
        // an English manuscript would be worse than the engine's own default.
        _rpc.Speak("Hello", "id-gone");

        Assert.Null(_engine.Spoken?.VoiceId);
    }

    [Fact]
    public void StoppingReachesTheEngine()
    {
        _rpc.Stop();

        Assert.Equal(1, _engine.Stops);
    }
}
