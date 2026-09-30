using System.Collections.Concurrent;
using Novalist.Sdk.Hooks;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

/// <summary>Speech conversion only. The editor owns insertion and undo.</summary>
public sealed class DictationRpc(Workspace workspace)
{
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _requests = new();
    public const int MaxAudioBytes = 8 * 1024 * 1024;
    public const int MaxTranscriptLength = 16000;

    [JsonRpcMethod("dictation/providers")]
    public DictationProviderDto[] Providers() => workspace.ExtensionsHost.DictationContributors
        .Select(p => new DictationProviderDto(p.DictationId, p.DictationName,
            p.IsDictationAvailable, p.AudioDestination, p.FormattingDestination)).ToArray();

    [JsonRpcMethod("dictation/transcribe")]
    public async Task<string> TranscribeAsync(string requestId, string providerId,
        string audioBase64, string mimeType, string language, CancellationToken cancellationToken)
    {
        ValidateLanguage(language);
        if (audioBase64.Length > (MaxAudioBytes + 2) / 3 * 4)
            throw new ArgumentException("Recording is too large.");
        var audio = Convert.FromBase64String(audioBase64);
        if (audio.Length == 0 || audio.Length > MaxAudioBytes)
            throw new ArgumentException("Recording is empty or too large.");
        if (mimeType is not ("audio/webm" or "audio/ogg" or "audio/wav" or "audio/mp4"))
            throw new ArgumentException("Unsupported recording format.");
        return await RunAsync(requestId, async ct =>
        {
            var text = await Provider(providerId).TranscribeAsync(audio, mimeType, language, ct);
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
        return RunAsync(requestId, ct => Provider(providerId).DetectDialogueAsync(transcript, language, precedingText, ct), cancellationToken);
    }

    [JsonRpcMethod("dictation/cancel")]
    public void Cancel(string requestId)
    {
        if (_requests.TryGetValue(requestId, out var request))
        {
            try { request.Cancel(); }
            catch (ObjectDisposedException) { }
        }
    }

    private IDictationContributor Provider(string id) => workspace.ExtensionsHost.DictationContributors
        .FirstOrDefault(p => p.DictationId == id && p.IsDictationAvailable)
        ?? throw new InvalidOperationException("Configure dictation in AI Assistant settings first.");

    private async Task<T> RunAsync<T>(string id, Func<CancellationToken, Task<T>> run, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(id, out _)) throw new ArgumentException("Invalid request id.");
        using var request = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        request.CancelAfter(TimeSpan.FromMinutes(3));
        if (!_requests.TryAdd(id, request)) throw new InvalidOperationException("Request already running.");
        try { return await run(request.Token); }
        finally { _requests.TryRemove(id, out _); }
    }

    private static void ValidateLanguage(string language)
    {
        if (language is not ("en" or "de")) throw new ArgumentException("Unsupported dictation language.");
    }
}

public sealed record DictationProviderDto(string Id, string Name, bool Available,
    string AudioDestination, string FormattingDestination);
