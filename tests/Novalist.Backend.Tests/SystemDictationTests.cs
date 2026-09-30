using System.Text.Json;
using Novalist.Backend.Dictation;
using Novalist.Backend.Rpc;
using Xunit;

namespace Novalist.Backend.Tests;

[Collection("BackendStatics")]
public sealed class SystemDictationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "nl-system-dictation-" + Guid.NewGuid().ToString("N"));
    private readonly Workspace _workspace;
    public SystemDictationTests() => _workspace = new Workspace(_root);
    public void Dispose() { _workspace.Dispose(); Directory.Delete(_root, true); }

    [Fact]
    public async Task WindowsOffersItsOnlinePanelWithoutLoadingAModelOrMicrophone()
    {
        var bridge = new Bridge();
        var opens = 0;
        var system = new SystemDictation("windows", bridge, () => opens++);
        var rpc = new DictationRpc(_workspace, system);
        var provider = Assert.Single(await rpc.Providers());
        Assert.Equal(SystemDictation.ProviderId, provider.Id);
        Assert.True(provider.Available);
        Assert.True(provider.UsesSystemPanel);
        Assert.False(provider.AutomaticDialogue);
        Assert.True((await rpc.SystemStatusAsync(default)).Online);
        Assert.Equal(["en", "de"], provider.Languages!.Select(l => l.Language));
        rpc.OpenSystemPanel();
        Assert.Equal(1, opens);
        Assert.Empty(bridge.Requests);
        await Assert.ThrowsAsync<InvalidOperationException>(() => system.TranscribeAsync([1], "en", default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => system.PrepareAsync("de", default));
    }

    [Fact]
    public async Task AppleAvailabilityKeepsSupportedAndInstalledLanguagesSeparate()
    {
        var bridge = new Bridge { Reply = """
            {"engine":"apple","available":true,"online":false,"usesSystemPanel":false,
             "languages":[{"language":"en","supported":true,"installed":true},
                          {"language":"de","supported":true,"installed":false}]}
            """ };
        var system = new SystemDictation("apple", bridge, () => throw new Exception());
        var rpc = new DictationRpc(_workspace, system);
        var provider = Assert.Single(await rpc.Providers());
        Assert.True(provider.Available);
        Assert.False(provider.UsesSystemPanel);
        Assert.False(provider.AutomaticDialogue);
        Assert.True(provider.Languages![0].Installed);
        Assert.False(provider.Languages[1].Installed);
        Assert.Equal("status", bridge.Requests[0].GetProperty("operation").GetString());
        Assert.Throws<InvalidOperationException>(rpc.OpenSystemPanel);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("de")]
    public async Task ApplePreparesViaTheSystemAndTranscribesWithoutFormatting(string language)
    {
        var bridge = new Bridge { Reply = "true" };
        var rpc = new DictationRpc(_workspace, new SystemDictation("apple", bridge, () => { }));
        await rpc.PrepareSystemAsync(Guid.NewGuid().ToString(), language, default);
        Assert.Equal("prepare", bridge.Requests[0].GetProperty("operation").GetString());
        Assert.Equal(language, bridge.Requests[0].GetProperty("language").GetString());
        bridge.Reply = JsonSerializer.Serialize("Hallo, sagte sie.");
        var text = await rpc.TranscribeAsync(Guid.NewGuid().ToString(), SystemDictation.ProviderId, "AQI=", "audio/wav", language, default);
        Assert.Equal("Hallo, sagte sie.", text);
        var sent = bridge.Requests[1];
        Assert.Equal("transcribe", sent.GetProperty("operation").GetString());
        Assert.Equal("AQI=", sent.GetProperty("audio").GetString());
        Assert.Equal(language, sent.GetProperty("language").GetString());
        var formatted = Assert.Single(await rpc.FormatAsync(Guid.NewGuid().ToString(), SystemDictation.ProviderId,
            text, language, "", default));
        Assert.Equal("narration", formatted.Kind);
        Assert.Equal(text, formatted.Text);
        Assert.Equal(2, bridge.Requests.Count);
    }

    [Fact]
    public async Task UnsupportedSystemsDoNotAttemptToLoadAppleFrameworks()
    {
        var bridge = new Bridge();
        var system = new SystemDictation("unsupported", bridge, () => throw new Exception());
        var status = await system.StatusAsync(default);
        Assert.False(status.Available);
        Assert.Empty(status.Languages);
        Assert.Empty(bridge.Requests);
        Assert.Throws<InvalidOperationException>(system.OpenSystemPanel);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task MissingNativeBridgeIsUnavailableInsteadOfBreakingOtherProviders(int kind)
    {
        var bridge = new Bridge { Error = kind switch {
            0 => new DllNotFoundException(), 1 => new EntryPointNotFoundException(), _ => new BadImageFormatException() } };
        var system = new SystemDictation("apple", bridge, () => { });
        Assert.False((await system.StatusAsync(default)).Available);
    }

    [Fact]
    public async Task CancellationReachesLanguageInstallation()
    {
        var bridge = new Bridge { Wait = true };
        var rpc = new DictationRpc(_workspace, new SystemDictation("apple", bridge, () => { }));
        var id = Guid.NewGuid().ToString();
        var pending = rpc.PrepareSystemAsync(id, "de", default);
        rpc.Cancel(id);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.True(bridge.Token.IsCancellationRequested);
        Assert.True(SerialDispatchJsonRpc.IsReentrant("dictation/prepareSystem"));
        Assert.True(SerialDispatchJsonRpc.IsReentrant("dictation/systemStatus"));
    }

    [Fact]
    public async Task InvalidInputNeverReachesTheNativeBridge()
    {
        var bridge = new Bridge();
        var system = new SystemDictation("apple", bridge, () => { });
        var rpc = new DictationRpc(_workspace, system);
        await Assert.ThrowsAsync<ArgumentException>(() => system.PrepareAsync("fr", default));
        await Assert.ThrowsAsync<ArgumentException>(() => system.TranscribeAsync([1], "fr", default));
        await Assert.ThrowsAsync<ArgumentException>(() => rpc.PrepareSystemAsync("invalid", "en", default));
        await Assert.ThrowsAsync<ArgumentException>(() => rpc.TranscribeAsync(Guid.NewGuid().ToString(),
            SystemDictation.ProviderId, "AQ==", "audio/webm", "en", default));
        await Assert.ThrowsAsync<ArgumentException>(() => rpc.TranscribeAsync(Guid.NewGuid().ToString(),
            SystemDictation.ProviderId, "", "audio/wav", "en", default));
        await Assert.ThrowsAsync<ArgumentException>(() => rpc.TranscribeAsync(Guid.NewGuid().ToString(),
            SystemDictation.ProviderId, "AQ==", "invalid", "en", default));
        await Assert.ThrowsAsync<ArgumentException>(() => rpc.FormatAsync(Guid.NewGuid().ToString(),
            SystemDictation.ProviderId, "Hello", "en", new string('x', 2001), default));
        await Assert.ThrowsAsync<ArgumentException>(() => rpc.FormatAsync(Guid.NewGuid().ToString(),
            SystemDictation.ProviderId, " ", "en", "", default));
        await Assert.ThrowsAsync<ArgumentException>(() => rpc.FormatAsync(Guid.NewGuid().ToString(),
            SystemDictation.ProviderId, new string('x', DictationRpc.MaxTranscriptLength + 1), "en", "", default));
        Assert.Empty(bridge.Requests);
    }

    private sealed class Bridge : IAppleSpeechBridge
    {
        public string Reply = "null";
        public Exception? Error;
        public bool Wait;
        public CancellationToken Token;
        public List<JsonElement> Requests = [];
        public async Task<string> RequestAsync(object request, CancellationToken token)
        {
            Token = token;
            Requests.Add(JsonSerializer.SerializeToElement(request));
            if (Error != null) throw Error;
            if (Wait) await Task.Delay(Timeout.Infinite, token);
            return Reply;
        }
    }
}
