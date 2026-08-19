using System.Runtime.CompilerServices;
using Novalist.Backend;
using Novalist.Backend.Rpc;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Sdk;
using Novalist.Sdk.Hooks;
using Novalist.Sdk.Models.Narration;
using Novalist.Sdk.Services;
using Xunit;

namespace Novalist.Backend.Tests;

/// <summary>
/// Covers the designed half of narration: which engines are installed, the brief
/// a voice comes from, designing and forgetting one, and auditioning it across
/// the emotional range.
///
/// The engine here is a stub, which is the point of the seam: the whole path is
/// exercised with no model, and an engine that misbehaves - throws, refuses,
/// cannot design - is a case the host has to survive rather than a case that
/// only shows up on somebody's machine.
/// </summary>
public sealed class VoiceEngineRpcTests : IDisposable
{
    private readonly string _root;
    private readonly Workspace _workspace;
    private readonly VoiceEngineRpc _rpc;
    private readonly StubEngine _engine = new();
    private readonly NarrationClipCache _cache;

    public VoiceEngineRpcTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "nl-voice-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _workspace = new Workspace(Path.Combine(_root, "settings"));
        _workspace.Projects.CreateProjectAsync(_root, "VoiceNovel", "Book").GetAwaiter().GetResult();
        _workspace.OpenProjectAsync(_workspace.Projects.ProjectRoot!).GetAwaiter().GetResult();
        _workspace.ExtensionsHost.VoiceEngines.Add(_engine);
        // Its own cache folder, so one test's clips are never another's.
        _cache = new NarrationClipCache(Path.Combine(_root, "cache"));
        _rpc = new VoiceEngineRpc(_workspace, _cache);
    }

    public void Dispose()
    {
        _workspace.ExtensionsHost.VoiceEngines.Clear();
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private EntityService Entities => new(_workspace.Projects);

    private async Task<CharacterData> MiraAsync(AiInclusion ai = AiInclusion.WhenMentioned)
    {
        var mira = new CharacterData
        {
            Name = "Mira",
            Surname = "Vance",
            Age = "34",
            Build = "wiry",
            Ai = ai
        };
        await Entities.SaveCharacterAsync(mira);
        return mira;
    }

    private async Task SceneAsync(string html)
    {
        var chapter = await _workspace.Projects.CreateChapterAsync("One");
        var scene = await _workspace.Projects.CreateSceneAsync(chapter.Guid, "S");
        await _workspace.Projects.WriteSceneContentAsync(chapter, scene, html);
    }

    // ── voiceEngines/list ──

    [Fact]
    public async Task List_ReportsWhatTheEngineCanDoAndWhetherItIsReady()
    {
        var engines = await _rpc.ListAsync();

        var engine = Assert.Single(engines);
        Assert.Equal(StubEngine.Id, engine.EngineId);
        Assert.Equal("Stub", engine.EngineName);
        Assert.False(engine.IsReady);
        Assert.True(
            ((VoiceEngineFeatures)engine.Features).HasFlag(
                VoiceEngineFeatures.DesignFromDescription));
    }

    [Fact]
    public async Task List_NoEngineInstalledIsAnEmptyListRatherThanAFault()
    {
        _workspace.ExtensionsHost.VoiceEngines.Clear();

        Assert.Empty(await _rpc.ListAsync());
    }

    [Fact]
    public async Task List_AnEngineThatThrowsWhenAskedIsReportedAsBroken()
    {
        // Rather than taking the view down with it.
        _engine.ThrowOnStatus = true;

        var engine = Assert.Single(await _rpc.ListAsync());

        Assert.False(engine.IsReady);
        Assert.Equal(nameof(InvalidOperationException), engine.Error);
    }

    // ── voiceEngines/prepare ──

    [Fact]
    public async Task Prepare_MakesTheEngineReady()
    {
        var engine = await _rpc.PrepareAsync(StubEngine.Id);

        Assert.NotNull(engine);
        Assert.True(engine!.IsReady);
    }

    [Fact]
    public async Task Prepare_AnEngineThatIsNotInstalledIsNothing()
        => Assert.Null(await _rpc.PrepareAsync("nobody"));

    [Fact]
    public async Task Prepare_AFailureComesBackAsTheReasonRatherThanAsAnException()
    {
        // This stub throws and then says nothing about itself, so the type is
        // all there is. Something beats nothing: a writer told only that the
        // dialog closed has been told the least useful true thing.
        _engine.ThrowOnPrepare = true;

        var engine = await _rpc.PrepareAsync(StubEngine.Id);

        Assert.NotNull(engine);
        Assert.False(engine!.IsReady);
        Assert.Equal(nameof(InvalidOperationException), engine.Error);
    }

    [Fact]
    public async Task Prepare_AnEngineThatExplainsItselfIsQuotedRatherThanTheException()
    {
        // The whole point of the change: an engine that says "install Python"
        // must not have that replaced by "InvalidOperationException".
        _engine.ThrowOnPrepare = true;
        _engine.StatusError = "install Python 3";

        var engine = await _rpc.PrepareAsync(StubEngine.Id);

        Assert.Equal("install Python 3", engine!.Error);
    }

    // ── voiceEngines/brief ──

    [Fact]
    public async Task Brief_DescribesTheInstrumentAndQuotesTheirOwnLines()
    {
        var mira = await MiraAsync();
        await SceneAsync("<p>\"You are late,\" said Mira.</p>");

        var brief = await _rpc.BriefAsync(mira.Id);

        Assert.NotNull(brief);
        Assert.Equal("Mira Vance", brief!.Name);
        Assert.Contains("Age: 34", brief.Description);
        Assert.Contains("Build: wiry", brief.Description);
        Assert.Contains("You are late,", brief.SampleLines);
        Assert.Equal(nameof(VoiceBriefRefusal.None), brief.Refusal);
    }

    [Fact]
    public async Task Brief_ACharacterWhoDoesNotExistIsNothing()
        => Assert.Null(await _rpc.BriefAsync("nobody"));

    [Fact]
    public async Task Brief_WithNoProjectOpenIsNothingRatherThanAFault()
    {
        // The dialog can be opened from a window with no project in it, and an
        // exception across the wire would reach the writer as a failed call.
        var closed = new VoiceEngineRpc(new Workspace(Path.Combine(_root, "settings-brief")));

        Assert.Null(await closed.BriefAsync("mira"));
    }

    [Fact]
    public async Task Brief_RefusesAnEntryTheWriterWithheldFromModels()
    {
        var mira = await MiraAsync(AiInclusion.Never);

        var brief = await _rpc.BriefAsync(mira.Id);

        Assert.Equal(nameof(VoiceBriefRefusal.WithheldFromAi), brief!.Refusal);
        Assert.Equal(string.Empty, brief.Description);
    }

    [Fact]
    public async Task Brief_TheWriterCanOverruleThatDeliberately()
    {
        var mira = await MiraAsync(AiInclusion.Never);

        var brief = await _rpc.BriefAsync(mira.Id, consent: true);

        Assert.Equal(nameof(VoiceBriefRefusal.None), brief!.Refusal);
        Assert.Contains("Age: 34", brief.Description);
    }

    /// <summary>
    /// Designs a voice and keeps it, which is what designing used to do on its
    /// own.
    ///
    /// It is two steps now because design is not reliable per attempt: the same
    /// description asked for twice gives two voices, and one of them may not be
    /// the voice that was asked for. Nothing is stored until the writer has
    /// heard it. A design that failed has nothing to keep.
    /// </summary>
    private async Task<VoiceDesignDto> KeptAsync(
        string engineId, string characterId, string description, bool consent = false)
    {
        var offered = await _rpc.DesignAsync(engineId, characterId, description, consent);
        if (offered.Error == null)
            Assert.True(await _rpc.KeepVoiceAsync());
        return offered;
    }

    private async Task<VoiceDesignDto> KeptNarratorAsync(string engineId, string description)
    {
        var offered = await _rpc.DesignNarratorAsync(engineId, description);
        if (offered.Error == null)
            Assert.True(await _rpc.KeepVoiceAsync());
        return offered;
    }

    // ── voiceEngines/design ──

    [Fact]
    public async Task Design_StoresTheAudioAndCastsThemInIt()
    {
        var mira = await MiraAsync();

        var result = await KeptAsync(StubEngine.Id, mira.Id, "Age: 34. Low and level.");

        Assert.Null(result.Error);
        Assert.NotNull(result.VoiceId);
        // The audio is the voice, and it is what was kept.
        var stored = Assert.Single(await _rpc.VoicesAsync());
        Assert.Equal(result.VoiceId, stored.VoiceId);
        Assert.Equal("Mira Vance", stored.DisplayName);
        Assert.Equal(StubEngine.Id, stored.EngineId);
        // And they are cast, rather than left one step short of a reading.
        var cast = await new NarrationRpc(_workspace).CastAsync();
        Assert.Equal(result.VoiceId, cast.Members.FirstOrDefault()?.VoiceId ?? await CastOf(mira.Id));
    }

    private async Task<string?> CastOf(string characterId)
        => (await new VoiceCast(_workspace.Projects, _workspace.FileService).ReadAsync())
            .Voices.GetValueOrDefault(characterId);

    [Fact]
    public async Task Design_StripsTheEmotionOutOfWhatTheWriterTyped()
    {
        // A rule the dialog can talk its way around is not a rule: whatever the
        // writer edited the brief to say goes through the same filter.
        var mira = await MiraAsync();

        await KeptAsync(StubEngine.Id, mira.Id, "A wiry, angry, joyful woman.");

        Assert.DoesNotContain("angry", _engine.LastBrief!.Description);
        Assert.DoesNotContain("joyful", _engine.LastBrief.Description);
        Assert.Contains("wiry", _engine.LastBrief.Description);
    }

    [Fact]
    public async Task Design_AnEngineThatCannotDesignSaysSoRatherThanTrying()
    {
        _engine.CanDesign = false;
        var mira = await MiraAsync();

        var result = await KeptAsync(StubEngine.Id, mira.Id, "Low and level.");

        Assert.Equal("EngineCannotDesign", result.Error);
        Assert.Empty(await _rpc.VoicesAsync());
    }

    [Fact]
    public async Task Design_RefusesWithoutAnEngineOrACharacter()
    {
        var mira = await MiraAsync();

        Assert.Equal("NoEngine", (await KeptAsync("nobody", mira.Id, "x")).Error);
        Assert.Equal("NoCharacter", (await KeptAsync(StubEngine.Id, "nobody", "x")).Error);
    }

    [Fact]
    public async Task Design_HonoursAnEntryTheWriterWithheldFromModels()
    {
        var mira = await MiraAsync(AiInclusion.Never);

        var refused = await KeptAsync(StubEngine.Id, mira.Id, "Low and level.");
        Assert.Equal(nameof(VoiceBriefRefusal.WithheldFromAi), refused.Error);

        // And designs once they say so deliberately.
        var allowed = await KeptAsync(StubEngine.Id, mira.Id, "Low and level.", consent: true);
        Assert.Null(allowed.Error);
    }

    [Fact]
    public async Task Design_AnEngineThatThrowsComesBackAsTheReason()
    {
        _engine.ThrowOnDesign = true;
        var mira = await MiraAsync();

        var result = await KeptAsync(StubEngine.Id, mira.Id, "Low and level.");

        // The engine's own reason, not the wrapper's type name. "An
        // InvalidOperationException occurred" is not a reason and left the
        // writer with nothing to act on and nothing to tell us.
        Assert.Equal("no", result.Error);
        Assert.Empty(await _rpc.VoicesAsync());
    }

    [Fact]
    public async Task Design_WithNoProjectOpenHasNowhereToPutIt()
    {
        var closed = new Workspace(Path.Combine(_root, "settings-closed"));
        closed.ExtensionsHost.VoiceEngines.Add(_engine);

        var result = await new VoiceEngineRpc(closed).DesignAsync(StubEngine.Id, "mira", "x");

        Assert.Equal("NoProject", result.Error);
        // And the engine was never asked to spend a minute on something that
        // could not have been kept.
        Assert.Null(_engine.LastBrief);
    }

    [Fact]
    public async Task Design_AnEmptyDescriptionFallsBackToTheBriefRatherThanSendingNothing()
    {
        var mira = await MiraAsync();

        await KeptAsync(StubEngine.Id, mira.Id, "   ");

        Assert.Contains("Age: 34", _engine.LastBrief!.Description);
    }

    // ── voiceEngines/forget ──

    [Fact]
    public async Task Forget_DropsTheVoiceAndUnCastsWhoeverReadInIt()
    {
        var mira = await MiraAsync();
        var designed = await KeptAsync(StubEngine.Id, mira.Id, "Low and level.");
        await new NarrationRpc(_workspace).SetVoiceAsync(null, designed.VoiceId);

        Assert.True(await _rpc.ForgetAsync(designed.VoiceId!));

        Assert.Empty(await _rpc.VoicesAsync());
        var cast = await new VoiceCast(_workspace.Projects, _workspace.FileService).ReadAsync();
        // Nobody is left pointing at a voice that no longer exists.
        Assert.Null(cast.NarratorVoiceId);
        Assert.DoesNotContain(mira.Id, cast.Voices.Keys);
        Assert.Equal(designed.VoiceId, _engine.Forgotten);
    }

    [Fact]
    public async Task Forget_SaysWhenThereWasNothingToForget()
        => Assert.False(await _rpc.ForgetAsync("never-designed"));

    [Fact]
    public async Task Forget_AnEngineThatRefusesDoesNotKeepTheProjectHoldingIt()
    {
        var mira = await MiraAsync();
        var designed = await KeptAsync(StubEngine.Id, mira.Id, "Low and level.");
        _engine.ThrowOnForget = true;

        Assert.True(await _rpc.ForgetAsync(designed.VoiceId!));

        Assert.Empty(await _rpc.VoicesAsync());
    }

    // ── voiceEngines/audition ──

    [Fact]
    public async Task Audition_ReadsTheSameLineAtSeveralPointsOnTheRange()
    {
        // One neutral sample says nothing about whether the casting works.
        var mira = await MiraAsync();
        var designed = await KeptAsync(StubEngine.Id, mira.Id, "Low and level.");

        var clips = await _rpc.AuditionAsync(designed.VoiceId!, "You are late.");

        Assert.Equal(3, clips.Length);
        Assert.Equal(["neutral", "angry", "sorrowful"], clips.Select(c => c.Key));
        Assert.All(clips, c => Assert.NotEmpty(c.Audio));
        Assert.All(clips, c => Assert.Null(c.Error));
        // The direction reached the engine rather than being dropped on the way.
        Assert.Equal(
            ["neutral", "angry", "sorrowful"],
            _engine.LastRequest!.Segments.Select(s => s.Direction.Key));
        Assert.All(_engine.LastRequest.Segments, s => Assert.NotEmpty(s.Direction.Vector));
        // And it travelled beside the words, never inside them.
        Assert.All(_engine.LastRequest.Segments, s => Assert.Equal("You are late.", s.Text));
    }

    [Fact]
    public async Task Audition_TakesTheEmotionsTheCallerAsksFor()
    {
        var mira = await MiraAsync();
        var designed = await KeptAsync(StubEngine.Id, mira.Id, "Low and level.");

        var clips = await _rpc.AuditionAsync(designed.VoiceId!, "A line.", ["joyful"]);

        Assert.Equal(["joyful"], clips.Select(c => c.Key));
    }

    [Fact]
    public async Task Audition_AVoiceThatWasNeverDesignedHasNothingToPlay()
        => Assert.Empty(await _rpc.AuditionAsync("never-designed", "A line."));

    [Fact]
    public async Task Audition_AnEngineThatThrowsMidWayKeepsWhatItAlreadyGave()
    {
        var mira = await MiraAsync();
        var designed = await KeptAsync(StubEngine.Id, mira.Id, "Low and level.");
        _engine.ThrowOnRenderAfter = 1;

        var clips = await _rpc.AuditionAsync(designed.VoiceId!, "A line.");

        Assert.Single(clips);
    }

    [Fact]
    public async Task Audition_ALanguageWithNoLexiconStillReadsPlainly()
    {
        _workspace.Settings.Settings.AutoReplacementLanguage = "fr";
        var mira = await MiraAsync();
        var designed = await KeptAsync(StubEngine.Id, mira.Id, "Low and level.");

        var clips = await _rpc.AuditionAsync(designed.VoiceId!, "A line.");

        // No emotion vocabulary to pick from, so the neutral reading is the only
        // one offered rather than an English one.
        Assert.Equal(["neutral"], clips.Select(c => c.Key));
    }

    // ── narration/render ──

    [Fact]
    public async Task Render_WithNoEngineReadyReadsWithTheSystemVoicesInstead()
    {
        // A null engine id is the signal for that, rather than an empty clip
        // list which would read as "rendered nothing".
        await MiraAsync();
        await SceneAsync("<p>\"A,\" said Mira.</p>");

        var render = await _rpc.RenderAsync(0, 8);

        Assert.Null(render.EngineId);
        Assert.Empty(render.Clips);
    }

    [Fact]
    public async Task Render_SpeaksTheWindowAskedForAndSaysHowLongTheBookIs()
    {
        var mira = await MiraAsync();
        await SceneAsync("<p>She waited. \"You are late,\" said Mira.</p>");
        await _rpc.PrepareAsync(StubEngine.Id);
        var designed = await KeptAsync(StubEngine.Id, mira.Id, "Low and level.");
        await new NarrationRpc(_workspace).SetVoiceAsync(null, designed.VoiceId);

        var render = await _rpc.RenderAsync(0, 8);

        Assert.Equal(StubEngine.Id, render.EngineId);
        Assert.Equal(3, render.Total);
        Assert.Equal(3, render.Clips.Length);
        Assert.All(render.Clips, c => Assert.Null(c.Error));
        // The audio went to the cache and comes back as a name, not as base64
        // in the message.
        Assert.All(render.Clips, c => Assert.NotNull(c.Clip));
        Assert.All(render.Clips, c => Assert.DoesNotContain("/", c.Clip!));
    }

    [Fact]
    public async Task Render_TakesOnlyTheSegmentsItWasAskedFor()
    {
        var mira = await MiraAsync();
        await SceneAsync(
            "<p>\"A,\" said Mira.</p><p>\"B,\" said Mira.</p><p>\"C,\" said Mira.</p>");
        await _rpc.PrepareAsync(StubEngine.Id);
        var designed = await KeptAsync(StubEngine.Id, mira.Id, "Low and level.");
        await new NarrationRpc(_workspace).SetVoiceAsync(null, designed.VoiceId);

        var first = await _rpc.RenderAsync(0, 2);
        var second = await _rpc.RenderAsync(2, 2);

        Assert.Equal(2, first.Clips.Length);
        Assert.NotEmpty(second.Clips);
        Assert.Empty(first.Clips.Select(c => c.Key).Intersect(second.Clips.Select(c => c.Key)));
    }

    [Fact]
    public async Task Render_PastTheEndOfTheBookHasNothingToSpeak()
    {
        var mira = await MiraAsync();
        await SceneAsync("<p>\"A,\" said Mira.</p>");
        await _rpc.PrepareAsync(StubEngine.Id);
        await KeptAsync(StubEngine.Id, mira.Id, "Low and level.");

        var render = await _rpc.RenderAsync(500, 8);

        Assert.Equal(StubEngine.Id, render.EngineId);
        Assert.Empty(render.Clips);
    }

    [Fact]
    public async Task Render_WithNothingCastHasNothingToSpeak()
    {
        await MiraAsync();
        await SceneAsync("<p>\"A,\" said Mira.</p>");
        await _rpc.PrepareAsync(StubEngine.Id);

        var render = await _rpc.RenderAsync(0, 8);

        Assert.Empty(render.Clips);
    }

    [Fact]
    public async Task Render_TellsTheEngineOnlyWhatItSaysItCanTake()
    {
        var mira = await MiraAsync();
        await SceneAsync("<p>\"You are late,\" Mira snapped.</p>");
        await _rpc.PrepareAsync(StubEngine.Id);
        var designed = await KeptAsync(StubEngine.Id, mira.Id, "Low and level.");
        await new NarrationRpc(_workspace).SetVoiceAsync(null, designed.VoiceId);

        await _rpc.RenderAsync(0, 8);

        // The stub advertises the vector and not the instruction, so it gets the
        // numbers and no sentence.
        var spoken = _engine.LastRequest!.Segments.First(s => s.IsDialogue);
        Assert.Equal("angry", spoken.Direction.Key);
        Assert.NotEmpty(spoken.Direction.Vector);
        Assert.Equal(string.Empty, spoken.Direction.Instruction);
        // And the direction travelled beside the words, never inside them.
        Assert.DoesNotContain("angry", spoken.Text);
    }

    [Fact]
    public async Task Render_ASegmentTheEngineRefusesComesBackAsThatRatherThanAsSilence()
    {
        var mira = await MiraAsync();
        await SceneAsync("<p>\"A,\" said Mira.</p>");
        await _rpc.PrepareAsync(StubEngine.Id);
        var designed = await KeptAsync(StubEngine.Id, mira.Id, "Low and level.");
        await new NarrationRpc(_workspace).SetVoiceAsync(null, designed.VoiceId);
        _engine.RefuseRender = true;

        var render = await _rpc.RenderAsync(0, 8);

        var clip = render.Clips.First();
        Assert.Null(clip.Clip);
        Assert.NotNull(clip.Error);
    }

    [Fact]
    public async Task Render_AnEngineThatThrowsIsReportedRatherThanPropagated()
    {
        var mira = await MiraAsync();
        await SceneAsync("<p>\"A,\" said Mira.</p>");
        await _rpc.PrepareAsync(StubEngine.Id);
        var designed = await KeptAsync(StubEngine.Id, mira.Id, "Low and level.");
        await new NarrationRpc(_workspace).SetVoiceAsync(null, designed.VoiceId);
        _engine.ThrowOnRenderAfter = 0;

        var render = await _rpc.RenderAsync(0, 8);

        Assert.Contains(render.Clips, c => c.Error != null);
    }

    [Fact]
    public async Task RenderStop_EmptiesTheCache()
    {
        var mira = await MiraAsync();
        await SceneAsync("<p>\"A,\" said Mira.</p>");
        await _rpc.PrepareAsync(StubEngine.Id);
        var designed = await KeptAsync(StubEngine.Id, mira.Id, "Low and level.");
        await new NarrationRpc(_workspace).SetVoiceAsync(null, designed.VoiceId);
        await _rpc.RenderAsync(0, 8);
        Assert.True(_cache.Size() > 0);

        Assert.True(_rpc.RenderStop());

        Assert.Equal(0, _cache.Size());
    }

    [Fact]
    public async Task Render_StoppedPartWayKeepsWhatItAlreadyHas()
    {
        var mira = await MiraAsync();
        await SceneAsync("<p>\"A,\" said Mira.</p><p>\"B,\" said Mira.</p>");
        await _rpc.PrepareAsync(StubEngine.Id);
        var designed = await KeptAsync(StubEngine.Id, mira.Id, "Low and level.");
        await new NarrationRpc(_workspace).SetVoiceAsync(null, designed.VoiceId);
        _engine.Hold = new TaskCompletionSource();
        _engine.Started = new TaskCompletionSource();

        var rendering = _rpc.RenderAsync(0, 8);
        // Stopped while the engine is genuinely part way through.
        await _engine.Started.Task;
        Assert.True(_rpc.RenderStop());
        _engine.Hold.SetResult();
        var render = await rendering;

        // What was rendered before the stop still comes back; the interface
        // simply will not play it.
        Assert.True(render.Clips.Length < 3);
    }

    [Fact]
    public async Task Render_AnEngineThatIgnoresTheTokenIsStoppedByTheHostAnyway()
    {
        var mira = await MiraAsync();
        await SceneAsync("<p>\"A,\" said Mira.</p><p>\"B,\" said Mira.</p>");
        await _rpc.PrepareAsync(StubEngine.Id);
        var designed = await KeptAsync(StubEngine.Id, mira.Id, "Low and level.");
        await new NarrationRpc(_workspace).SetVoiceAsync(null, designed.VoiceId);
        _engine.IgnoreCancellation = true;
        _engine.Hold = new TaskCompletionSource();
        _engine.Started = new TaskCompletionSource();

        var rendering = _rpc.RenderAsync(0, 8);
        await _engine.Started.Task;
        _rpc.RenderStop();
        _engine.Hold.SetResult();
        var render = await rendering;

        Assert.True(render.Clips.Length < 3);
    }

    [Fact]
    public async Task Render_AnEngineThatWillNotSayHowItIsIsNotUsed()
    {
        await MiraAsync();
        await SceneAsync("<p>\"A,\" said Mira.</p>");
        await _rpc.PrepareAsync(StubEngine.Id);
        _engine.ThrowOnStatus = true;

        var render = await _rpc.RenderAsync(0, 8);

        // Falls back to the system voices rather than trusting an engine that
        // cannot answer a question about itself.
        Assert.Null(render.EngineId);
    }

    [Fact]
    public async Task Render_WithNoProjectOpenHasNoBookToRead()
    {
        await _rpc.PrepareAsync(StubEngine.Id);
        var closed = new Workspace(Path.Combine(_root, "settings-render"));
        closed.ExtensionsHost.VoiceEngines.Add(_engine);

        var render = await new VoiceEngineRpc(closed, _cache).RenderAsync(0, 8);

        Assert.Equal(StubEngine.Id, render.EngineId);
        Assert.Empty(render.Clips);
        Assert.Equal(0, render.Total);
    }

    // ── narration/designNarrator ──

    [Fact]
    public async Task NarratorBrief_DescribesTheBookRatherThanAnybodyInIt()
    {
        var book = _workspace.Projects.ActiveBook!;
        book.NarrativePerson = "third limited";
        book.Tense = "past";
        await _workspace.Projects.SaveProjectAsync();

        var brief = _rpc.NarratorBrief();

        Assert.Contains("third limited", brief);
        Assert.Contains("past", brief);
    }

    [Fact]
    public async Task DesignNarrator_StoresTheVoiceAndCastsTheNarratorInIt()
    {
        await _rpc.PrepareAsync(StubEngine.Id);

        var result = await KeptNarratorAsync(StubEngine.Id, "Level and unhurried.");

        Assert.Null(result.Error);
        var cast = await new VoiceCast(_workspace.Projects, _workspace.FileService).ReadAsync();
        Assert.Equal(result.VoiceId, cast.NarratorVoiceId);
        Assert.Contains(await _rpc.VoicesAsync(), v => v.VoiceId == result.VoiceId);
    }

    [Fact]
    public async Task DesignNarrator_StripsTheEmotionOutOfWhatTheWriterTyped()
    {
        await _rpc.PrepareAsync(StubEngine.Id);

        await KeptNarratorAsync(StubEngine.Id, "Level and angry and joyful.");

        Assert.DoesNotContain("angry", _engine.LastBrief!.Description);
        Assert.Contains("Level", _engine.LastBrief.Description);
    }

    [Fact]
    public async Task DesignNarrator_WithNothingTypedFallsBackToTheBooksOwnBrief()
    {
        var book = _workspace.Projects.ActiveBook!;
        book.NarrativePerson = "first person";
        await _workspace.Projects.SaveProjectAsync();
        await _rpc.PrepareAsync(StubEngine.Id);

        await KeptNarratorAsync(StubEngine.Id, "   ");

        Assert.Contains("first person", _engine.LastBrief!.Description);
    }

    [Fact]
    public async Task DesignNarrator_RefusesWithoutAProjectOrAnEngine()
    {
        var closed = new Workspace(Path.Combine(_root, "settings-narrator"));
        closed.ExtensionsHost.VoiceEngines.Add(_engine);

        Assert.Equal(
            "NoProject",
            (await new VoiceEngineRpc(closed).DesignNarratorAsync(StubEngine.Id, "x")).Error);
        Assert.Equal("NoEngine", (await KeptNarratorAsync("nobody", "x")).Error);
    }

    [Fact]
    public async Task DesignNarrator_AnEngineThatCannotDesignSaysSo()
    {
        _engine.CanDesign = false;

        Assert.Equal(
            "EngineCannotDesign",
            (await KeptNarratorAsync(StubEngine.Id, "x")).Error);
    }

    [Fact]
    public async Task DesignNarrator_AnEngineThatThrowsComesBackAsTheReason()
    {
        _engine.ThrowOnDesign = true;

        var result = await KeptNarratorAsync(StubEngine.Id, "Level.");

        Assert.Equal("no", result.Error);
    }

    /// <summary>
    /// An engine that speaks nothing and can be made to misbehave on demand.
    /// </summary>
    // ── narration/auditionLine ──

    /// <summary>The one scene these tests write, with a narrator cast so its
    /// prose has a voice.</summary>
    private async Task<(string ChapterGuid, string SceneId)> LineSceneAsync(string html)
    {
        var chapter = await _workspace.Projects.CreateChapterAsync("One");
        var scene = await _workspace.Projects.CreateSceneAsync(chapter.Guid, "S");
        await _workspace.Projects.WriteSceneContentAsync(chapter, scene, html);

        var store = new VoiceStore(_workspace.Projects, _workspace.FileService);
        await store.SaveAsync(
            new DesignedVoice(
                "narrator", "Narrator", string.Empty, StubEngine.Id, "wav", 16000,
                DateTime.UtcNow.ToString("O")),
            [1, 2, 3]);
        var cast = new VoiceCast(_workspace.Projects, _workspace.FileService);
        var sheet = await cast.ReadAsync();
        sheet.NarratorVoiceId = "narrator";
        await cast.WriteAsync(sheet);

        return (chapter.Guid, scene.Id);
    }

    [Fact]
    public async Task AuditionLine_SpeaksTheLineTheSelectionSitsIn()
    {
        var (chapterGuid, sceneId) = await LineSceneAsync("<p>She turned away from the window.</p>");
        await _rpc.PrepareAsync(StubEngine.Id);

        // A phrase, not the whole line - which is what a writer actually selects.
        var clip = await _rpc.AuditionLineAsync(chapterGuid, sceneId, "turned away");

        Assert.Null(clip.Error);
        Assert.NotNull(clip.Clip);
        Assert.Contains("She turned away", _engine.LastRequest!.Segments[0].Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AuditionLine_ASelectionSpanningMoreThanTheLine_StillFindsIt()
    {
        var (chapterGuid, sceneId) = await LineSceneAsync("<p>\u201cGet out,\u201d she said.</p>");
        await _rpc.PrepareAsync(StubEngine.Id);

        var clip = await _rpc.AuditionLineAsync(
            chapterGuid, sceneId, "\u201cGet out,\u201d she said. And she meant it.");

        Assert.Null(clip.Error);
    }

    [Fact]
    public async Task AuditionLine_WhitespaceIsNotWhatDecidesAMatch()
    {
        // The editor hands back the document's whitespace; the script's is
        // collapsed. Comparing them raw finds nothing.
        var (chapterGuid, sceneId) = await LineSceneAsync("<p>She turned\n   away.</p>");
        await _rpc.PrepareAsync(StubEngine.Id);

        Assert.Null((await _rpc.AuditionLineAsync(chapterGuid, sceneId, "She turned away.")).Error);
    }

    [Fact]
    public async Task AuditionLine_WithNoEngineReadySaysSo()
    {
        var (chapterGuid, sceneId) = await LineSceneAsync("<p>She turned away.</p>");

        Assert.Equal(
            "no-engine", (await _rpc.AuditionLineAsync(chapterGuid, sceneId, "turned")).Error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AuditionLine_WithNothingSelectedSaysSo(string text)
    {
        var (chapterGuid, sceneId) = await LineSceneAsync("<p>She turned away.</p>");
        await _rpc.PrepareAsync(StubEngine.Id);

        Assert.Equal("empty", (await _rpc.AuditionLineAsync(chapterGuid, sceneId, text)).Error);
    }

    [Fact]
    public async Task AuditionLine_TextThatIsNotInTheSceneSaysSo()
    {
        var (chapterGuid, sceneId) = await LineSceneAsync("<p>She turned away.</p>");
        await _rpc.PrepareAsync(StubEngine.Id);

        Assert.Equal(
            "not-in-scene",
            (await _rpc.AuditionLineAsync(chapterGuid, sceneId, "a line from another book")).Error);
    }

    [Fact]
    public async Task AuditionLine_InASceneThatIsNoLongerThereIsNotAFault()
    {
        await _rpc.PrepareAsync(StubEngine.Id);

        Assert.Equal("not-in-scene", (await _rpc.AuditionLineAsync("gone", "gone", "anything")).Error);
    }

    [Fact]
    public async Task AuditionLine_WithNothingCastSaysSoRatherThanRenderingSilence()
    {
        var chapter = await _workspace.Projects.CreateChapterAsync("One");
        var scene = await _workspace.Projects.CreateSceneAsync(chapter.Guid, "S");
        await _workspace.Projects.WriteSceneContentAsync(chapter, scene, "<p>She turned away.</p>");
        await _rpc.PrepareAsync(StubEngine.Id);

        Assert.Equal(
            "no-voice",
            (await _rpc.AuditionLineAsync(chapter.Guid, scene.Id, "turned away")).Error);
    }

    [Fact]
    public async Task AuditionLine_AnEngineThatRefusesTheLineSaysWhy()
    {
        var (chapterGuid, sceneId) = await LineSceneAsync("<p>She turned away.</p>");
        await _rpc.PrepareAsync(StubEngine.Id);
        _engine.RefuseRender = true;

        Assert.NotNull((await _rpc.AuditionLineAsync(chapterGuid, sceneId, "turned away")).Error);
    }

    [Fact]
    public async Task AuditionLine_AnEngineThatAnswersNothingIsReportedRatherThanHanging()
    {
        // A sidecar that died between being asked and answering. Silence is the
        // one outcome that reads as the feature being broken.
        var (chapterGuid, sceneId) = await LineSceneAsync("<p>She turned away.</p>");
        await _rpc.PrepareAsync(StubEngine.Id);
        _engine.RenderNothing = true;

        Assert.Equal(
            "sidecar-exited",
            (await _rpc.AuditionLineAsync(chapterGuid, sceneId, "turned away")).Error);
    }

    [Fact]
    public async Task AuditionLine_AnEngineThatThrowsIsReportedByTypeAndNotByLine()
    {
        var (chapterGuid, sceneId) = await LineSceneAsync("<p>She turned away.</p>");
        await _rpc.PrepareAsync(StubEngine.Id);
        _engine.ThrowOnRenderAfter = 0;

        var clip = await _rpc.AuditionLineAsync(chapterGuid, sceneId, "turned away");

        Assert.Equal(nameof(InvalidOperationException), clip.Error);
    }

    private sealed class StubEngine : IExtension, IVoiceEngineContributor
    {
        public const string Id = "com.example.stub";

        public bool CanDesign { get; set; } = true;
        public bool ThrowOnStatus { get; set; }
        public bool ThrowOnPrepare { get; set; }
        public bool ThrowOnDesign { get; set; }
        public bool ThrowOnForget { get; set; }

        /// <summary>What the engine says about itself, which is where a reason a
        /// writer can act on belongs.</summary>
        public string? StatusError { get; set; }
        public bool RefuseRender { get; set; }

        /// <summary>An engine that returns nothing at all - a sidecar that died
        /// between being asked and answering.</summary>
        public bool RenderNothing { get; set; }

        /// <summary>Held before the second clip, so a test can stop a render
        /// while it is genuinely in flight rather than before it starts.</summary>
        public TaskCompletionSource? Hold { get; set; }

        /// <summary>An engine that never looks at the token. The host's own
        /// check is what has to stop it then.</summary>
        public bool IgnoreCancellation { get; set; }

        /// <summary>Signalled once the render is genuinely under way. Stopping
        /// before that cancels nothing, because the host has not yet reached the
        /// engine - which is a race in the test, not in the product.</summary>
        public TaskCompletionSource? Started { get; set; }
        public int? ThrowOnRenderAfter { get; set; }

        public VoiceBrief? LastBrief { get; private set; }
        public NarrationRequest? LastRequest { get; private set; }
        public string? Forgotten { get; private set; }

        private bool _ready;

        string IExtension.Id => Id;
        public string DisplayName => "Stub";
        public string Description => "A voice engine that speaks nothing.";
        public string Version => "1.0";
        public string Author => "Tests";
        public void Initialize(IHostServices host) { }
        public void Shutdown() { }

        public string EngineId => Id;
        public string EngineName => "Stub";

        public VoiceEngineFeatures Features =>
            (CanDesign ? VoiceEngineFeatures.DesignFromDescription : VoiceEngineFeatures.None)
            | VoiceEngineFeatures.EmotionVector;

        public Task<VoiceEngineStatus> GetStatusAsync(CancellationToken cancellationToken = default)
            => ThrowOnStatus
                ? throw new InvalidOperationException("no")
                : Task.FromResult(new VoiceEngineStatus { IsReady = _ready, Error = StatusError });

        public Task PrepareAsync(
            IProgress<VoiceEnginePrepare>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (ThrowOnPrepare)
                throw new InvalidOperationException("no");
            _ready = true;
            return Task.CompletedTask;
        }

        public Task<VoiceDesignResult> DesignVoiceAsync(
            VoiceBrief brief, CancellationToken cancellationToken = default)
        {
            if (ThrowOnDesign)
                throw new InvalidOperationException("no");
            LastBrief = brief;
            return Task.FromResult(new VoiceDesignResult
            {
                VoiceId = brief.VoiceId,
                ReferenceAudio = [1, 2, 3, 4],
                SampleRate = 16000,
                ResolvedDescription = brief.Description
            });
        }

        public async IAsyncEnumerable<NarrationClip> RenderAsync(
            NarrationRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            Started?.TrySetResult();
            if (RenderNothing)
                yield break;
            var given = 0;
            foreach (var segment in request.Segments)
            {
                if (ThrowOnRenderAfter is { } limit && given >= limit)
                    throw new InvalidOperationException("no");
                if (given == 1 && Hold != null)
                    await Hold.Task;
                if (!IgnoreCancellation)
                    cancellationToken.ThrowIfCancellationRequested();
                given++;
                yield return RefuseRender
                    ? new NarrationClip { Key = segment.Key, Error = "no voice" }
                    : new NarrationClip
                    {
                        Key = segment.Key,
                        // Distinct per segment, so a cache named after content
                        // is exercised rather than collapsing every clip into
                        // one file.
                        Audio = [9, (byte)(given & 0xFF)],
                        SampleRate = 16000,
                        DurationMs = 100
                    };
                await Task.Yield();
            }
        }

        public Task ForgetVoiceAsync(string voiceId, CancellationToken cancellationToken = default)
        {
            if (ThrowOnForget)
                throw new InvalidOperationException("no");
            Forgotten = voiceId;
            return Task.CompletedTask;
        }
    }

    // ── offered, then kept or thrown away ──

    [Fact]
    public async Task Design_OffersTheVoiceAndStoresNothingUntilItIsKept()
    {
        // Design is not reliable per attempt, and storing the first result made
        // a miss into the character's voice until somebody noticed.
        var mira = await MiraAsync();

        var offered = await _rpc.DesignAsync(StubEngine.Id, mira.Id, "Low and level.");

        Assert.Null(offered.Error);
        // There is something to listen to before deciding.
        Assert.NotNull(offered.Clip);
        // And nothing has been kept or cast.
        Assert.Empty(await _rpc.VoicesAsync());
        Assert.Null(await CastOf(mira.Id));
    }

    [Fact]
    public async Task KeepVoice_StoresTheOfferedVoiceAndCastsThemInIt()
    {
        var mira = await MiraAsync();
        var offered = await _rpc.DesignAsync(StubEngine.Id, mira.Id, "Low and level.");

        Assert.True(await _rpc.KeepVoiceAsync());

        var stored = Assert.Single(await _rpc.VoicesAsync());
        Assert.Equal(offered.VoiceId, stored.VoiceId);
        Assert.Equal(offered.VoiceId, await CastOf(mira.Id));
    }

    [Fact]
    public async Task KeepVoice_TwiceKeepsOneVoice()
    {
        var mira = await MiraAsync();
        await _rpc.DesignAsync(StubEngine.Id, mira.Id, "Low and level.");
        await _rpc.KeepVoiceAsync();

        // The second press has nothing left to keep.
        Assert.False(await _rpc.KeepVoiceAsync());
        Assert.Single(await _rpc.VoicesAsync());
    }

    [Fact]
    public async Task KeepVoice_WithNothingOfferedKeepsNothing()
        => Assert.False(await _rpc.KeepVoiceAsync());

    [Fact]
    public async Task DiscardVoice_ThrowsTheOfferAway()
    {
        var mira = await MiraAsync();
        await _rpc.DesignAsync(StubEngine.Id, mira.Id, "Low and level.");

        Assert.True(_rpc.DiscardVoice());

        // And there is nothing left for a later Keep to commit.
        Assert.False(await _rpc.KeepVoiceAsync());
        Assert.Empty(await _rpc.VoicesAsync());
    }

    [Fact]
    public void DiscardVoice_WithNothingOfferedIsNotAFault()
        => Assert.False(_rpc.DiscardVoice());

    [Fact]
    public async Task DesignNarrator_OffersBeforeItCasts()
    {
        var offered = await _rpc.DesignNarratorAsync(StubEngine.Id, "Low and level.");

        Assert.Null(offered.Error);
        Assert.NotNull(offered.Clip);
        var before = await new VoiceCast(_workspace.Projects, _workspace.FileService).ReadAsync();
        Assert.Null(before.NarratorVoiceId);

        Assert.True(await _rpc.KeepVoiceAsync());

        var after = await new VoiceCast(_workspace.Projects, _workspace.FileService).ReadAsync();
        Assert.Equal(offered.VoiceId, after.NarratorVoiceId);
    }
}
