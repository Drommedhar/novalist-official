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
    /// The brief for a character, for the writer to read and edit before
    /// anything is designed.
    ///
    /// Shown first on purpose. A design prompt assembled invisibly is one the
    /// writer cannot correct, and this one is assembled from fields they may not
    /// have thought of as describing a voice.
    /// </summary>
    [JsonRpcMethod("voiceEngines/brief")]
    public async Task<VoiceBriefDto?> BriefAsync(string characterId, bool consent = false)
    {
        // Nothing to build a brief from, and an exception across the wire would
        // reach the writer as a failed call rather than as an empty dialog.
        if (_workspace.Projects.ActiveBook == null)
            return null;

        var character = (await _entities.LoadCharactersAsync())
            .FirstOrDefault(c => string.Equals(c.Id, characterId, StringComparison.Ordinal));
        if (character == null)
            return null;

        var draft = VoiceBriefBuilder.Build(
            character,
            await SampleLinesAsync(characterId),
            SceneAnalysisLexicon.For(WritingLanguage()),
            consent);

        Log.Info(
            $"voiceEngines/brief refusal={draft.Refusal} len={draft.Description.Length} " +
            $"samples={draft.SampleLines.Count}.");

        return new VoiceBriefDto(
            characterId,
            EntityResolveIndex.Compose(character.Name, character.Surname),
            draft.Description,
            [.. draft.SampleLines],
            draft.Refusal.ToString());
    }

    /// <summary>
    /// Designs a voice for a character and stores it, then casts them in it.
    ///
    /// <paramref name="description"/> is what the writer approved, which may not
    /// be what the builder proposed - and it goes through the same emotion filter
    /// either way, because a rule the dialog can talk its way around is not a
    /// rule.
    /// </summary>
    [JsonRpcMethod("voiceEngines/design")]
    // aislop-ignore-next-line complexity/too-many-params -- Published JSON-RPC parameter names and ordering are part of the renderer protocol and must remain compatible.
    public async Task<VoiceDesignDto> DesignAsync(
        string engineId,
        string characterId,
        string description,
        bool consent = false,
        string? act = null,
        string? chapter = null,
        string? scene = null,
        int? seed = null)
    {
        // No project, nowhere to keep the audio - and no point asking an engine
        // to spend a minute designing something that cannot be stored.
        if (_workspace.Projects.ActiveBook == null)
            return VoiceDesignDto.Failed("NoProject");

        var engine = Find(engineId);
        if (engine == null)
            return VoiceDesignDto.Failed("NoEngine");
        if (!engine.Features.HasFlag(VoiceEngineFeatures.DesignFromDescription))
            return VoiceDesignDto.Failed("EngineCannotDesign");

        var brief = await BriefAsync(characterId, consent);
        if (brief == null)
            return VoiceDesignDto.Failed("NoCharacter");
        if (brief.Refusal != nameof(VoiceBriefRefusal.None))
            return VoiceDesignDto.Failed(brief.Refusal);

        var lexicon = SceneAnalysisLexicon.For(WritingLanguage());
        var wanted = VoiceBriefBuilder.Strip(description, lexicon);
        // A voice designed for one stretch of the book gets an id of its own, so
        // asking for an older Mira in Act Three does not overwrite how she
        // sounded in Act One. Designing a standing voice is the same call with
        // nowhere named, and mints the id it always did.
        var where = new VoiceScope(act, chapter, scene);
        var voiceId = VoiceCast.ScopedVoiceId(characterId, engine.EngineId, where);

        VoiceDesignResult designed;
        try
        {
            designed = await engine.DesignVoiceAsync(new VoiceBrief
            {
                VoiceId = voiceId,
                DisplayName = brief.Name,
                Description = wanted.Length > 0 ? wanted : brief.Description,
                SampleLines = brief.SampleLines,
                Language = WritingLanguage(),
                // Null asks the engine for a fresh draw, which is what "I did
                // not like that one, try again" has to mean. A number asks for
                // one particular voice back.
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
            Log.Warn($"voiceEngines/design failed type={ex.GetType().Name}.");
            return VoiceDesignDto.Failed(Reason(ex));
        }

        var stored = new DesignedVoice(
            designed.VoiceId,
            brief.Name,
            wanted,
            engine.EngineId,
            designed.AudioFormat,
            designed.SampleRate,
            DateTime.UtcNow.ToString("O"),
            designed.Seed,
            designed.ReferenceText);

        return await OfferAsync(stored, designed, characterId, where);
    }

    /// <summary>
    /// Holds a designed voice for the writer to hear before it becomes
    /// anybody's.
    ///
    /// Voice design is not reliable per attempt - the same description asked
    /// for twice gives two voices, and one of them may not be the voice that
    /// was asked for at all. Storing the first result and casting it made a
    /// miss into the character's voice until somebody noticed. So it is offered
    /// instead: rendered to the clip cache, played, and kept only if it is
    /// right.
    /// </summary>
    private async Task<VoiceDesignDto> OfferAsync(
        DesignedVoice stored,
        VoiceDesignResult designed,
        string? characterId,
        VoiceScope? where = null)
    {
        var clip = await _clips.WriteAsync(designed.ReferenceAudio, designed.AudioFormat);
        _candidate = new VoiceCandidate(stored, designed.ReferenceAudio, characterId, where);

        Log.Info(
            $"voiceEngines/design offered bytes={designed.ReferenceAudio.Length} " +
            $"rate={designed.SampleRate} narrator={characterId == null} " +
            $"scoped={where?.IsSomewhere == true} seeded={stored.Seed != null}.");
        return new VoiceDesignDto(stored.VoiceId, stored.Description, null, clip, stored.Seed);
    }

    /// <summary>
    /// Keeps the voice that was offered: stores the audio and casts whoever it
    /// was designed for.
    /// </summary>
    [JsonRpcMethod("voiceEngines/keepVoice")]
    public async Task<bool> KeepVoiceAsync()
    {
        if (_candidate is not { } candidate)
            return false;

        await _voices.SaveAsync(candidate.Stored, candidate.Audio);
        // Designing a voice for somebody and not casting them in it would leave
        // the writer one more step to discover - and a voice designed for one
        // stretch of the book must be cast over that stretch rather than become
        // how the character sounds everywhere, which is the opposite of what
        // was asked for.
        var scoped = candidate.Where is { IsSomewhere: true };
        if (candidate.Where is { IsSomewhere: true } where)
        {
            await _cast.SetScopeAsync(
                candidate.CharacterId, where, candidate.Stored.VoiceId);
        }
        else
        {
            await _cast.SetVoiceAsync(candidate.CharacterId, candidate.Stored.VoiceId);
        }
        _candidate = null;

        Log.Info($"voiceEngines/keepVoice ok scoped={scoped}.");
        return true;
    }

    /// <summary>
    /// Throws the offered voice away. Nothing was stored, so this only forgets
    /// - but it is a call rather than a timeout, because the writer closing the
    /// dialog is a decision.
    /// </summary>
    [JsonRpcMethod("voiceEngines/discardVoice")]
    public bool DiscardVoice()
    {
        var had = _candidate != null;
        _candidate = null;
        Log.Info($"voiceEngines/discardVoice had={had}.");
        return had;
    }

    /// <param name="CharacterId">Who it was designed for; null for the narrator.</param>
    private sealed record VoiceCandidate(
        DesignedVoice Stored, byte[] Audio, string? CharacterId, VoiceScope? Where = null);

    private VoiceCandidate? _candidate;

    /// <summary>
    /// What to put in front of the writer when a design fails.
    ///
    /// The engine's own words where it gave any - the Speech extension reports
    /// codes such as "sidecar-exited-while-designing" - and the exception type
    /// only when it did not, which is the case for a fault that never reached
    /// the engine at all.
    /// </summary>
    private static string Reason(Exception ex)
        => string.IsNullOrWhiteSpace(ex.Message) ? ex.GetType().Name : ex.Message;

    /// <summary>Every voice this book has been given.</summary>
    [JsonRpcMethod("voiceEngines/voices")]
    public async Task<DesignedVoiceDto[]> VoicesAsync()
        => [.. (await _voices.ListAsync()).Select(v => new DesignedVoiceDto(
            v.VoiceId, v.DisplayName, v.Description, v.EngineId, v.DesignedAt, v.Seed))];

    /// <summary>
    /// Forgets a designed voice, and un-casts anybody reading in it.
    ///
    /// Leaving the cast pointing at a voice that no longer exists would give the
    /// writer a reading that silently falls back to the narrator with nothing on
    /// screen saying why.
    /// </summary>
    [JsonRpcMethod("voiceEngines/forget")]
    public async Task<bool> ForgetAsync(string voiceId)
    {
        var voice = await _voices.GetAsync(voiceId);
        if (voice == null || !await _voices.DeleteAsync(voiceId))
            return false;

        var sheet = await _cast.ReadAsync();
        foreach (var (characterId, cast) in sheet.Voices.ToArray())
        {
            if (string.Equals(cast, voiceId, StringComparison.Ordinal))
                await _cast.SetVoiceAsync(characterId, null);
        }
        if (string.Equals(sheet.NarratorVoiceId, voiceId, StringComparison.Ordinal))
            await _cast.SetVoiceAsync(null, null);

        // And the stretches it was cast over. A scope left pointing at a voice
        // that no longer exists is worse than a stale standing cast: it wins
        // over the character's real voice, so those chapters fall silently back
        // to the narrator while the rest of the book is right.
        var stale = (await _cast.ReadAsync()).Overrides
            .Where(o => string.Equals(o.VoiceId, voiceId, StringComparison.Ordinal))
            .ToArray();
        foreach (var scope in stale)
        {
            await _cast.SetScopeAsync(
                string.IsNullOrEmpty(scope.CharacterId) ? null : scope.CharacterId,
                new VoiceScope(scope.Act, scope.Chapter, scope.Scene),
                null);
        }

        var engine = Find(voice.EngineId);
        if (engine != null)
        {
            try
            {
                await engine.ForgetVoiceAsync(voiceId);
            }
            catch (Exception ex)
            {
                // The project has already forgotten it; an engine that will not
                // is a diagnostic, not a failure the writer can act on.
                Log.Warn($"voiceEngines/forget engine refused type={ex.GetType().Name}.");
            }
        }

        Log.Info("voiceEngines/forget ok.");
        return true;
    }
}
