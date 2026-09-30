using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Novalist.Sdk.Hooks;

/// <summary>
/// Speech input and dialogue detection supplied by an extension. The host owns
/// microphone permission, capture, quotation style and insertion. No
/// manuscript context is sent: only the recording and its transcript.
/// </summary>
public interface IDictationContributor
{
    string DictationId { get; }
    string DictationName { get; }
    bool IsDictationAvailable { get; }
    /// <summary>User-visible engine/model labels; never include credentials.</summary>
    string AudioDestination { get; }
    string FormattingDestination { get; }
    Task<string> TranscribeAsync(byte[] audio, string mimeType, string language,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DictationSegment>> DetectDialogueAsync(string transcript, string language, string precedingText,
        CancellationToken cancellationToken = default);
}

/// <summary>Optional eager model loading when recording starts. No audio or downloads.</summary>
public interface IDictationWarmupContributor
{
    Task WarmUpAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Text in spoken order. Kind is narration, dialogue, or attribution (a speech
/// tag attached to dialogue). NewParagraph separates speakers or paragraphs;
/// false allows dialogue to resume after an attribution on the same line.
/// Text has no surrounding dialogue quotes. Preserve every spoken word, in
/// order; punctuation and capitalization may be corrected, never paraphrased.
/// </summary>
public sealed record DictationSegment(string Text, string Kind, bool NewParagraph = false);
