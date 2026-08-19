using System.Security.Cryptography;
using System.Text;
// The engine's own segment, not this project's. Both are called
// NarrationSegment and they are different things: one is a piece of the book,
// the other is one instruction to a speech model - and it is the instruction
// that decides what comes out.
using SdkSegment = Novalist.Sdk.Models.Narration.NarrationSegment;

namespace Novalist.Core.Services;

/// <summary>
/// What decides how one line comes out, written down as a key.
///
/// The reading used to render every line from scratch every time Play was
/// pressed, and the cache was emptied on Stop - so listening to a paragraph
/// twice cost twice, and a writer correcting one line paid for the whole scene
/// again. Speech is seconds per line on a good graphics card; a chapter is
/// minutes of waiting for audio that had already been made once.
///
/// Everything that changes the sound goes in and nothing else does. Change the
/// words, the voice, the direction, the speed or the engine and the key changes,
/// so the line is made again. Change where the writer is scrolled, or the
/// speaker's colour, or the file it lives in, and it does not.
///
/// The voice goes in as a hash of its <em>audio</em> rather than as its id. A
/// redesigned voice keeps the id it had - that is what makes it the same
/// character - so an id alone would have gone on serving every line in the voice
/// the writer had just replaced.
/// </summary>
public static class NarrationRecipe
{
    /// <summary>
    /// The key for one line as it will actually be performed.
    /// </summary>
    /// <param name="segment">The line, cast and directed, as the engine will
    /// receive it.</param>
    /// <param name="engineId">Which engine will speak it. Two engines given the
    /// same instructions do not produce the same sound.</param>
    /// <param name="language">The language the book is read in.</param>
    /// <param name="rate">The reading pace.</param>
    /// <param name="voice">The reference audio for this line's voice, or null
    /// where the engine speaks without one.</param>
    public static string For(
        SdkSegment segment,
        string engineId,
        string language,
        double rate,
        byte[]? voice)
    {
        var said = new StringBuilder();
        // A separator no field can contain, so two different sets of fields can
        // never run together into the same string.
        void Add(string? value) => said.Append(value ?? string.Empty).Append('\u0000');

        Add(engineId);
        Add(language);
        Add(rate.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
        Add(segment.VoiceId);
        Add(Digest(voice));
        Add(segment.Text);
        Add(segment.IsDialogue ? "d" : "n");
        Add(segment.Direction.Key);
        Add(segment.Direction.Instruction);
        // Ordered, because a dictionary is not: the same delivery must not hash
        // two ways depending on which slider was moved first.
        foreach (var (name, weight) in segment.Direction.Vector.OrderBy(
                     p => p.Key, StringComparer.Ordinal))
        {
            Add(name);
            Add(weight.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
        }
        // The clip a line was told to sound like is part of how it sounds.
        Add(Digest(segment.Direction.ReferenceAudio));

        return Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(said.ToString())))[..32].ToLowerInvariant();
    }

    private static string Digest(byte[]? audio)
        => audio is { Length: > 0 }
            ? Convert.ToHexString(SHA256.HashData(audio), 0, 8)
            : string.Empty;
}
