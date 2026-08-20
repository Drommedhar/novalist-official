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
        Assert.DoesNotContain("Build: wiry", brief.Description);
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
        Assert.False(string.IsNullOrWhiteSpace(
            _engine.LastRequest!.VoiceReferenceTexts[designed.VoiceId]));
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
    public async Task RenderStop_KeepsWhatWasMadeSoPlayingAgainDoesNotCostTwice()
    {
        // Stopping to fix a word and pressing Play again is the commonest thing
        // there is to do in this view. Emptying the cache here meant the scene
        // was spoken from nothing every time - minutes of a model reproducing
        // audio that was on the disk beside it.
        var mira = await MiraAsync();
        await SceneAsync("<p>\"A,\" said Mira.</p>");
        await _rpc.PrepareAsync(StubEngine.Id);
        var designed = await KeptAsync(StubEngine.Id, mira.Id, "Low and level.");
        await new NarrationRpc(_workspace).SetVoiceAsync(null, designed.VoiceId);
        await _rpc.RenderAsync(0, 8);
        var made = _cache.Size();
        Assert.True(made > 0);

        Assert.True(_rpc.RenderStop());

        Assert.Equal(made, _cache.Size());
    }

    [Fact]
    public async Task Render_SaysWhichLineIsBeingMadeAsItHappens()
    {
        // A window is one request and one answer, so without this the page
        // learns nothing for the whole of it - and marking the batch that was
        // asked for hatches a dozen sentences as "being made" when eleven have
        // not been started.
        var said = new List<string?>();
        VoiceEngineRpc.Making = m => said.Add(m.Key);
        try
        {
            var mira = await MiraAsync();
            await SceneAsync("<p>\"A,\" said Mira. She waited.</p>");
            await _rpc.PrepareAsync(StubEngine.Id);
            var designed = await KeptAsync(StubEngine.Id, mira.Id, "Low and level.");
            await new NarrationRpc(_workspace).SetVoiceAsync(null, designed.VoiceId);

            await _rpc.RenderAsync(0, 8);

            // One line named at a time, and nothing left named at the end.
            Assert.All(said, key => Assert.True(key == null || key.Length > 0));
            Assert.True(said.Count > 1);
            Assert.Null(said[^1]);
        }
        finally
        {
            VoiceEngineRpc.Making = null;
        }
    }

    [Fact]
    public async Task Render_SaysNothingIsBeingMadeWhenItAllCameOffTheDisk()
    {
        var mira = await MiraAsync();
        await SceneAsync("<p>\"A,\" said Mira.</p>");
        await _rpc.PrepareAsync(StubEngine.Id);
        var designed = await KeptAsync(StubEngine.Id, mira.Id, "Low and level.");
        await new NarrationRpc(_workspace).SetVoiceAsync(null, designed.VoiceId);
        await _rpc.RenderAsync(0, 8);

        var said = new List<string?>();
        VoiceEngineRpc.Making = m => said.Add(m.Key);
        try
        {
            await _rpc.RenderAsync(0, 8);
        }
        finally
        {
            VoiceEngineRpc.Making = null;
        }

        // Nothing was made, so nothing should have been reported as being made -
        // a line that flashes as "working" and is already on disk is a lie about
        // where the time is going.
        Assert.Empty(said);
    }

    [Fact]
    public async Task Render_ASecondTimeReusesWhatWasAlreadyMade()
    {
        var mira = await MiraAsync();
        await SceneAsync("<p>\"A,\" said Mira.</p>");
        await _rpc.PrepareAsync(StubEngine.Id);
        var designed = await KeptAsync(StubEngine.Id, mira.Id, "Low and level.");
        await new NarrationRpc(_workspace).SetVoiceAsync(null, designed.VoiceId);

        var first = await _rpc.RenderAsync(0, 8);
        _engine.Spoken.Clear();
        var again = await _rpc.RenderAsync(0, 8);

        // The same clips, and the engine was not asked for any of them.
        Assert.Equal(
            first.Clips.Select(c => c.Clip), again.Clips.Select(c => c.Clip));
        Assert.Empty(_engine.Spoken);
    }

    [Fact]
    public async Task Render_AfterTheWriterAsksForItAgain_MakesItAgain()
    {
        // Delivery is not reproducible: the same line asked for twice comes back
        // differently. Reuse is what makes a second listen instant, and this is
        // what stops it also making the reading fixed.
        var mira = await MiraAsync();
        await SceneAsync("<p>\"A,\" said Mira.</p>");
        await _rpc.PrepareAsync(StubEngine.Id);
        var designed = await KeptAsync(StubEngine.Id, mira.Id, "Low and level.");
        await new NarrationRpc(_workspace).SetVoiceAsync(null, designed.VoiceId);
        await _rpc.RenderAsync(0, 8);
        _engine.Spoken.Clear();

        Assert.True(_rpc.RenderAgain());
        await _rpc.RenderAsync(0, 8);

        Assert.NotEmpty(_engine.Spoken);
    }

    [Fact]
    public async Task Render_AfterTheVoiceIsRedesigned_DoesNotServeTheOldOne()
    {
        // A redesigned voice keeps the id it had - that is what makes it the
        // same character - so a cache keyed on the id alone would have gone on
        // serving every line in the voice the writer had just replaced.
        var mira = await MiraAsync();
        await SceneAsync("<p>\"A,\" said Mira.</p>");
        await _rpc.PrepareAsync(StubEngine.Id);
        var designed = await KeptAsync(StubEngine.Id, mira.Id, "Low and level.");
        await new NarrationRpc(_workspace).SetVoiceAsync(null, designed.VoiceId);
        await _rpc.RenderAsync(0, 8);
        _engine.Spoken.Clear();

        // The same voice id, different audio.
        var store = new VoiceStore(_workspace.Projects, _workspace.FileService);
        var voice = await store.GetAsync(designed.VoiceId!);
        await store.SaveAsync(voice!, [9, 9, 9, 9]);
        await _rpc.RenderAsync(0, 8);

        Assert.NotEmpty(_engine.Spoken);
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
    public async Task NarratorBrief_DescribesAnAcousticNarratorVoice()
    {
        var book = _workspace.Projects.ActiveBook!;
        book.NarrativePerson = "third limited";
        book.Tense = "past";
        await _workspace.Projects.SaveProjectAsync();

        var brief = _rpc.NarratorBrief();

        Assert.Contains("audiobook narrator", brief);
        Assert.Contains("natural timbre", brief);
        Assert.DoesNotContain("third limited", brief);
        Assert.DoesNotContain("past", brief);
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
    public async Task DesignNarrator_WithNothingTypedFallsBackToAStableAcousticBrief()
    {
        var book = _workspace.Projects.ActiveBook!;
        book.NarrativePerson = "first person";
        await _workspace.Projects.SaveProjectAsync();
        await _rpc.PrepareAsync(StubEngine.Id);

        await KeptNarratorAsync(StubEngine.Id, "   ");

        Assert.Contains("audiobook narrator", _engine.LastBrief!.Description);
        Assert.Contains("mid-range pitch", _engine.LastBrief.Description);
        Assert.DoesNotContain("first person", _engine.LastBrief.Description);
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
    public async Task AuditionLine_WithNoVoiceCastSaysSo()
    {
        // Nothing cast at all, so there is no voice to speak it in and no engine
        // to ask - which engine speaks a line is decided by the voice it is cast
        // in, and an uncast line names none.
        var chapter = await _workspace.Projects.CreateChapterAsync("One");
        var scene = await _workspace.Projects.CreateSceneAsync(chapter.Guid, "S");
        await _workspace.Projects.WriteSceneContentAsync(
            chapter, scene, "<p>She turned away.</p>");

        Assert.Equal(
            "no-voice", (await _rpc.AuditionLineAsync(chapter.Guid, scene.Id, "turned")).Error);
    }

    [Fact]
    public async Task AuditionLine_StartsTheEngineThatMadeTheVoiceRatherThanRefusing()
    {
        // Cast, installed, and never loaded in this process. Hearing one line
        // while you write it is the moment least able to afford "press Prepare
        // first" - and the engine is one model load from being able to answer.
        var (chapterGuid, sceneId) = await LineSceneAsync("<p>She turned away.</p>");

        var clip = await _rpc.AuditionLineAsync(chapterGuid, sceneId, "turned");

        Assert.Null(clip.Error);
        Assert.Equal(1, _engine.Prepared);
    }

    [Fact]
    public async Task AuditionLine_WithAVoiceWhoseEngineIsNotInstalledSaysSo()
    {
        var (chapterGuid, sceneId) = await LineSceneAsync("<p>She turned away.</p>");
        // Cast in a voice made by an engine this machine no longer has. Speaking
        // it in whatever engine happens to be loaded is the bug this replaces.
        await new VoiceStore(_workspace.Projects, _workspace.FileService).SaveAsync(
            new DesignedVoice(
                "elsewhere", "Elsewhere", string.Empty, "com.example.gone", "wav", 24000,
                DateTime.UtcNow.ToString("O")),
            [1, 2, 3]);
        await new VoiceCast(_workspace.Projects, _workspace.FileService)
            .SetVoiceAsync(null, "elsewhere");

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

    // ── Starting itself ──

    [Fact]
    public async Task AnInstalledEngine_StartsItselfRatherThanWaitingToBeAskedAgain()
    {
        // An engine's model lives in a process that dies with the app, so
        // "prepared" never survived a restart - and the button that fixed that
        // sits on a rail a writer has no reason to visit twice. The result was
        // an app that had a speech engine and read the book in the operating
        // system's voice every morning until somebody found the button again.
        Assert.False((await _rpc.ListAsync())[0].IsReady);

        // Whatever the first list reported, the start it kicked off is what the
        // reading waits for.
        var engine = await _rpc.RenderAsync(0, 1);

        Assert.Equal(1, _engine.Prepared);
        Assert.True((await _rpc.ListAsync())[0].IsReady);
        Assert.Equal(StubEngine.Id, engine.EngineId);
    }

    [Fact]
    public async Task AnEngineStartingItself_SaysSoInTheVeryAnswerThatStartedIt()
    {
        // The statuses are read a moment before the start is kicked off, so the
        // answer would otherwise report the state the call had just changed:
        // "not ready", with nothing moving and no reason for the cast rail ever
        // to ask again. It sat there saying so for the whole of a model load.
        _engine.Hold = new TaskCompletionSource();
        _engine.HoldPrepare = new TaskCompletionSource();

        var listed = Assert.Single(await _rpc.ListAsync());

        Assert.True(listed.IsPreparing);
        Assert.False(listed.IsReady);

        _engine.HoldPrepare.SetResult();
    }

    [Fact]
    public async Task AnEngineWithGigabytesLeftToFetch_IsNotStartedOnTheWritersBehalf()
    {
        _engine.DownloadBytes = 8L * 1024 * 1024 * 1024;

        await _rpc.ListAsync();
        await _rpc.ListAsync();

        // A download is a decision about somebody's connection, and looking at
        // a screen is not consent to spend it.
        Assert.Equal(0, _engine.Prepared);
    }

    [Fact]
    public async Task AnEngineThatCannotStart_IsNotRetriedOnEveryRefresh()
    {
        _engine.ThrowOnPrepare = true;

        await _rpc.ListAsync();
        await _rpc.ListAsync();
        await _rpc.ListAsync();

        // A model that fails to load fails the same way every time. Retrying it
        // per refresh spends a process start to learn what the last one said.
        Assert.Equal(1, _engine.Prepared);
    }

    [Fact]
    public async Task PreparingByHand_DoesNotAlsoStartTheEngineASecondTime()
    {
        await _rpc.PrepareAsync(StubEngine.Id);
        await _rpc.ListAsync();

        Assert.Equal(1, _engine.Prepared);
    }

    // ── The draw ──

    [Fact]
    public async Task Design_WithNoSeedAsksTheEngineForAFreshDraw()
    {
        // "I did not like that one, try again" is what the whole offer-and-keep
        // dialog is built around. The seed used to be derived from the words, so
        // pressing Design again on an unchanged brief returned the identical
        // voice and said nothing about it.
        var mira = await MiraAsync();
        await _rpc.PrepareAsync(StubEngine.Id);

        await _rpc.DesignAsync(StubEngine.Id, mira.Id, "a wiry voice");

        Assert.Null(_engine.LastBrief!.Seed);
    }

    [Fact]
    public async Task Design_WithASeedAsksForThatOneParticularVoice()
    {
        var mira = await MiraAsync();
        await _rpc.PrepareAsync(StubEngine.Id);

        await _rpc.DesignAsync(StubEngine.Id, mira.Id, "a wiry voice", seed: 4242);

        Assert.Equal(4242, _engine.LastBrief!.Seed);
    }

    [Fact]
    public async Task Design_ANegativeSeedIsTheInterfaceSayingSurpriseMe()
    {
        var mira = await MiraAsync();
        await _rpc.PrepareAsync(StubEngine.Id);

        await _rpc.DesignAsync(StubEngine.Id, mira.Id, "a wiry voice", seed: -1);

        Assert.Null(_engine.LastBrief!.Seed);
    }

    [Fact]
    public async Task Design_ReportsWhatItWasDrawnWith_AndKeepsIt()
    {
        // A voice heard once and not kept is otherwise gone: nothing anywhere
        // else remembers the draw.
        var mira = await MiraAsync();
        await _rpc.PrepareAsync(StubEngine.Id);
        _engine.SeedUsed = 99;

        var offered = await _rpc.DesignAsync(StubEngine.Id, mira.Id, "a wiry voice");
        Assert.Equal(99, offered.Seed);

        await _rpc.KeepVoiceAsync();

        Assert.Equal(99, Assert.Single(await _rpc.VoicesAsync()).Seed);
    }

    // ── Which engine speaks ──

    [Fact]
    public async Task Render_GoesToTheEngineThatMadeTheVoice_NotTheOneThatLoadedFirst()
    {
        // The bug this ends, reported from a real machine: a writer with a real
        // speech engine and the example tone generator installed heard their
        // whole book as a sine wave. The reading went to whichever engine had
        // finished loading, and the example loads instantly while a real one
        // takes half a minute - so the example always won, and nothing anywhere
        // said so, because the reading was doing exactly what it had been told.
        var other = new StubEngine(OtherEngineId);
        _workspace.ExtensionsHost.VoiceEngines.Insert(0, other);
        await _rpc.PrepareAsync(StubEngine.Id);
        await _rpc.PrepareAsync(OtherEngineId);

        // Cast in a voice the second engine made.
        var (chapterGuid, sceneId) = await LineSceneAsync("<p>She turned away.</p>");
        _ = (chapterGuid, sceneId);
        await new VoiceStore(_workspace.Projects, _workspace.FileService).SaveAsync(
            new DesignedVoice(
                "hers", "Hers", string.Empty, OtherEngineId, "wav", 16000,
                DateTime.UtcNow.ToString("O")),
            [1, 2, 3]);
        await new VoiceCast(_workspace.Projects, _workspace.FileService)
            .SetVoiceAsync(null, "hers");

        await _rpc.RenderAsync(0, 4);

        Assert.NotNull(other.LastRequest);
        Assert.Null(_engine.LastRequest);
    }

    [Fact]
    public async Task Render_TellsTheInterfaceWhichEngineSpoke()
    {
        var (_, _) = await LineSceneAsync("<p>She turned away.</p>");
        await _rpc.PrepareAsync(StubEngine.Id);

        var window = await _rpc.RenderAsync(0, 4);

        Assert.Equal(StubEngine.Id, window.EngineId);
        Assert.NotEmpty(window.Clips);
    }

    [Fact]
    public async Task Render_WithTheVoicesEngineUninstalled_FallsBackRatherThanUsingAnother()
    {
        // A project assembled on another machine, or an extension since removed.
        // Speaking those lines in whatever engine is loaded would be a reading
        // in the wrong voice that nothing reported.
        await _rpc.PrepareAsync(StubEngine.Id);
        var (_, _) = await LineSceneAsync("<p>She turned away.</p>");
        await new VoiceStore(_workspace.Projects, _workspace.FileService).SaveAsync(
            new DesignedVoice(
                "elsewhere", "Elsewhere", string.Empty, "com.example.gone", "wav", 16000,
                DateTime.UtcNow.ToString("O")),
            [1, 2, 3]);
        await new VoiceCast(_workspace.Projects, _workspace.FileService)
            .SetVoiceAsync(null, "elsewhere");

        var window = await _rpc.RenderAsync(0, 4);

        Assert.Empty(window.Clips);
    }

    [Fact]
    public async Task Render_ABookCastAcrossTwoEnginesComesBackInReadingOrder()
    {
        // Each engine speaks its own voices, and the interface plays the clips
        // in the order they arrive - so two engines rendering in turn have to be
        // merged back into the order of the book, or it is read in two passes.
        var other = new StubEngine(OtherEngineId);
        _workspace.ExtensionsHost.VoiceEngines.Add(other);
        await _rpc.PrepareAsync(StubEngine.Id);
        await _rpc.PrepareAsync(OtherEngineId);

        var mira = await MiraAsync();
        var chapter = await _workspace.Projects.CreateChapterAsync("One");
        var scene = await _workspace.Projects.CreateSceneAsync(chapter.Guid, "S");
        await _workspace.Projects.WriteSceneContentAsync(
            chapter, scene, "<p>She waited. \"You are late,\" said Mira. The wind took it.</p>");

        var store = new VoiceStore(_workspace.Projects, _workspace.FileService);
        await store.SaveAsync(
            new DesignedVoice(
                "narrator", "Narrator", string.Empty, StubEngine.Id, "wav", 16000,
                DateTime.UtcNow.ToString("O")),
            [1, 2, 3]);
        await store.SaveAsync(
            new DesignedVoice(
                "hers", "Hers", string.Empty, OtherEngineId, "wav", 16000,
                DateTime.UtcNow.ToString("O")),
            [4, 5, 6]);
        var cast = new VoiceCast(_workspace.Projects, _workspace.FileService);
        await cast.SetVoiceAsync(null, "narrator");
        await cast.SetVoiceAsync(mira.Id, "hers");

        var window = await _rpc.RenderAsync(0, 8);

        // Narration, her line, narration. Rendered engine by engine and left in
        // that order, her line would arrive last and the reading would play the
        // paragraph out of order - which sounds like the attribution is wrong.
        // The tag is its own piece of narration, so the paragraph is four: the
        // waiting, her line, "said Mira", and the wind.
        Assert.Equal(4, window.Clips.Length);
        var hers = Assert.Single(other.LastRequest!.Segments).Key;
        Assert.Equal(1, Array.FindIndex(window.Clips, c => c.Key == hers));
    }

    [Fact]
    public async Task Render_WithTheVoicesEngineStillToDownload_DoesNotFetchItToPlayALine()
    {
        var (_, _) = await LineSceneAsync("<p>She turned away.</p>");
        _engine.DownloadBytes = 8L * 1024 * 1024 * 1024;

        var window = await _rpc.RenderAsync(0, 4);

        Assert.Empty(window.Clips);
        Assert.Equal(0, _engine.Prepared);
    }

    [Fact]
    public async Task Render_WithAnEngineThatAlreadyFailedToStart_DoesNotKeepRetryingIt()
    {
        var (_, _) = await LineSceneAsync("<p>She turned away.</p>");
        _engine.ThrowOnPrepare = true;

        await _rpc.RenderAsync(0, 4);
        await _rpc.RenderAsync(0, 4);

        // A model that will not load will not load. Retrying it per window would
        // spend a process start on every paragraph of the reading.
        Assert.Equal(1, _engine.Prepared);
    }

    [Fact]
    public async Task Render_WithAnEngineThatCannotSayHowItIs_ReadsWithoutIt()
    {
        var (_, _) = await LineSceneAsync("<p>She turned away.</p>");
        _engine.ThrowOnStatus = true;

        Assert.Empty((await _rpc.RenderAsync(0, 4)).Clips);
    }

    [Fact]
    public async Task Render_AnEngineWithNothingToSayInThisWindowIsNotAsked()
    {
        // Mira has a voice from another engine and does not speak in this
        // window. Asking that engine for a window it has no lines in would
        // start a second model for nothing.
        var other = new StubEngine(OtherEngineId);
        _workspace.ExtensionsHost.VoiceEngines.Add(other);
        await _rpc.PrepareAsync(StubEngine.Id);
        await _rpc.PrepareAsync(OtherEngineId);

        var mira = await MiraAsync();
        var (_, _) = await LineSceneAsync("<p>She turned away.</p>");
        await new VoiceStore(_workspace.Projects, _workspace.FileService).SaveAsync(
            new DesignedVoice(
                "hers", "Hers", string.Empty, OtherEngineId, "wav", 16000,
                DateTime.UtcNow.ToString("O")),
            [4, 5, 6]);
        await new VoiceCast(_workspace.Projects, _workspace.FileService)
            .SetVoiceAsync(mira.Id, "hers");

        await _rpc.RenderAsync(0, 4);

        Assert.NotNull(_engine.LastRequest);
        Assert.Null(other.LastRequest);
    }

    [Fact]
    public async Task AuditionLine_CastInAVoiceThisMachineHasNoAudioFor_SaysSo()
    {
        // The cast file is in the project and travels through Git; the audio is
        // large and may not have come with it. Those lines have a voice on paper
        // and none this machine can speak.
        var (chapterGuid, sceneId) = await LineSceneAsync("<p>She turned away.</p>");
        await _rpc.PrepareAsync(StubEngine.Id);
        var wav = Path.Combine(
            _workspace.Projects.ProjectRoot!, ".novalist", "narration", "voices", "narrator.wav");
        File.Delete(wav);

        Assert.Equal(
            "no-voice", (await _rpc.AuditionLineAsync(chapterGuid, sceneId, "turned")).Error);
    }

    // ── Voices for one stretch of the book ──

    [Fact]
    public async Task AVoiceDesignedForOneAct_DoesNotOverwriteHowTheySoundElsewhere()
    {
        var mira = await MiraAsync();
        await _rpc.PrepareAsync(StubEngine.Id);

        var standing = await _rpc.DesignAsync(StubEngine.Id, mira.Id, "a wiry voice");
        await _rpc.KeepVoiceAsync();
        var older = await _rpc.DesignAsync(
            StubEngine.Id, mira.Id, "a wiry voice", false, act: "Two");
        await _rpc.KeepVoiceAsync();

        // The bug this replaces: the second design reused the first's id, so
        // asking for an older Mira in Act Two silently destroyed how she sounded
        // in Act One and there was no way back to it.
        Assert.NotEqual(standing.VoiceId, older.VoiceId);
        Assert.Equal(2, (await _rpc.VoicesAsync()).Length);
    }

    [Fact]
    public async Task AVoiceDesignedForOneAct_IsCastOverThatActAndNotOverTheBook()
    {
        var mira = await MiraAsync();
        await _rpc.PrepareAsync(StubEngine.Id);
        await _rpc.DesignAsync(StubEngine.Id, mira.Id, "a wiry voice");
        await _rpc.KeepVoiceAsync();

        var older = await _rpc.DesignAsync(
            StubEngine.Id, mira.Id, "a wiry voice", false, act: "Two");
        await _rpc.KeepVoiceAsync();

        var cast = new VoiceCast(_workspace.Projects, _workspace.FileService);
        var sheet = await cast.ReadAsync();
        Assert.Equal(older.VoiceId, Assert.Single(sheet.Overrides).VoiceId);
        // And the standing voice is untouched, which is the whole point.
        Assert.NotEqual(older.VoiceId, sheet.Voices[mira.Id]);
    }

    [Fact]
    public async Task TheSameStretchDesignedTwice_ReplacesItRatherThanStacking()
    {
        var mira = await MiraAsync();
        await _rpc.PrepareAsync(StubEngine.Id);

        await _rpc.DesignAsync(StubEngine.Id, mira.Id, "a wiry voice", false, act: "Two");
        await _rpc.KeepVoiceAsync();
        await _rpc.DesignAsync(StubEngine.Id, mira.Id, "a wiry voice", false, act: "Two");
        await _rpc.KeepVoiceAsync();

        var cast = new VoiceCast(_workspace.Projects, _workspace.FileService);
        Assert.Single((await cast.ReadAsync()).Overrides);
    }

    [Fact]
    public async Task ForgettingAVoice_AlsoDropsTheStretchesItWasCastOver()
    {
        var mira = await MiraAsync();
        await _rpc.PrepareAsync(StubEngine.Id);
        var older = await _rpc.DesignAsync(
            StubEngine.Id, mira.Id, "a wiry voice", false, act: "Two");
        await _rpc.KeepVoiceAsync();

        await _rpc.ForgetAsync(older.VoiceId!);

        // A scope pointing at a voice that no longer exists is worse than a
        // stale standing cast: it wins over the character's real voice, so those
        // chapters fall silently back to the narrator while the rest is right.
        var cast = new VoiceCast(_workspace.Projects, _workspace.FileService);
        Assert.Empty((await cast.ReadAsync()).Overrides);
    }

    /// <summary>A second engine id, so a test can have two installed and say
    /// which of them spoke.</summary>
    private const string OtherEngineId = "com.example.stub.other";

    private sealed class StubEngine : IExtension, IVoiceEngineContributor
    {
        public const string Id = "com.example.stub";

        /// <summary>Which engine this one is, so a test can install two and say
        /// which of them spoke.</summary>
        private readonly string _id;

        public StubEngine(string id = Id) => _id = id;

        public bool CanDesign { get; set; } = true;
        public bool ThrowOnStatus { get; set; }
        public bool ThrowOnPrepare { get; set; }
        public bool ThrowOnDesign { get; set; }
        public bool ThrowOnForget { get; set; }

        /// <summary>What the engine says about itself, which is where a reason a
        /// writer can act on belongs.</summary>
        public string? StatusError { get; set; }
        public bool RefuseRender { get; set; }

        /// <summary>Gigabytes still to fetch. What tells an engine that is one
        /// model load from ready from one that is a download away, which is the
        /// difference between starting it unasked and never doing so.</summary>
        public long? DownloadBytes { get; set; }

        /// <summary>How many times it has been asked to get ready.</summary>
        public int Prepared { get; private set; }

        /// <summary>What this engine reports it drew with.</summary>
        public int? SeedUsed { get; set; }

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

        /// <summary>Every line this engine was actually asked to speak, so a
        /// test can say what was made again and what came off the disk.</summary>
        public List<string> Spoken { get; } = [];
        public string? Forgotten { get; private set; }

        private bool _ready;

        string IExtension.Id => _id;
        public string DisplayName => "Stub";
        public string Description => "A voice engine that speaks nothing.";
        public string Version => "1.0";
        public string Author => "Tests";
        public void Initialize(IHostServices host) { }
        public void Shutdown() { }

        public string EngineId => _id;
        public string EngineName => "Stub";

        public VoiceEngineFeatures Features =>
            (CanDesign ? VoiceEngineFeatures.DesignFromDescription : VoiceEngineFeatures.None)
            | VoiceEngineFeatures.EmotionVector;

        public Task<VoiceEngineStatus> GetStatusAsync(CancellationToken cancellationToken = default)
            => ThrowOnStatus
                ? throw new InvalidOperationException("no")
                : Task.FromResult(new VoiceEngineStatus
                {
                    IsReady = _ready,
                    Error = StatusError,
                    DownloadBytes = DownloadBytes
                });

        public Task PrepareAsync(
            IProgress<VoiceEnginePrepare>? progress = null,
            CancellationToken cancellationToken = default)
        {
            Prepared++;
            if (ThrowOnPrepare)
                throw new InvalidOperationException("no");
            if (HoldPrepare is { } wait)
                return wait.Task.ContinueWith(_ => _ready = true, TaskScheduler.Default);
            _ready = true;
            return Task.CompletedTask;
        }

        /// <summary>Held so a test can look at an engine while it is genuinely
        /// still loading rather than after it has finished.</summary>
        public TaskCompletionSource? HoldPrepare { get; set; }

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
                ReferenceText = brief.SampleLines.FirstOrDefault() ?? "This is the reference.",
                SampleRate = 16000,
                ResolvedDescription = brief.Description,
                Seed = SeedUsed
            });
        }

        public async IAsyncEnumerable<NarrationClip> RenderAsync(
            NarrationRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            foreach (var segment in request.Segments)
                Spoken.Add(segment.Key);
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
