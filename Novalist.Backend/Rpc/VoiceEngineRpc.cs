using Novalist.Backend.Extensions;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Sdk.Hooks;
using Novalist.Sdk.Models.Narration;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

/// <summary>
/// Backs the designed half of narration: which speech engines are installed,
/// getting one ready, and designing a character a voice of their own.
///
/// Novalist loads no model itself. Everything here goes through
/// <see cref="IVoiceEngineContributor"/>, which an extension supplies - the same
/// arrangement the Wiki's article generator and the grammar checker already use,
/// and the reason the core app carries no AI dependency.
///
/// The brief a voice is designed from is built here, from the Codex entry and
/// the character's own lines, and it describes the <b>instrument</b> only. The
/// emotion belongs to the per-line direction and is applied at render time to a
/// fixed identity, so a character can be furious in one chapter and grieving in
/// the next without being two voices.
/// </summary>
public sealed partial class VoiceEngineRpc
{
    private readonly Workspace _workspace;
    private readonly EntityService _entities;
    private readonly VoiceStore _voices;
    private readonly VoiceCast _cast;
    private readonly NarrationClipCache _clips;

    /// <summary>Cancels whatever render is in flight. Replaced per render, so a
    /// second Play cancels the first rather than racing it.</summary>
    private CancellationTokenSource? _rendering;

    public VoiceEngineRpc(Workspace workspace, NarrationClipCache? clips = null)
    {
        _workspace = workspace;
        _entities = new EntityService(workspace.Projects);
        _voices = new VoiceStore(workspace.Projects, workspace.FileService);
        _cast = new VoiceCast(workspace.Projects, workspace.FileService);
        _clips = clips ?? new NarrationClipCache(workspace.SettingsDirectory);
    }

    /// <summary>
    /// The speech engines installed, with what each can do and whether it is
    /// ready. Empty when none is installed, which is the signal to keep offering
    /// the system voices rather than an empty picker.
    /// </summary>
    [JsonRpcMethod("voiceEngines/list")]
    public async Task<VoiceEngineDto[]> ListAsync()
    {
        var engines = await StatusesAsync();
        // Stamped after the fact, because the statuses were read a moment before
        // these were started and would otherwise report the state this call
        // just changed - which left the cast rail saying "not ready" with
        // nothing on it moving, and no reason for anything to ask again.
        var started = StartWhatIsAlreadyInstalled(engines);
        return
        [
            .. engines.Select(e => started.Contains(e.Status.EngineId)
                ? e.Status with { IsPreparing = true }
                : e.Status)
        ];
    }

    /// <summary>
    /// Starts any engine that has everything it needs and is only waiting to be
    /// asked.
    ///
    /// An engine's model is loaded into a process that dies with the app, so
    /// "prepared" never survived a restart - and the button that fixed that sits
    /// on a rail the writer has no reason to look at once the download is done.
    /// The result was an application that had a speech engine installed,
    /// reported it as not ready every morning, and read the book in the
    /// operating system's voice until somebody found the button again.
    ///
    /// Only what is already downloaded. An engine with gigabytes still to fetch
    /// is a decision the writer makes, on a metered connection they may be
    /// paying for, and starting that unasked would be indefensible - which is
    /// exactly what <see cref="VoiceEngineStatus.DownloadBytes"/> distinguishes.
    ///
    /// Once per engine per session. A model that fails to load fails the same
    /// way every time, and retrying it on every refresh would spend a process
    /// start per poll to learn the same thing.
    /// </summary>
    /// <returns>The engines this call started, so their statuses can say so.</returns>
    private HashSet<string> StartWhatIsAlreadyInstalled(IReadOnlyList<EngineStatus> engines)
    {
        var wanted = new List<IVoiceEngineContributor>();
        lock (_gate)
        {
            foreach (var (engine, status) in engines)
            {
                if (status.IsReady || status.IsPreparing || status.DownloadBytes is > 0)
                    continue;
                if (!_started.Add(status.EngineId))
                    continue;
                wanted.Add(engine);
            }
        }

        foreach (var engine in wanted)
        {
            Log.Info($"voiceEngines auto-start id={engine.EngineId}.");
            // Not awaited. Loading a model is tens of seconds and this is a
            // status call - a list that blocked on it would freeze the cast rail
            // for the whole load, which is the thing being fixed rather than a
            // cheaper version of it. Started outside the lock, because an
            // engine's own first moments are not this object's to hold.
            var starting = StartAsync(engine);
            lock (_gate)
            {
                _starting[engine.EngineId] = starting;
            }
        }
        return [.. wanted.Select(e => e.EngineId)];
    }

    /// <summary>Whether this engine has a start of ours still running.</summary>
    private bool Starting(string engineId)
    {
        lock (_gate)
        {
            return _starting.TryGetValue(engineId, out var task) && !task.IsCompleted;
        }
    }

    private static async Task StartAsync(IVoiceEngineContributor engine)
    {
        try
        {
            await engine.PrepareAsync();
        }
        catch (Exception ex)
        {
            // Nobody asked for this, so nobody is waiting on a reason. The
            // engine's own status carries it for the next list.
            Log.Warn($"voiceEngines auto-start failed type={ex.GetType().Name}.");
        }
    }

    /// <summary>Engines already started without being asked.</summary>
    private readonly HashSet<string> _started = new(StringComparer.Ordinal);

    /// <summary>Starts in flight, by engine id, so a reading that arrives during
    /// one waits for it rather than falling back to the operating system's
    /// voice - and so a status can say that one is under way.</summary>
    private readonly Dictionary<string, Task> _starting = new(StringComparer.Ordinal);

    /// <summary>
    /// Guards the two above.
    ///
    /// Preparing an engine, designing a voice and hearing a line are all exempt
    /// from the backend's one-at-a-time gate - they are model loads, and queuing
    /// the application behind one is what that exemption exists to prevent. So
    /// two of these calls genuinely do run at once, and both of them touch this.
    /// </summary>
    private readonly object _gate = new();

    /// <summary>An engine and what it says about itself, kept together so a
    /// caller acting on a status does not have to look the engine up again by
    /// its id.</summary>
    private sealed record EngineStatus(IVoiceEngineContributor Engine, VoiceEngineDto Status);

    private async Task<List<EngineStatus>> StatusesAsync()
    {
        var engines = new List<EngineStatus>();
        foreach (var engine in _workspace.ExtensionsHost.VoiceEngines)
        {
            // An engine that throws while being asked how it is is reported as
            // not ready rather than taking the view down with it.
            VoiceEngineStatus status;
            try
            {
                status = await engine.GetStatusAsync();
            }
            catch (Exception ex)
            {
                status = new VoiceEngineStatus { Error = ex.GetType().Name };
            }

            engines.Add(new EngineStatus(engine, new VoiceEngineDto(
                engine.EngineId,
                engine.EngineName,
                (int)engine.Features,
                status.IsReady,
                // Or one we started ourselves. An engine loading a model because
                // the app opened is preparing in every sense the interface cares
                // about, and an engine that does not say so about itself would
                // otherwise leave the cast rail reporting "not ready" until the
                // writer happened to reopen the view.
                status.IsPreparing || Starting(engine.EngineId),
                status.Error,
                status.Detail,
                status.DownloadBytes)));
        }

        Log.Info(
            $"voiceEngines/list count={engines.Count} " +
            $"ready={engines.Count(e => e.Status.IsReady)}.");
        return engines;
    }

    /// <summary>
    /// Gets an engine ready - the download, the environment, the first model
    /// load. Returns its status afterwards, so the caller learns what happened
    /// rather than only that the call returned.
    /// </summary>
    [JsonRpcMethod("voiceEngines/prepare")]
    public async Task<VoiceEngineDto?> PrepareAsync(string engineId)
    {
        var engine = Find(engineId);
        if (engine == null)
            return null;

        string? failure = null;
        try
        {
            // The engine reports its own progress to the writer through the
            // host's busy dialog - it is the only party that knows whether it is
            // downloading, unpacking or loading. Nothing useful can be returned
            // from here until it finishes, which is exactly why this call is not
            // allowed to hold the request queue.
            await engine.PrepareAsync();
        }
        catch (Exception ex)
        {
            // Only the type is logged: an engine's own words can quote a path,
            // and the diagnostic log must never carry one.
            Log.Warn($"voiceEngines/prepare failed type={ex.GetType().Name}.");
            failure = ex.GetType().Name;
        }

        // Its own status first, because that is where an engine puts the reason
        // in words the writer can act on - reporting the exception type instead
        // left "InvalidOperationException" on screen in place of "install
        // Python". But an engine that throws and then says nothing about itself
        // must not leave the writer with no reason at all, so the type stands in
        // where there is nothing better.
        // Statuses rather than the list: this engine has just been prepared by
        // hand, and an auto-start fired off the back of reading its result would
        // be a second load of the model it either just loaded or just failed to.
        lock (_gate)
        {
            _started.Add(engineId);
        }
        var status = (await StatusesAsync())
            .Select(e => e.Status)
            .FirstOrDefault(e => e.EngineId == engineId);
        if (status == null || failure == null || !string.IsNullOrWhiteSpace(status.Error))
            return status;
        return status with { Error = failure };
    }
}

/// <summary>One installed speech engine.</summary>
/// <param name="Features">The <c>VoiceEngineFeatures</c> flags as an integer, so
/// the renderer can test them without a second copy of the enum.</param>
public sealed record VoiceEngineDto(
    string EngineId,
    string EngineName,
    int Features,
    bool IsReady,
    bool IsPreparing,
    string? Error,
    string Detail,
    long? DownloadBytes);

/// <summary>What a character's voice would be designed from.</summary>
/// <param name="Refusal">"None", or why it cannot be - "WithheldFromAi" when the
/// writer set the entry to never reach a model.</param>
public sealed record VoiceBriefDto(
    string CharacterId,
    string Name,
    string Description,
    string[] SampleLines,
    string Refusal);

/// <param name="Clip">Where the offered voice can be heard, in the clip cache.
/// Null when the design failed, and on nothing else.</param>
/// <param name="Seed">What this voice was drawn with. Shown beside the offer so
/// a writer who likes it can ask for the same one again - and so one they liked
/// and discarded is not gone for good.</param>
public sealed record VoiceDesignDto(
    string? VoiceId, string Description, string? Error, string? Clip = null, int? Seed = null)
{
    public static VoiceDesignDto Failed(string error) => new(null, string.Empty, error);
}

public sealed record DesignedVoiceDto(
    string VoiceId, string DisplayName, string Description, string EngineId, string DesignedAt,
    int? Seed = null);

/// <summary>One audition clip, base64 so it crosses JSON-RPC.</summary>
/// <param name="Key">The emotion it was read with.</param>
public sealed record AuditionClipDto(
    string Key, string Audio, string AudioFormat, int SampleRate, double DurationMs, string? Error);

/// <summary>The line an engine is making right now, or null when it is between
/// lines. Sent as it happens rather than with the window it belongs to.</summary>
public sealed record NarrationMakingDto(string? Key);

/// <summary>One rendered segment.</summary>
/// <param name="Clip">The name to fetch the audio by, or null when this segment
/// could not be spoken.</param>
public sealed record NarrationClipDto(string Key, string? Clip, double DurationMs, string? Error);

public sealed record NarrationAudioChunkDto(string StreamId, string Key, int Sequence, string Audio, int SampleRate = 24000);

/// <summary>What one render window produced.</summary>
/// <param name="EngineId">Null when no engine is ready, which is the signal to
/// read with the system voices instead.</param>
/// <param name="Total">How many segments the book has, so the interface knows
/// when it has reached the end.</param>
public sealed record NarrationRenderDto(string? EngineId, NarrationClipDto[] Clips, int Total);
