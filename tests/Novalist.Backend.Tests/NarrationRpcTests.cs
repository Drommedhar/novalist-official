using Novalist.Backend;
using Novalist.Backend.Rpc;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Core.Utilities;
using Xunit;

namespace Novalist.Backend.Tests;

/// <summary>
/// Covers what the Narration view asks the backend for: the book's cast, the
/// book itself as prose marked up with the reading, and the two corrections the
/// writer can make while listening.
/// </summary>
public sealed class NarrationRpcTests : IDisposable
{
    private readonly string _root;
    private readonly Workspace _workspace;
    private readonly NarrationRpc _rpc;

    public NarrationRpcTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "nl-nar-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _workspace = new Workspace(Path.Combine(_root, "settings"));
        _workspace.Projects.CreateProjectAsync(_root, "NarNovel", "Book").GetAwaiter().GetResult();
        _workspace.OpenProjectAsync(_workspace.Projects.ProjectRoot!).GetAwaiter().GetResult();
        _rpc = new NarrationRpc(_workspace);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private EntityService Entities => new(_workspace.Projects);

    private async Task<(ChapterData Chapter, SceneData Scene)> SceneAsync(
        string chapterTitle, string sceneTitle, string html)
    {
        var chapter = await _workspace.Projects.CreateChapterAsync(chapterTitle);
        var scene = await _workspace.Projects.CreateSceneAsync(chapter.Guid, sceneTitle);
        await _workspace.Projects.WriteSceneContentAsync(chapter, scene, html);
        return (chapter, scene);
    }

    private async Task<(string Aldric, string Mira)> CastAsync()
    {
        var aldric = new CharacterData { Name = "Aldric", Surname = "Vane" };
        var mira = new CharacterData { Name = "Mira" };
        await Entities.SaveCharacterAsync(aldric);
        await Entities.SaveCharacterAsync(mira);
        return (aldric.Id, mira.Id);
    }

    /// <summary>The one scene the test wrote, read back through the book -
    /// which is the only way in now that the reading is book-shaped.</summary>
    private async Task<NarrationProseSceneDto> ReadSceneAsync()
        => Assert.Single(Assert.Single((await _rpc.BookAsync()).Chapters).Scenes);

    // ── narration/cast ──

    [Fact]
    public async Task Cast_EmptyBookHasNobodyInIt()
    {
        var cast = await _rpc.CastAsync();

        Assert.Empty(cast.Members);
        Assert.Null(cast.NarratorVoiceId);
        Assert.Equal(0, cast.UnassignedCount);
    }

    [Fact]
    public async Task Cast_ListsWhoSpeaksBusiestFirstWithTheirVoice()
    {
        var (aldric, mira) = await CastAsync();
        await SceneAsync("One", "S",
            "<p>\"A,\" said Aldric.</p><p>\"B,\" said Aldric.</p><p>\"C,\" said Mira.</p>");
        await _rpc.SetVoiceAsync(aldric, "aldric-voice");

        var cast = await _rpc.CastAsync();

        Assert.Equal([aldric, mira], cast.Members.Select(m => m.CharacterId));
        Assert.Equal("Aldric Vane", cast.Members[0].Name);
        Assert.Equal(2, cast.Members[0].LineCount);
        Assert.Equal("aldric-voice", cast.Members[0].VoiceId);
        // Uncast, which means the narrator reads them rather than nobody.
        Assert.Null(cast.Members[1].VoiceId);
    }

    [Fact]
    public async Task Cast_CountsTheLinesNobodyCouldBeTracedTo()
    {
        await CastAsync();
        await SceneAsync("One", "S", "<p>\"Nobody asked you.\"</p>");

        var cast = await _rpc.CastAsync();

        Assert.Equal(1, cast.UnassignedCount);
        Assert.Empty(cast.Members);
    }

    [Fact]
    public async Task Cast_ReportsTheNarratorSeparatelyFromTheCharacters()
    {
        var (aldric, _) = await CastAsync();
        await SceneAsync("One", "S", "<p>\"A,\" said Aldric.</p>");
        await _rpc.SetVoiceAsync(null, "narrator-voice");

        var cast = await _rpc.CastAsync();

        Assert.Equal("narrator-voice", cast.NarratorVoiceId);
        Assert.DoesNotContain("narrator-voice", cast.Members.Select(m => m.VoiceId));
        Assert.Equal([aldric], cast.Members.Select(m => m.CharacterId));
    }

    // ── narration/book ──

    [Fact]
    public async Task Book_AnEmptyBookHasNothingToRead()
    {
        var read = await _rpc.BookAsync();

        Assert.Empty(read.Chapters);
        Assert.Equal(0, read.SpokenCount);
    }

    [Fact]
    public async Task Book_WithNoProjectOpenHasNothingToRead()
    {
        var closed = new NarrationRpc(new Workspace(Path.Combine(_root, "settings-book")));

        Assert.Empty((await closed.BookAsync()).Chapters);
    }

    [Fact]
    public async Task Book_WalksEveryChapterInOrder()
    {
        await CastAsync();
        await SceneAsync("One", "First", "<p>\"A,\" said Mira.</p>");
        await SceneAsync("Two", "Second", "<p>\"B,\" said Aldric.</p>");

        var read = await _rpc.BookAsync();

        Assert.Equal(["One", "Two"], read.Chapters.Select(c => c.Title));
        Assert.Equal(
            ["First", "Second"],
            read.Chapters.SelectMany(c => c.Scenes).Select(s => s.SceneTitle));
        Assert.Equal(2, read.SpokenCount);
    }

    [Fact]
    public async Task Book_LeavesOutAChapterWithNoScenes()
    {
        await CastAsync();
        await _workspace.Projects.CreateChapterAsync("Empty");
        await SceneAsync("One", "First", "<p>\"A,\" said Mira.</p>");

        Assert.Equal(["One"], (await _rpc.BookAsync()).Chapters.Select(c => c.Title));
    }

    [Fact]
    public async Task Book_MarksTheProseUpWhereItStands()
    {
        await CastAsync();
        await SceneAsync("One", "S", "<p>She waited. \"You are late,\" Mira snapped.</p>");

        var scene = Assert.Single(Assert.Single((await _rpc.BookAsync()).Chapters).Scenes);

        // The writer's own HTML with a marker round each segment, rather than
        // the segments lifted out of it.
        Assert.Contains("<p>", scene.Html);
        foreach (var segment in scene.Segments)
            Assert.Contains("data-nl-seg=\"" + segment.Key + "\"", scene.Html);
    }

    [Fact]
    public async Task Book_InterleavesNarrationAndDialogueInReadingOrder()
    {
        await CastAsync();
        await SceneAsync("Seven", "The harbour wall",
            "<p>She had been on the wall since the tide turned. \"You are late,\" " +
            "Mira snapped, not turning round.</p>");

        var chapter = Assert.Single((await _rpc.BookAsync()).Chapters);
        var scene = Assert.Single(chapter.Scenes);

        Assert.Equal("Seven", chapter.Title);
        Assert.Equal("The harbour wall", scene.SceneTitle);
        Assert.Equal(
            [
                nameof(NarrationSegmentKind.Narration),
                nameof(NarrationSegmentKind.Dialogue),
                nameof(NarrationSegmentKind.Narration)
            ],
            scene.Segments.Select(s => s.Kind));
        Assert.Equal(Enumerable.Range(0, 3), scene.Segments.Select(s => s.Index));
        Assert.Equal("Mira", scene.Segments[1].SpeakerName);
        Assert.Equal("High", scene.Segments[1].Confidence);
        // The tag is the narrator's, and it is not read in Mira's voice.
        Assert.Null(scene.Segments[2].SpeakerId);
    }

    [Fact]
    public async Task Book_ResolvesEachSegmentToTheVoiceItIsReadIn()
    {
        var (_, mira) = await CastAsync();
        await SceneAsync("One", "S", "<p>The tide turned. \"You are late,\" said Mira.</p>");
        await _rpc.SetVoiceAsync(null, "narrator-voice");
        await _rpc.SetVoiceAsync(mira, "mira-voice");

        var scene = Assert.Single(Assert.Single((await _rpc.BookAsync()).Chapters).Scenes);

        Assert.Equal("narrator-voice", scene.Segments[0].VoiceId);
        Assert.Equal("mira-voice", scene.Segments[1].VoiceId);
    }

    [Fact]
    public async Task Book_AnUncastCharacterIsReadByTheNarrator()
    {
        await CastAsync();
        await SceneAsync("One", "S", "<p>\"You are late,\" said Mira.</p>");
        await _rpc.SetVoiceAsync(null, "narrator-voice");

        var scene = Assert.Single(Assert.Single((await _rpc.BookAsync()).Chapters).Scenes);

        // Mira has lines and no voice, so the whole scene is the narrator's -
        // a complete reading in the wrong voice, not one with holes in it.
        Assert.All(scene.Segments, s => Assert.Equal("narrator-voice", s.VoiceId));
        Assert.Contains(scene.Segments, s => s.SpeakerId != null);
    }

    [Fact]
    public async Task Book_CarriesTheCandidatesTheWriterCanPickFrom()
    {
        await CastAsync();
        await SceneAsync("One", "S",
            "<p>\"A,\" said Mira.</p><p>\"B,\" said Aldric.</p><p>\"C.\"</p>");

        var scene = Assert.Single(Assert.Single((await _rpc.BookAsync()).Chapters).Scenes);

        var guessed = scene.Segments.Last(s => s.Kind == nameof(NarrationSegmentKind.Dialogue));
        Assert.Equal("Low", guessed.Confidence);
        Assert.NotEmpty(guessed.Candidates);
        Assert.All(guessed.Candidates, c => Assert.NotEqual(c.CharacterId, c.Name));
        Assert.Equal(100, guessed.Candidates.Sum(c => c.Percent));
    }

    [Fact]
    public async Task Book_DirectsALineFromItsSpeechVerbAndSaysWhy()
    {
        await CastAsync();
        await SceneAsync("One", "S", "<p>\"You are late,\" Mira snapped.</p>");

        var scene = Assert.Single(Assert.Single((await _rpc.BookAsync()).Chapters).Scenes);

        Assert.Equal("angry", scene.Segments[0].DirectionKey);
        Assert.Equal(nameof(DirectionSource.Verb), scene.Segments[0].DirectionSource);
        Assert.Equal("snapped", scene.Segments[0].DirectionEvidence);
    }

    [Fact]
    public async Task Book_ReportsTheScenesOwnEmotionAndIntensity()
    {
        await CastAsync();
        var (chapter, scene) = await SceneAsync("One", "S", "<p>\"Go,\" said Mira.</p>");
        scene.AnalysisOverrides = new SceneAnalysisOverrides { Emotion = "tense", Intensity = 7 };
        await _workspace.Projects.SaveScenesAsync();

        var read = Assert.Single(Assert.Single((await _rpc.BookAsync()).Chapters).Scenes);

        Assert.Equal(chapter.Guid, read.ChapterGuid);
        Assert.Equal("tense", read.SceneEmotion);
        Assert.Equal(7, read.SceneIntensity);
        Assert.Equal("tense", read.Segments[0].DirectionKey);
        Assert.Equal(nameof(DirectionSource.Scene), read.Segments[0].DirectionSource);
    }

    [Fact]
    public async Task Book_UsesTheProjectWritingLanguageOverride()
    {
        var german = new CharacterData { Name = "Aldric" };
        await Entities.SaveCharacterAsync(german);
        var (chapter, scene) = await SceneAsync("Eins", "S",
            "<p>„Du bist zu spät“, flüsterte Aldric.</p>");

        _workspace.Projects.ProjectSettings.Overrides ??= new SettingsOverrides();
        _workspace.Projects.ProjectSettings.Overrides.AutoReplacementLanguage = "de";
        var spoken = (await ReadSceneAsync()).Segments[0];

        // "flüsterte" is only a speech verb, and only carries an emotion, via
        // the German lexicon.
        Assert.Equal(german.Id, spoken.SpeakerId);
        Assert.Equal("peaceful", spoken.DirectionKey);
        Assert.Equal("flüsterte", spoken.DirectionEvidence);
    }

    // ── narration/setVoice ──

    [Fact]
    public async Task SetVoice_CastsAndUnCastsACharacter()
    {
        var (aldric, _) = await CastAsync();
        await SceneAsync("One", "S", "<p>\"A,\" said Aldric.</p>");

        Assert.True(await _rpc.SetVoiceAsync(aldric, "aldric-voice"));
        Assert.Equal("aldric-voice", (await _rpc.CastAsync()).Members[0].VoiceId);

        Assert.True(await _rpc.SetVoiceAsync(aldric, null));
        Assert.Null((await _rpc.CastAsync()).Members[0].VoiceId);
    }

    [Fact]
    public async Task SetVoice_ABlankCharacterMeansTheNarrator()
    {
        Assert.True(await _rpc.SetVoiceAsync("   ", "narrator-voice"));

        Assert.Equal("narrator-voice", (await _rpc.CastAsync()).NarratorVoiceId);
    }

    [Fact]
    public async Task SetVoice_WithNoProjectOpenIsRefused()
    {
        var closed = new NarrationRpc(new Workspace(Path.Combine(_root, "settings2")));

        Assert.False(await closed.SetVoiceAsync(null, "narrator-voice"));
    }

    // ── narration/setDirection ──

    [Fact]
    public async Task SetDirection_DirectsOneSegmentAndKeepsIt()
    {
        await CastAsync();
        const string html = "<p>\"You are late,\" said Mira.</p>";
        var (chapter, scene) = await SceneAsync("One", "S", html);
        var key = DialogueScanner.Scan(html)[0].LineKey;

        Assert.True(await _rpc.SetDirectionAsync(chapter.Guid, scene.Id, key, "sorrowful"));

        var read = await ReadSceneAsync();
        Assert.Equal("sorrowful", read.Segments[0].DirectionKey);
        Assert.Equal(nameof(DirectionSource.Writer), read.Segments[0].DirectionSource);
    }

    [Fact]
    public async Task SetDirection_AnEmptyKeyAsksForTheLineToBeReadPlainly()
    {
        // Stored, because it is a decision. Letting it fall back to the scene's
        // emotion would quietly undo the writer.
        await CastAsync();
        const string html = "<p>\"You are late,\" Mira snapped.</p>";
        var (chapter, scene) = await SceneAsync("One", "S", html);
        var key = DialogueScanner.Scan(html)[0].LineKey;

        Assert.True(await _rpc.SetDirectionAsync(chapter.Guid, scene.Id, key, ""));

        var read = await ReadSceneAsync();
        Assert.Equal("neutral", read.Segments[0].DirectionKey);
        Assert.Equal(nameof(DirectionSource.Writer), read.Segments[0].DirectionSource);
    }

    [Fact]
    public async Task SetDirection_NullHandsTheSegmentBackToTheProse()
    {
        await CastAsync();
        const string html = "<p>\"You are late,\" Mira snapped.</p>";
        var (chapter, scene) = await SceneAsync("One", "S", html);
        var key = DialogueScanner.Scan(html)[0].LineKey;
        await _rpc.SetDirectionAsync(chapter.Guid, scene.Id, key, "joyful");

        Assert.True(await _rpc.SetDirectionAsync(chapter.Guid, scene.Id, key, null));

        var read = await ReadSceneAsync();
        Assert.Equal("angry", read.Segments[0].DirectionKey);
        Assert.Equal(nameof(DirectionSource.Verb), read.Segments[0].DirectionSource);
        Assert.Null(_workspace.ResolveScene(chapter.Guid, scene.Id).scene.DialogueDirections);
    }

    [Fact]
    public async Task SetDirection_ClearingOneThatWasNeverSetChangesNothing()
    {
        await CastAsync();
        var (chapter, scene) = await SceneAsync("One", "S", "<p>\"A,\" said Mira.</p>");

        Assert.False(await _rpc.SetDirectionAsync(chapter.Guid, scene.Id, "n:deadbeef:0", null));
    }

    [Fact]
    public async Task SetDirection_ClearingOneOfSeveralKeepsTheRest()
    {
        await CastAsync();
        const string html = "<p>The tide turned. \"You are late,\" Mira snapped.</p>";
        var (chapter, scene) = await SceneAsync("One", "S", html);
        var spoken = DialogueScanner.Scan(html)[0].LineKey;
        var prose = (await ReadSceneAsync()).Segments
            .First(s => s.Kind == nameof(NarrationSegmentKind.Narration)).Key;
        await _rpc.SetDirectionAsync(chapter.Guid, scene.Id, spoken, "joyful");
        await _rpc.SetDirectionAsync(chapter.Guid, scene.Id, prose, "somber");

        Assert.True(await _rpc.SetDirectionAsync(chapter.Guid, scene.Id, spoken, null));

        var directions = _workspace.ResolveScene(chapter.Guid, scene.Id).scene.DialogueDirections;
        Assert.NotNull(directions);
        Assert.Equal([prose], directions!.Keys);
    }

    [Fact]
    public async Task SetDirection_DirectsANarrationRunAsWellAsASpokenLine()
    {
        await CastAsync();
        const string html = "<p>The tide turned. \"You are late,\" said Mira.</p>";
        var (chapter, scene) = await SceneAsync("One", "S", html);
        var prose = (await ReadSceneAsync()).Segments
            .First(s => s.Kind == nameof(NarrationSegmentKind.Narration)).Key;

        Assert.True(await _rpc.SetDirectionAsync(chapter.Guid, scene.Id, prose, "melancholic"));

        Assert.Equal("melancholic", (await ReadSceneAsync()).Segments[0].DirectionKey);
    }

    [Fact]
    public async Task SetDirection_AMissingSceneIsRefused()
    {
        Assert.False(await _rpc.SetDirectionAsync("no-such-chapter", "no-such-scene", "k", "angry"));
    }

    // ── narration/emotions ──

    [Fact]
    public void Emotions_OffersTheWritingLanguagesOwnKeys()
    {
        _workspace.Settings.Settings.AutoReplacementLanguage = "en";

        var emotions = _rpc.Emotions();

        Assert.Equal(SceneAnalysisLexicon.For("en")!.EmotionKeys, emotions);
        Assert.Contains("neutral", emotions);
    }

    [Fact]
    public void Emotions_ALanguageWithNoLexiconOffersNoPicker()
    {
        // Rather than an English one, which would be a picker the writer's own
        // prose cannot be directed by.
        _workspace.Settings.Settings.AutoReplacementLanguage = "fr";

        Assert.Empty(_rpc.Emotions());
    }

    // ── directing by hand ──

    [Fact]
    public async Task SetDirection_ASliderPushedByHandIsWhatGetsPerformed()
    {
        var (chapter, scene) = await SceneAsync("One", "S", "<p>She turned away.</p>");
        var segment = (await ReadSceneAsync()).Segments[0];

        Assert.True(await _rpc.SetDirectionAsync(
            chapter.Guid, scene.Id, segment.Key, null,
            new Dictionary<string, double> { ["happy"] = 0.8, ["surprised"] = 0.3 }));

        var directed = (await ReadSceneAsync()).Segments[0];
        Assert.Equal("Writer", directed.DirectionSource);
        Assert.Equal(0.8, directed.DirectionVector["happy"], 3);
        Assert.Equal(0.3, directed.DirectionVector["surprised"], 3);
    }

    [Fact]
    public async Task SetDirections_OneArgumentIsDirectedOnce()
    {
        var (chapter, scene) = await SceneAsync(
            "One", "S",
            "<p>\u201cGet out,\u201d she said. \u201cI mean it,\u201d she said. \u201cNow,\u201d she said.</p>");
        var keys = (await ReadSceneAsync()).Segments.Select(s => s.Key).ToArray();

        Assert.True(await _rpc.SetDirectionsAsync(chapter.Guid, scene.Id, keys, "angry"));

        var directed = await ReadSceneAsync();
        Assert.All(directed.Segments, s => Assert.Equal("angry", s.DirectionKey));
        Assert.All(directed.Segments, s => Assert.Equal("Writer", s.DirectionSource));
    }

    [Fact]
    public async Task SetDirections_WithNoLinesNamedChangesNothing()
    {
        var (chapter, scene) = await SceneAsync("One", "S", "<p>She turned away.</p>");

        Assert.False(await _rpc.SetDirectionsAsync(chapter.Guid, scene.Id, [], "angry"));
    }

    [Fact]
    public async Task SetDirections_InASceneThatIsNotThereIsRefused()
        => Assert.False(await _rpc.SetDirectionsAsync("nope", "nope", ["k"], "angry"));

    [Fact]
    public async Task SetDirection_ClearingHandsTheLineBackToTheProse()
    {
        var (chapter, scene) = await SceneAsync("One", "S", "<p>She turned away.</p>");
        var segment = (await ReadSceneAsync()).Segments[0];
        await _rpc.SetDirectionAsync(chapter.Guid, scene.Id, segment.Key, "angry");

        Assert.True(await _rpc.SetDirectionAsync(chapter.Guid, scene.Id, segment.Key, null));

        Assert.NotEqual("Writer", (await ReadSceneAsync()).Segments[0].DirectionSource);
    }

    [Fact]
    public async Task SetDirection_ClearingWhatWasNeverSetChangesNothing()
    {
        var (chapter, scene) = await SceneAsync("One", "S", "<p>She turned away.</p>");
        var segment = (await ReadSceneAsync()).Segments[0];

        Assert.False(await _rpc.SetDirectionAsync(chapter.Guid, scene.Id, segment.Key, null));
    }

    [Fact]
    public async Task SetDirection_AClipCanBePointedAtInsteadOfDescribed()
    {
        var (chapter, scene) = await SceneAsync("One", "S", "<p>She turned away.</p>");
        var segment = (await ReadSceneAsync()).Segments[0];

        Assert.True(await _rpc.SetDirectionAsync(
            chapter.Guid, scene.Id, segment.Key, null, null, "a1b2c3.wav"));

        Assert.Equal("a1b2c3.wav", (await ReadSceneAsync()).Segments[0].DirectionClip);
    }

    // ── a standing register ──

    [Fact]
    public async Task SetRegister_ReachesEveryLineTheCharacterSpeaks()
    {
        var (aldric, mira) = await CastAsync();
        await SceneAsync("One", "S", "<p>\u201cGet out,\u201d Mira said.</p>");

        // A dimension the line does not already carry, so the assertion is
        // about the register arriving rather than about arithmetic.
        Assert.True(await _rpc.SetRegisterAsync(
            mira, new Dictionary<string, double> { ["melancholic"] = 0.2 }));

        var spoken = (await ReadSceneAsync()).Segments
            .First(s => s.Kind == "Dialogue");
        Assert.Equal(0.2, spoken.DirectionVector["melancholic"], 3);
        Assert.NotEqual(aldric, spoken.SpeakerId);
    }

    [Fact]
    public async Task SetRegister_TheNarratorHasOneToo()
    {
        await SceneAsync("One", "S", "<p>She turned away.</p>");

        Assert.True(await _rpc.SetRegisterAsync(
            null, new Dictionary<string, double> { ["melancholic"] = 0.3 }));

        Assert.Equal(
            0.3, (await ReadSceneAsync()).Segments[0].DirectionVector["melancholic"], 3);
    }

    [Fact]
    public async Task SetRegister_ClearingRemovesIt()
    {
        var (_, mira) = await CastAsync();
        await _rpc.SetRegisterAsync(mira, new Dictionary<string, double> { ["calm"] = 0.2 });

        Assert.True(await _rpc.SetRegisterAsync(mira, null));

        Assert.Empty(await _rpc.RegistersAsync());
    }

    [Fact]
    public async Task SetRegister_ClearingTheNarratorsRemovesIt()
    {
        await _rpc.SetRegisterAsync(null, new Dictionary<string, double> { ["calm"] = 0.2 });

        Assert.True(await _rpc.SetRegisterAsync(null, null));

        Assert.Empty(await _rpc.RegistersAsync());
    }

    [Fact]
    public async Task SetRegister_ADimensionNoEngineTakesIsDropped()
    {
        var (_, mira) = await CastAsync();

        await _rpc.SetRegisterAsync(
            mira, new Dictionary<string, double> { ["smug"] = 0.5, ["calm"] = 0.2 });

        Assert.Equal(["calm"], (await _rpc.RegistersAsync())[mira].Keys);
    }

    [Fact]
    public async Task SetRegister_AllZeroesIsNoRegisterAtAll()
    {
        var (_, mira) = await CastAsync();

        await _rpc.SetRegisterAsync(mira, new Dictionary<string, double> { ["calm"] = 0 });

        Assert.Empty(await _rpc.RegistersAsync());
    }

    [Fact]
    public async Task Registers_ReportTheNarratorUnderTheEmptyKey()
    {
        await _rpc.SetRegisterAsync(null, new Dictionary<string, double> { ["calm"] = 0.2 });

        Assert.True((await _rpc.RegistersAsync()).ContainsKey(string.Empty));
    }

    [Fact]
    public void Dimensions_AreTheOnesAnEngineActuallyTakes()
        => Assert.Equal(EmotionDirector.Dimensions, _rpc.Dimensions());


}
