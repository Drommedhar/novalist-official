using Novalist.Backend.Extensions;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Sdk.Hooks;
using Novalist.Sdk.Models.Narration;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

public sealed partial class VoiceEngineRpc
{
    /// <summary>
    /// Designs the narrator's voice from the book rather than from a Codex
    /// entry.
    ///
    /// The narrator is not a character and has no entry to read. What decides
    /// how a book should be narrated is what kind of book it is and who is
    /// telling it - the declared person and tense, and the logline - all of
    /// which the writer already wrote down.
    /// </summary>
    [JsonRpcMethod("narration/designNarrator")]
    public async Task<VoiceDesignDto> DesignNarratorAsync(
        string engineId, string description, int? seed = null)
    {
        var book = _workspace.Projects.ActiveBook;
        if (book == null)
            return VoiceDesignDto.Failed("NoProject");

        var engine = Find(engineId);
        if (engine == null)
            return VoiceDesignDto.Failed("NoEngine");
        if (!engine.Features.HasFlag(VoiceEngineFeatures.DesignFromDescription))
            return VoiceDesignDto.Failed("EngineCannotDesign");

        var lexicon = SceneAnalysisLexicon.For(WritingLanguage());
        var typed = VoiceBriefBuilder.Strip(description, lexicon);
        var wanted = typed.Length > 0
            ? typed
            : NarrationRender.NarratorBrief(book, lexicon);
        var voiceId = $"narrator-{engine.EngineId}";

        VoiceDesignResult designed;
        try
        {
            designed = await engine.DesignVoiceAsync(new VoiceBrief
            {
                VoiceId = voiceId,
                DisplayName = book.Name,
                Description = wanted,
                Language = WritingLanguage(),
                Seed = seed is >= 0 ? seed : null
            });
        }
        catch (Exception ex)
        {
            // The type goes to the log, because an exception's message can name
            // a path and the diagnostic log is a thing writers send us. The
            // message goes to the screen in front of the person whose machine
            // it is, because "InvalidOperationException" is not a reason and
            // left them with nothing to act on and nothing to tell us.
            Log.Warn($"narration/designNarrator failed type={ex.GetType().Name}.");
            return VoiceDesignDto.Failed(Reason(ex));
        }

        var stored = new DesignedVoice(
            designed.VoiceId, book.Name, wanted, engine.EngineId,
            designed.AudioFormat, designed.SampleRate, DateTime.UtcNow.ToString("O"),
            designed.Seed, designed.ReferenceText);

        return await OfferAsync(stored, designed, characterId: null);
    }

    /// <summary>What the narrator's brief would say, for the dialog to show
    /// before anything is designed.</summary>
    [JsonRpcMethod("narration/narratorBrief")]
    public string NarratorBrief()
        => NarrationRender.NarratorBrief(
            _workspace.Projects.ActiveBook, SceneAnalysisLexicon.For(WritingLanguage()));

    /// <summary>
    /// Speaks one line in a designed voice at several points on the emotional
    /// range, and returns the clips.
    ///
    /// Three emotions rather than one, because one neutral sample says nothing
    /// about whether the casting works. The claim the whole two-stage design
    /// rests on is that a designed identity survives being performed - and this
    /// is where a writer hears whether it does.
    /// </summary>
    [JsonRpcMethod("voiceEngines/audition")]
    public async Task<AuditionClipDto[]> AuditionAsync(
        string voiceId, string text, string[]? emotions = null)
    {
        var voice = await _voices.GetAsync(voiceId);
        var engine = voice == null ? null : Find(voice.EngineId);
        var audio = await _voices.ReadAudioAsync(voiceId);
        if (voice == null || engine == null || audio == null)
            return [];

        var keys = emotions is { Length: > 0 } ? emotions : DefaultAuditionEmotions();
        if (engine.Features.HasFlag(VoiceEngineFeatures.EmotionInferred))
            keys = [EmotionDirector.NeutralKey];
        var referenceTexts = await _voices.ReadReferenceTextsForAsync([voiceId]);
        var request = new NarrationRequest
        {
            Language = WritingLanguage(),
            Voices = new Dictionary<string, byte[]>(StringComparer.Ordinal) { [voiceId] = audio },
            VoiceReferenceTexts = referenceTexts,
            Segments =
            [
                .. keys.Select(key => new Sdk.Models.Narration.NarrationSegment
                {
                    Key = key,
                    Text = text,
                    VoiceId = voiceId,
                    IsDialogue = true,
                    Direction = Direct(key)
                })
            ]
        };

        var clips = new List<AuditionClipDto>();
        try
        {
            await foreach (var clip in engine.RenderAsync(request))
            {
                clips.Add(new AuditionClipDto(
                    clip.Key,
                    Convert.ToBase64String(clip.Audio),
                    clip.AudioFormat,
                    clip.SampleRate,
                    clip.DurationMs,
                    clip.Error));
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"voiceEngines/audition failed type={ex.GetType().Name}.");
        }

        Log.Info($"voiceEngines/audition clips={clips.Count} asked={keys.Length}.");
        return [.. clips];
    }

    /// <summary>
    /// The three emotions an audition uses when the caller names none: the
    /// neutral one and the two furthest from it the language has. A sample that
    /// only proves a voice can be calm proves nothing about the book.
    /// </summary>
    private string[] DefaultAuditionEmotions()
    {
        var keys = SceneAnalysisLexicon.For(WritingLanguage())?.EmotionKeys ?? [];
        return
        [
            EmotionDirector.NeutralKey,
            .. new[] { "angry", "sorrowful" }.Where(keys.Contains)
        ];
    }

    /// <summary>One emotion key as every kind of direction an engine might take:
    /// the name, the numbers, and a sentence. The engine uses whichever its
    /// feature flags say it understands.</summary>
    private static Sdk.Models.Narration.VoiceDirection Direct(string key)
        => new()
        {
            Key = key,
            Vector = EmotionDirector.Vector(key, null),
            Instruction = $"Read this {key}.",
            Source = nameof(DirectionSource.Writer)
        };

    /// <summary>A few of this character's own lines, for the brief. How somebody
    /// talks describes their voice better than any adjective, and the writer
    /// already wrote it.</summary>
    private async Task<IReadOnlyList<string>> SampleLinesAsync(string characterId)
    {
        var characters = await _entities.LoadCharactersAsync();
        var index = await new DialogueIndexService(_workspace.Projects)
            .BuildAsync(characters, characterId, WritingLanguage());

        return
        [
            .. index.Groups
                .SelectMany(g => g.Scenes)
                .SelectMany(s => s.Lines)
                .Select(l => l.Text)
                .Take(VoiceBriefBuilder.MaxSampleLines)
        ];
    }
}
