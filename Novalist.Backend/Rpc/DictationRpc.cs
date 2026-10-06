using System.Collections.Concurrent;
using Novalist.Sdk.Hooks;
using Novalist.Backend.Dictation;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

/// <summary>Speech conversion only. The editor owns insertion and undo.</summary>
// aislop-ignore-next-line complexity/function-too-long -- Primary-constructor class declaration; the scanner counts independent methods as one constructor body.
public sealed class DictationRpc(Workspace workspace, ISystemDictation? system = null)
{
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _requests = new();
    public const int MaxAudioBytes = 8 * 1024 * 1024;
    public const int MaxTranscriptLength = 16000;

    [JsonRpcMethod("dictation/providers")]
    public async Task<DictationProviderDto[]> Providers(CancellationToken cancellationToken = default)
    {
        var providers = workspace.ExtensionsHost.DictationContributors
            .Select(p => new DictationProviderDto(p.DictationId, p.DictationName,
                p.IsDictationAvailable, p.AudioDestination, p.FormattingDestination,
                SupportsWarmup: p is IDictationWarmupContributor or IDictationOptionsContributor,
                SupportsVocabulary: p is IDictationOptionsContributor)).ToList();
        if (system != null)
        {
            var status = await system.StatusAsync(cancellationToken);
            providers.Add(new(SystemDictation.ProviderId, status.Engine == "windows" ? "Windows voice typing" : "System dictation",
                status.Available, status.Engine == "windows" ? "Microsoft online speech" : "Apple SpeechAnalyzer",
                "", AutomaticDialogue: false, UsesSystemPanel: status.UsesSystemPanel,
                Languages: status.Languages));
        }
        return providers.ToArray();
    }

    [JsonRpcMethod("dictation/systemStatus")]
    public Task<SystemDictationStatus> SystemStatusAsync(CancellationToken cancellationToken)
        => System().StatusAsync(cancellationToken);

    [JsonRpcMethod("dictation/prepareSystem")]
    public Task PrepareSystemAsync(string requestId, string language, CancellationToken cancellationToken)
    {
        ValidateLanguage(language);
        return RunAsync(requestId, async ct => { await System().PrepareAsync(language, ct); return true; }, cancellationToken,
            TimeSpan.FromMinutes(30));
    }

    [JsonRpcMethod("dictation/openSystemPanel")]
    public void OpenSystemPanel() => System().OpenSystemPanel();

    private ISystemDictation System() => system ?? throw new InvalidOperationException("System dictation is unavailable.");

    [JsonRpcMethod("dictation/warmUp")]
    public Task WarmUpAsync(string requestId, string providerId, bool automaticDialogue = true, CancellationToken cancellationToken = default)
        => RunAsync(requestId, async ct =>
        {
            var provider = Provider(providerId);
            if (provider is IDictationOptionsContributor options) await options.WarmUpAsync(automaticDialogue, ct);
            else if (automaticDialogue && provider is IDictationWarmupContributor warmup) await warmup.WarmUpAsync(ct);
            return true;
        }, cancellationToken, TimeSpan.FromMinutes(10));

    [JsonRpcMethod("dictation/transcribe")]
    // aislop-ignore-next-line complexity/too-many-params -- Published JSON-RPC parameter names and ordering are part of the renderer protocol and must remain compatible.
    public async Task<string> TranscribeAsync(string requestId, string providerId,
        string audioBase64, string mimeType, string language, string[]? vocabulary = null, CancellationToken cancellationToken = default)
    {
        ValidateLanguage(language);
        vocabulary ??= [];
        if (vocabulary.Length > 128 || vocabulary.Any(term => string.IsNullOrWhiteSpace(term)
                || term.Length > 80 || term.Any(char.IsControl)) || vocabulary.Sum(term => term.Length + 2) > 2000)
            throw new ArgumentException("Vocabulary may contain at most 128 terms of 80 characters, within 2000 characters total.");
        if (audioBase64.Length > (MaxAudioBytes + 2) / 3 * 4)
            throw new ArgumentException("Recording is too large.");
        var audio = Convert.FromBase64String(audioBase64);
        if (audio.Length == 0 || audio.Length > MaxAudioBytes)
            throw new ArgumentException("Recording is empty or too large.");
        if (mimeType is not ("audio/webm" or "audio/ogg" or "audio/wav" or "audio/mp4"))
            throw new ArgumentException("Unsupported recording format.");
        return await RunAsync(requestId, async ct =>
        {
            if (providerId == SystemDictation.ProviderId && mimeType != "audio/wav")
                throw new ArgumentException("System dictation requires WAV audio.");
            string text;
            if (providerId == SystemDictation.ProviderId)
            {
                if (vocabulary.Length > 0) throw new ArgumentException("This provider does not support vocabulary hints.");
                text = await System().TranscribeAsync(audio, language, ct);
            }
            else
            {
                var provider = Provider(providerId);
                if (provider is IDictationOptionsContributor options)
                    text = await options.TranscribeAsync(audio, mimeType, language, vocabulary, ct);
                else
                {
                    if (vocabulary.Length > 0) throw new ArgumentException("This provider does not support vocabulary hints.");
                    text = await provider.TranscribeAsync(audio, mimeType, language, ct);
                }
            }
            if (text.Length > MaxTranscriptLength) throw new InvalidOperationException("Transcript is too long.");
            return text;
        }, cancellationToken);
    }

    [JsonRpcMethod("dictation/format")]
    public Task<IReadOnlyList<DictationSegment>> FormatAsync(string requestId, string providerId,
        string transcript, string language, string precedingText, CancellationToken cancellationToken)
    {
        ValidateLanguage(language);
        if (string.IsNullOrWhiteSpace(transcript) || transcript.Length > MaxTranscriptLength)
            throw new ArgumentException("Transcript is empty or too long.");
        if (precedingText.Length > 2000) throw new ArgumentException("Dictation context is too long.");
        return RunAsync(requestId, ct => providerId == SystemDictation.ProviderId
            ? Task.FromResult<IReadOnlyList<DictationSegment>>([new(transcript, "narration")])
            : Provider(providerId).DetectDialogueAsync(transcript, language, precedingText, ct), cancellationToken);
    }

    [JsonRpcMethod("dictation/cancel")]
    public void Cancel(string requestId)
    {
        // Serialize cancellation with removal so a completing request cannot
        // dispose its source while Cancel is using it.
        lock (_requests)
        {
            if (_requests.TryGetValue(requestId, out var request)) request.Cancel();
        }
    }

    private IDictationContributor Provider(string id) => workspace.ExtensionsHost.DictationContributors
        .FirstOrDefault(p => p.DictationId == id && p.IsDictationAvailable)
        ?? throw new InvalidOperationException("The selected dictation provider is unavailable.");

    private async Task<T> RunAsync<T>(string id, Func<CancellationToken, Task<T>> run, CancellationToken cancellationToken,
        TimeSpan? timeout = null)
    {
        if (!Guid.TryParse(id, out _)) throw new ArgumentException("Invalid request id.");
        using var request = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        request.CancelAfter(timeout ?? TimeSpan.FromMinutes(3));
        if (!_requests.TryAdd(id, request)) throw new InvalidOperationException("Request already running.");
        try { return await run(request.Token); }
        finally { lock (_requests) _requests.TryRemove(id, out _); }
    }

    private static void ValidateLanguage(string language)
    {
        if (language is not ("en" or "de")) throw new ArgumentException("Unsupported dictation language.");
    }
}

public sealed record DictationProviderDto(string Id, string Name, bool Available,
    string AudioDestination, string FormattingDestination, bool AutomaticDialogue = true,
    bool UsesSystemPanel = false, SystemDictationLanguage[]? Languages = null, bool SupportsWarmup = false,
    bool SupportsVocabulary = false);
