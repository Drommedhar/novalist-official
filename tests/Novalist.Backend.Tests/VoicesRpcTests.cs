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
        public int Waits { get; private set; }
        public bool Finished { get; set; } = true;

        /// <summary>The order the engine was driven in, so a test can prove the
        /// caller waited rather than firing and forgetting.</summary>
        public List<string> Calls { get; } = [];

        public IReadOnlyList<SystemVoice> List() => Voices;

        public void Speak(string text, string? voiceId, double rate)
        {
            Spoken = (text, voiceId, rate);
            Calls.Add("speak");
        }

        public bool WaitUntilDone()
        {
            Waits++;
            Calls.Add("wait");
            return Finished;
        }

        public void Stop()
        {
            Stops++;
            Calls.Add("stop");
        }
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
    public async Task SpeakingWithNoEngineIsRefusedRatherThanSilentlyDoingNothing()
    {
        _engine.Available = false;

        // False is what tells the renderer to fall back to the browser.
        Assert.False(await _rpc.SpeakAsync("Es war eine dunkle Nacht."));
        Assert.Null(_engine.Spoken);
    }

    [Fact]
    public async Task AChosenVoiceIsTheOneUsed()
    {
        _engine.Voices.Add(new SystemVoice("id-hazel", "Hazel", "en-GB"));
        _engine.Voices.Add(new SystemVoice("id-katja", "Katja", "de-DE"));

        Assert.True(await _rpc.SpeakAsync("Hello", "id-hazel", 1.5));

        Assert.Equal("id-hazel", _engine.Spoken?.VoiceId);
        Assert.Equal(1.5, _engine.Spoken?.Rate);
        Assert.Equal("Hello", _engine.Spoken?.Text);
    }

    [Fact]
    public async Task WithNoChoiceNothingIsForcedOnTheEngine()
    {
        _engine.Voices.Add(new SystemVoice("id-hazel", "Hazel", "en-GB"));

        // The project language is English by default, and the fake's only voice
        // speaks it - so it is picked rather than left to the engine.
        await _rpc.SpeakAsync("Hello");

        Assert.Equal("id-hazel", _engine.Spoken?.VoiceId);
    }

    [Fact]
    public async Task AVoiceNothingSpeaksIsLeftToTheEngine()
    {
        _engine.Voices.Add(new SystemVoice("id-katja", "Katja", "de-DE"));
        // Nothing speaks the default project language, and guessing German for
        // an English manuscript would be worse than the engine's own default.
        await _rpc.SpeakAsync("Hello", "id-gone");

        Assert.Null(_engine.Spoken?.VoiceId);
    }

    [Fact]
    public void StoppingReachesTheEngine()
    {
        _rpc.Stop();

        Assert.Equal(1, _engine.Stops);
    }

    [Fact]
    public async Task TheAnswerWaitsForTheSentenceToBeSpoken()
    {
        _engine.Voices.Add(new SystemVoice("id-hazel", "Hazel", "en-GB"));

        await _rpc.SpeakAsync("Es war eine dunkle Nacht.");

        // Answering before the sentence is spoken makes the editor queue the
        // next one at once, so the reading races through the scene giving each
        // paragraph about a second - which is exactly what it did.
        Assert.Equal(["speak", "wait"], _engine.Calls);
        Assert.Equal(1, _engine.Waits);
    }

    [Fact]
    public async Task ASentenceThatWasStoppedReportsSo()
    {
        _engine.Voices.Add(new SystemVoice("id-hazel", "Hazel", "en-GB"));
        _engine.Finished = false;

        // False is what stops the editor moving on, so a stopped reading stays
        // stopped rather than carrying on into the next paragraph.
        Assert.False(await _rpc.SpeakAsync("Es war eine dunkle Nacht."));
    }
}
