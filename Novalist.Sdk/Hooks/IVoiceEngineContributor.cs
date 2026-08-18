using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Novalist.Sdk.Models.Narration;

namespace Novalist.Sdk.Hooks;

/// <summary>
/// Lets an extension supply the voices a book is read aloud in, and the speech
/// that reads it.
///
/// This sits beside <see cref="IArticleGeneratorContributor"/> and
/// <see cref="IGrammarCheckContributor"/> for the same reason those exist: the
/// core app assembles the reading - who says what, how it should be said, and in
/// whose voice - entirely offline, and then hands it to whoever can speak it.
/// Novalist itself never loads a model.
///
/// Two stages, and keeping them apart is the whole design. A voice is
/// <b>designed once</b> from a description of the character and stored; the
/// emotion is chosen <b>per line, at render time</b>, and applied to that fixed
/// identity. A character who is furious in chapter three and grieving in chapter
/// twenty is one voice and two performances - not two voices.
///
/// <b>The seam has no network affordance, by contract.</b> Nothing here takes an
/// endpoint, a key or a base URL. Novalist's read-aloud promises the writer that
/// listening to their book sends nothing anywhere, and an engine that reaches the
/// network breaks that promise on the app's behalf.
/// </summary>
public interface IVoiceEngineContributor
{
    /// <summary>Stable id, reverse-domain (e.g. "com.example.tts.local"). The
    /// writer's chosen engine is remembered by this.</summary>
    string EngineId { get; }

    /// <summary>Display name, shown in settings and in the cast rail.</summary>
    string EngineName { get; }

    /// <summary>What this engine can be asked for. Consumers branch on these
    /// rather than on the engine's identity, so an engine that cannot be
    /// directed is offered no direction controls instead of being sent
    /// directions it will ignore.</summary>
    VoiceEngineFeatures Features { get; }

    /// <summary>Where the engine is right now: ready, still preparing, or unable
    /// to run on this machine and why.</summary>
    Task<VoiceEngineStatus> GetStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the engine ready to speak - downloading weights, building an
    /// environment, loading a model. Cheap and re-entrant once ready.
    ///
    /// Separate from rendering because it is the slow, once-per-machine step,
    /// and the writer is owed honest numbers before it starts rather than a
    /// spinner after they press Play.
    /// </summary>
    Task PrepareAsync(
        IProgress<VoiceEnginePrepare>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stage one: designs a voice from a description of the character and
    /// returns the reference audio that <b>is</b> that voice from now on.
    ///
    /// The result is stored as audio, deliberately. Voice design is not
    /// deterministic - the same description and the same seed produce a similar
    /// but measurably different voice each run - so re-deriving the voice at
    /// playback would hand the writer a slightly different actor every session,
    /// and a different one again in any rendered file.
    /// </summary>
    /// <exception cref="System.NotSupportedException">
    /// The engine does not advertise <see cref="VoiceEngineFeatures.DesignFromDescription"/>.
    /// </exception>
    Task<VoiceDesignResult> DesignVoiceAsync(
        VoiceBrief brief, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stage two: speaks a whole run of the book, yielding clips in input order
    /// as they are ready.
    ///
    /// The whole run rather than one line at a time, because an engine that can
    /// hold identity and prosody across a chapter can only do so if it is given
    /// the chapter. An engine without <see cref="VoiceEngineFeatures.ContinuousContext"/>
    /// is free to treat each segment separately; the host joins the clips either
    /// way.
    /// </summary>
    IAsyncEnumerable<NarrationClip> RenderAsync(
        NarrationRequest request, CancellationToken cancellationToken = default);

    /// <summary>Drops a designed voice the writer deleted, so the engine stops
    /// holding audio for a character that no longer has one.</summary>
    Task ForgetVoiceAsync(string voiceId, CancellationToken cancellationToken = default);
}
