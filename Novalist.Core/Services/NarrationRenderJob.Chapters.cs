using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Novalist.Core.Utilities;
using Novalist.Sdk.Models.Narration;

namespace Novalist.Core.Services;

public sealed partial class NarrationRenderJob
{
    private async Task<NarrationChapterAudio> RenderChapterAsync(
        NarrationRenderChapter chapter, string name, NarrationRenderContext input,
        NarrationRenderSettings settings, Action<double> spoke, CancellationToken cancellationToken)
    {
        var audio = new ChapterAudioAssembly();

        foreach (var scene in chapter.Scenes)
        {
            audio.StartScene(settings.SceneGapMs);

            for (var at = 0; at < scene.Segments.Count; at += settings.Window)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var window = scene.Segments.Skip(at).Take(settings.Window).ToArray();
                // Consecutive sentences one voice says in one breath, read in
                // one breath. A recording is listened to end to end, and a model
                // that starts each call afresh resets its pitch and pace at
                // every full stop otherwise.
                var joined = NarrationRender.Joined(
                    window,
                    settings.JoinCharacters,
                    input.Features.HasFlag(VoiceEngineFeatures.EmotionInferred));
                var covers = joined.ToDictionary(
                    j => j.Segment.Key, j => j.Covers, StringComparer.Ordinal);

                var request = NarrationRender.Build(
                    [.. joined.Select(j => j.Segment)], input, settings.Rate, _ => scene.Where);

                // Segments the cast could not place - no voice, or a voice this
                // machine does not have - never reach the engine. Counted in
                // lines rather than in calls, so the total the writer sees is
                // the whole chapter however the lines were grouped.
                var asked = request.Segments.Select(r => r.Key).ToHashSet(StringComparer.Ordinal);
                foreach (var join in joined.Where(j => !asked.Contains(j.Segment.Key)))
                {
                    audio.Missed(join.Covers, spoke);
                }
                if (request.Segments.Count == 0)
                    continue;

                var heard = 0;
                await foreach (var clip in _render(request, cancellationToken)
                    .WithCancellation(cancellationToken))
                {
                    heard++;
                    var stands = covers.GetValueOrDefault(clip.Key, 1);
                    audio.Append(clip, stands, input.Features, settings, spoke);
                }

                // Fewer clips came back than calls went out. Counted rather than
                // assumed: an engine that quietly drops a line is a failure only
                // ever noticed while listening.
                for (var i = heard; i < request.Segments.Count; i++)
                {
                    var stands = covers.GetValueOrDefault(request.Segments[i].Key, 1);
                    audio.Missed(stands, spoke);
                }
            }
        }

        return await audio.SaveAsync(_folder, name, chapter, cancellationToken);
    }

    /// <summary>
    /// Everything that would change the audio, as one string.
    ///
    /// The words, who says them, how, at what pace, and what the engine can be
    /// told - a cast change over unchanged prose is still a different reading.
    /// What is deliberately absent is anything positional: inserting a chapter
    /// must not invalidate the ones after it.
    /// </summary>
    internal static string Fingerprint(
        NarrationRenderChapter chapter,
        VoiceCastSheet sheet,
        VoiceEngineFeatures features,
        string language,
        NarrationRenderSettings settings)
    {
        var builder = new StringBuilder();
        builder.Append(language).Append(Unit)
            .Append(settings.Rate.ToString("F3", CultureInfo.InvariantCulture)).Append(Unit)
            .Append(settings.SegmentGapMs).Append(Unit)
            .Append(settings.SceneGapMs).Append(Unit)
            // The fade is audible at every join of the chapter, so changing
            // it changes the audio.
            .Append(settings.JoinFadeMs).Append(Unit)
            .Append(settings.JoinCharacters).Append(Unit)
            .Append((int)features).Append('\n');

        // By scene rather than over the flattened chapter, because which voice
        // a line resolves to now depends on where the line is.
        foreach (var scene in chapter.Scenes)
        {
            foreach (var segment in scene.Segments)
            {
                builder.Append(segment.Key).Append(Unit)
                    .Append(VoiceCast.Resolve(sheet, segment.SpeakerId, scene.Where) ?? "-")
                    .Append(Unit)
                    .Append(segment.Direction.Key).Append(Unit)
                    .Append(segment.Direction.ReferenceClip ?? "-").Append(Unit);
                // The line's own direction with the speaker's standing register
                // already added, which is what will actually be performed.
                // Hashing the line's own numbers instead meant a writer who made
                // a character warmer or more clipped and re-rendered was told
                // every chapter was unchanged, and heard the old delivery back.
                foreach (var (dimension, value) in EmotionDirector
                    .WithRegister(segment.Direction.Vector, sheet.RegisterFor(segment.SpeakerId))
                    .OrderBy(p => p.Key, StringComparer.Ordinal))
                {
                    builder.Append(dimension).Append('=')
                        .Append(value.ToString("F3", CultureInfo.InvariantCulture)).Append(',');
                }
                builder.Append(Unit).Append(segment.Text).Append('\n');
            }
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())))[..32];
    }

    /// <summary>
    /// A chapter's file name: its position, so a directory listing is in reading
    /// order, and part of its guid, so two chapters sharing a title do not write
    /// to the same file.
    /// </summary>
    internal static string FileNameFor(int index, NarrationRenderChapter chapter)
    {
        var id = new string([.. chapter.Guid.Where(char.IsLetterOrDigit).Take(8)]);
        return $"chapter-{index + 1:D3}-{(id.Length == 0 ? "x" : id)}.wav";
    }

    private sealed class ChapterAudioAssembly
    {
        private readonly MemoryStream samples = new();
        private WaveFormat? format;
        private int missing;
        private int gapMs;

        public void StartScene(int sceneGapMs)
        {
            if (samples.Length > 0) gapMs = sceneGapMs;
        }

        public void Missed(int count, Action<double> spoke)
        {
            missing += count;
            for (var i = 0; i < count; i++) spoke(0);
        }

        public void Append(NarrationClip clip, int stands, VoiceEngineFeatures features,
            NarrationRenderSettings settings, Action<double> spoke)
        {
            var read = clip.Error == null ? WaveAudio.Read(clip.Audio) : null;
            if (read == null || read.Samples.Length == 0)
            {
                Missed(stands, spoke);
                return;
            }

            format ??= read.Format;
            if (read.Format != format)
            {
                Missed(stands, spoke);
                return;
            }

            if (samples.Length > 0)
                samples.Write(WaveAudio.Silence(read.Format, gapMs));
            samples.Write(features.HasFlag(VoiceEngineFeatures.ContinuousContext)
                ? read.Samples
                : WaveAudio.Fade(read.Samples, read.Format, settings.JoinFadeMs));
            gapMs = settings.SegmentGapMs;
            for (var i = 0; i < stands; i++) spoke(read.DurationMs / stands);
        }

        public async Task<NarrationChapterAudio> SaveAsync(
            string folder, string name, NarrationRenderChapter chapter, CancellationToken cancellationToken)
        {
            var shape = format ?? new WaveFormat(24000, 1, 16);
            var bytes = samples.ToArray();
            await File.WriteAllBytesAsync(
                Path.Combine(folder, name), WaveAudio.Write(shape, bytes), cancellationToken);
            return new NarrationChapterAudio(
                chapter.Guid, chapter.Title, name, shape.DurationMs(bytes.LongLength), missing, false);
        }
    }

}
