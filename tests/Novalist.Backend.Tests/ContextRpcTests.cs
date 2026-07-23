using System.Text;
using Novalist.Backend;
using Novalist.Backend.Rpc;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Xunit;

namespace Novalist.Backend.Tests;

public sealed class ContextRpcTests : IDisposable
{
    private readonly string _root;
    private readonly Workspace _workspace;
    private readonly ContextRpc _rpc;

    public ContextRpcTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "nl-ctx-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _workspace = new Workspace(Path.Combine(_root, "settings"));
        _workspace.Projects.CreateProjectAsync(_root, "CtxNovel", "Book").GetAwaiter().GetResult();
        _workspace.OpenProjectAsync(_workspace.Projects.ProjectRoot!).GetAwaiter().GetResult();
        _rpc = new ContextRpc(_workspace);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private EntityService Entities => new(_workspace.Projects);

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("en", true)]
    [InlineData("en-GB", true)]
    [InlineData("de", true)]
    [InlineData("zh-CN", true)]
    [InlineData("fr", false)]     // no lexicon ships for French yet
    public void SupportsKeywordAnalysis_MatchesTheShippedLexicons(string? language, bool expected)
        => Assert.Equal(expected, ContextRpc.SupportsKeywordAnalysis(language));

    [Fact]
    public async Task Analyze_GermanProject_UsesTheGermanLexicon()
    {
        var chapter = await _workspace.Projects.CreateChapterAsync("K");
        var scene = await _workspace.Projects.CreateSceneAsync(chapter.Guid, "S");
        // German prose: English keyword lists would score this at nothing.
        await Write(chapter, scene,
            "<p>Angst und Panik erfüllten sie. Der Kampf drohte, und sie wollte fliehen.</p>");

        _workspace.Settings.Settings.AutoReplacementLanguage = "de";
        var analysis = (await _rpc.AnalyzeAsync(chapter.Guid, scene.Id)).Analysis;

        Assert.True(analysis.KeywordAnalysisSupported);
        Assert.True(analysis.Intensity < 0, "German negative/conflict words should push intensity down.");
        Assert.Equal("fearful", analysis.Emotion);
        Assert.NotEqual(string.Empty, analysis.Conflict);
        // The lexicon drives the dropdown, so the full key set is still offered.
        Assert.Contains("triumphant", analysis.EmotionKeys);
    }

    [Fact]
    public async Task Analyze_ChineseProject_UsesTheChineseLexicon()
    {
        var chapter = await _workspace.Projects.CreateChapterAsync("K");
        var scene = await _workspace.Projects.CreateSceneAsync(chapter.Guid, "S");
        await Write(chapter, scene, "<p>她感到恐惧和恐慌。战斗迫近，她只想逃跑。</p>");

        _workspace.Settings.Settings.AutoReplacementLanguage = "zh-CN";
        var analysis = (await _rpc.AnalyzeAsync(chapter.Guid, scene.Id)).Analysis;

        Assert.True(analysis.KeywordAnalysisSupported);
        Assert.Equal("fearful", analysis.Emotion);
        Assert.True(analysis.Intensity < 0);
    }

    [Fact]
    public async Task Analyze_LanguageWithoutLexicon_SkipsKeywordDerivedValues()
    {
        var chapter = await _workspace.Projects.CreateChapterAsync("K");
        var scene = await _workspace.Projects.CreateSceneAsync(chapter.Guid, "S");
        await Write(chapter, scene, "<p>She felt hope and joy, but the fight and betrayal loomed.</p>");

        var english = (await _rpc.AnalyzeAsync(chapter.Guid, scene.Id)).Analysis;
        Assert.True(english.KeywordAnalysisSupported);
        Assert.NotEqual(0, english.Intensity);

        // French ships no lexicon: guessing with English words would be wrong, so
        // the keyword-derived values are left for the writer.
        _workspace.Settings.Settings.AutoReplacementLanguage = "fr";
        var french = (await _rpc.AnalyzeAsync(chapter.Guid, scene.Id)).Analysis;

        Assert.False(french.KeywordAnalysisSupported);
        Assert.Equal(0, french.Intensity);
        Assert.Equal(string.Empty, french.Emotion);
        Assert.Equal(string.Empty, french.Conflict);
        Assert.Empty(french.Tags);
        Assert.Empty(french.EmotionKeys);
        // Language-independent statistics are unaffected.
        Assert.Equal(english.WordCount, french.WordCount);
    }

    [Fact]
    public async Task Analyze_LanguageWithoutLexicon_StillOffersAnExistingOverride()
    {
        var chapter = await _workspace.Projects.CreateChapterAsync("K");
        var scene = await _workspace.Projects.CreateSceneAsync(chapter.Guid, "S");
        await Write(chapter, scene, "<p>Texte.</p>");
        await _workspace.Projects.SetSceneAnalysisOverridesAsync(
            chapter.Guid, scene.Id, new SceneAnalysisOverrides { Emotion = "joyful" });

        _workspace.Settings.Settings.AutoReplacementLanguage = "fr";
        var analysis = (await _rpc.AnalyzeAsync(chapter.Guid, scene.Id)).Analysis;

        Assert.Equal("joyful", analysis.Emotion);
        Assert.Equal(["joyful"], analysis.EmotionKeys);   // so the dropdown can show it
    }

    private Task Write(ChapterData chapter, SceneData scene, string content)
        => _workspace.Projects.WriteSceneContentAsync(chapter, scene, content);

    [Fact]
    public async Task AnalyzeRichScene_MatchesEntitiesPovEmotionIntensityAndMentionMatrix()
    {
        // Two characters with surnames / without; one whose scene-scoped override
        // renames it (exercises the override-set display path).
        await Entities.SaveCharacterAsync(new CharacterData
        {
            Name = "Mira",
            Surname = "Frost",
            Role = "Protagonist",
            Group = "Nordwacht",
            Images = [new EntityImage { Path = "Images/mira.png" }]
        });
        await Entities.SaveCharacterAsync(new CharacterData { Name = "Solo" });

        var chapterA = await _workspace.Projects.CreateChapterAsync("Chapter A");
        chapterA.Act = "Act I";
        var rich = await _workspace.Projects.CreateSceneAsync(chapterA.Guid, "Rich Scene");

        await Entities.SaveCharacterAsync(new CharacterData
        {
            Name = "Bram",
            Role = "Sidekick",
            ChapterOverrides =
            [
                new CharacterOverride
                {
                    Chapter = chapterA.Guid,
                    Scene = "Rich Scene",
                    Name = "Bram",
                    Surname = "Thorne",
                    Role = "Captain"
                }
            ]
        });

        await Entities.SaveLocationAsync(new LocationData { Name = "Ashford", Type = "City", Parent = "[[Realm]]" });
        await Entities.SaveLocationAsync(new LocationData { Name = "Harbor" });
        await Entities.SaveItemAsync(new ItemData { Name = "Dagger", Type = "Weapon" });
        await Entities.SaveItemAsync(new ItemData { Name = "Amulet", Type = "Trinket" });
        await Entities.SaveLoreAsync(new LoreData { Name = "The Pact" });

        var sb = new StringBuilder();
        for (var i = 0; i < 100; i++)
        {
            sb.Append("Mira and Solo walked through Ashford toward the Harbor. ");
            sb.Append("\"We must fight now, I say, we cannot flee!\" ");
        }
        sb.Append("Bram carried the Dagger and the Amulet, bound by The Pact, ");
        sb.Append("filled with sorrow and grief, sad despair, blood and hurt. ");
        await Write(chapterA, rich, sb.ToString());

        // A later chapter that mentions only Solo, so Mira/Bram trail off.
        var chapterB = await _workspace.Projects.CreateChapterAsync("Chapter B");
        var laterScene = await _workspace.Projects.CreateSceneAsync(chapterB.Guid, "Later Scene");
        await Write(chapterB, laterScene, "Solo rested by the fire here.");

        var dto = await _rpc.AnalyzeAsync(chapterA.Guid, rich.Id);

        // Characters (first-appearance order): Mira, Solo, Bram.
        Assert.Equal(3, dto.Characters.Length);
        var mira = dto.Characters.Single(c => c.Name == "Mira Frost");
        Assert.Equal("Protagonist", mira.Detail);
        Assert.Equal("Nordwacht", mira.Secondary);
        Assert.EndsWith("/Images/mira.png", mira.ImagePath);
        var solo = dto.Characters.Single(c => c.Name == "Solo");
        Assert.Null(solo.Secondary);
        Assert.Null(solo.ImagePath);
        // Override renamed Bram -> "Bram Thorne" with role "Captain".
        Assert.Contains(dto.Characters, c => c.Name == "Bram Thorne" && c.Detail == "Captain");

        // Locations: parent normalized ([[..]] stripped), empty parent -> null.
        var ashford = dto.Locations.Single(l => l.Name == "Ashford");
        Assert.Equal("City", ashford.Detail);
        Assert.Equal("Realm", ashford.Secondary);
        Assert.Null(dto.Locations.Single(l => l.Name == "Harbor").Secondary);

        Assert.Equal(2, dto.Items.Length);
        Assert.Contains(dto.Items, i => i.Name == "Dagger" && i.Detail == "Weapon");
        Assert.Single(dto.Lore, l => l.Name == "The Pact");

        // Mention matrix: 2 chapters. Mira present in A only (gap 1); Solo present
        // in both (gap 0).
        Assert.Equal(3, dto.MentionRows.Length);
        var miraRow = dto.MentionRows.Single(r => r.Name == "Mira Frost");
        Assert.Equal(2, miraRow.Cells.Length);
        Assert.True(miraRow.Cells[0].Present);
        Assert.True(miraRow.Cells[0].Current);
        Assert.False(miraRow.Cells[1].Present);
        Assert.False(miraRow.Cells[1].Current);
        Assert.Equal("1", miraRow.Cells[0].ChapterLabel);
        Assert.Equal(1, miraRow.LastSeenChaptersAgo);
        var soloRow = dto.MentionRows.Single(r => r.Name == "Solo");
        Assert.True(soloRow.Cells[1].Present);
        Assert.Equal(0, soloRow.LastSeenChaptersAgo);

        var analysis = dto.Analysis;
        Assert.StartsWith("Mira", analysis.Pov);
        Assert.Contains("Mira Frost", analysis.PovOptions);
        Assert.Contains("Bram Thorne", analysis.PovOptions);
        Assert.Equal("sorrowful", analysis.Emotion);
        Assert.Equal(16, analysis.EmotionKeys.Length);
        Assert.Equal(-10, analysis.Intensity);
        Assert.True(analysis.DialoguePercent >= 35, $"dialogue% was {analysis.DialoguePercent}");
        Assert.True(analysis.WordCount >= 1200, $"word count was {analysis.WordCount}");
        Assert.True(analysis.AvgSentenceLength > 0);
        Assert.NotEmpty(analysis.Conflict);
        // Nine tags computed; Distinct().Take(4) keeps the first four.
        Assert.Equal(
            new[] { "sceneTag.dialogue", "sceneTag.highTension", "sceneTag.conflict", "sceneTag.ensemble" },
            analysis.Tags);
    }

    [Fact]
    public async Task AnalyzeEmptyScene_NoMatchesNeutralAndPovFallback()
    {
        // A character whose only override matches nothing: forces every override
        // resolution tier to be evaluated (and return null) via the POV fallback.
        await Entities.SaveCharacterAsync(new CharacterData
        {
            Name = "Ghost",
            ChapterOverrides =
            [
                new CharacterOverride { Chapter = "zzz", Scene = "zzz", Act = "zzz" }
            ]
        });

        var chapter = await _workspace.Projects.CreateChapterAsync("Solo Chapter");
        chapter.Act = "Act Z";
        var blank = await _workspace.Projects.CreateSceneAsync(chapter.Guid, "Blank Scene");

        var dto = await _rpc.AnalyzeAsync(chapter.Guid, blank.Id);

        Assert.Empty(dto.Characters);
        Assert.Empty(dto.Locations);
        Assert.Empty(dto.Items);
        Assert.Empty(dto.Lore);
        Assert.Empty(dto.MentionRows);

        var analysis = dto.Analysis;
        Assert.Equal(string.Empty, analysis.Pov);
        Assert.Equal("neutral", analysis.Emotion);
        Assert.Equal(0, analysis.Intensity);
        Assert.Equal(0, analysis.DialoguePercent);
        Assert.Equal(0d, analysis.AvgSentenceLength);
        Assert.Equal(0, analysis.WordCount);
        Assert.Empty(analysis.Tags);
        // POV fallback lists every character when the scene matched none.
        Assert.Contains("Ghost", analysis.PovOptions);
    }

    [Fact]
    public async Task FirstPersonScene_DetectsFirstPersonPovAndInteriorTag()
    {
        var chapter = await _workspace.Projects.CreateChapterAsync("Ch");
        var scene = await _workspace.Projects.CreateSceneAsync(chapter.Guid, "S");
        await Write(chapter, scene, "I walked. We ran. My path. Our way. Me here.");

        var analysis = (await _rpc.AnalyzeAsync(chapter.Guid, scene.Id)).Analysis;

        Assert.Equal("pov.firstPerson", analysis.Pov);
        Assert.Contains("sceneTag.interior", analysis.Tags);
    }

    [Fact]
    public async Task NegativeScene_YieldsTenseEmotionFromIntensityFallback()
    {
        var chapter = await _workspace.Projects.CreateChapterAsync("Ch");
        var scene = await _workspace.Projects.CreateSceneAsync(chapter.Guid, "S");
        // Negatives chosen to avoid emotion-keyword substrings (e.g. "danger"
        // contains "anger"), so DetectEmotion falls through to the intensity switch.
        await Write(chapter, scene, "blood hurt despair sad dark cold cry scream");

        var analysis = (await _rpc.AnalyzeAsync(chapter.Guid, scene.Id)).Analysis;

        Assert.Equal("tense", analysis.Emotion);
        Assert.Equal(-10, analysis.Intensity);
    }

    [Fact]
    public async Task PositiveScene_YieldsTriumphantEmotionFromIntensityFallback()
    {
        var chapter = await _workspace.Projects.CreateChapterAsync("Ch");
        var scene = await _workspace.Projects.CreateSceneAsync(chapter.Guid, "S");
        await Write(chapter, scene, "warm relief love bright safe");

        var analysis = (await _rpc.AnalyzeAsync(chapter.Guid, scene.Id)).Analysis;

        Assert.Equal("triumphant", analysis.Emotion);
        Assert.Equal(10, analysis.Intensity);
    }

    [Fact]
    public async Task BalancedConflictScene_UsesConflictIntensityBranchAndSnippet()
    {
        var chapter = await _workspace.Projects.CreateChapterAsync("Ch");
        var scene = await _workspace.Projects.CreateSceneAsync(chapter.Guid, "S");
        // hope(+1)*2 - conflict(argue,fight = 2) = 0 -> conflict-only branch.
        await Write(chapter, scene, "hope argue fight");

        var analysis = (await _rpc.AnalyzeAsync(chapter.Guid, scene.Id)).Analysis;

        Assert.Equal(-2, analysis.Intensity);
        Assert.Equal("hope argue fight", analysis.Conflict);
    }

    [Fact]
    public async Task LongConflictSentence_IsTruncatedWithEllipsis()
    {
        var chapter = await _workspace.Projects.CreateChapterAsync("Ch");
        var scene = await _workspace.Projects.CreateSceneAsync(chapter.Guid, "S");
        await Write(chapter, scene,
            "the weary travelers would argue at length about every single little decision that they had ever made here together");

        var analysis = (await _rpc.AnalyzeAsync(chapter.Guid, scene.Id)).Analysis;

        Assert.EndsWith("...", analysis.Conflict);
        Assert.True(analysis.Conflict.Length <= 92);
    }

    [Fact]
    public async Task HtmlScene_IsStrippedAndDecoded()
    {
        var chapter = await _workspace.Projects.CreateChapterAsync("Ch");
        var scene = await _workspace.Projects.CreateSceneAsync(chapter.Guid, "S");
        await Write(chapter, scene, "<p>alpha &amp; beta</p>");

        var analysis = (await _rpc.AnalyzeAsync(chapter.Guid, scene.Id)).Analysis;

        // Tags stripped, entity decoded to '&' (not a word) -> two words remain.
        Assert.Equal(2, analysis.WordCount);
    }

    [Fact]
    public async Task CharacterCard_CarriesGenderAndComputedAgeFromBirthDate()
    {
        // Age stored as a birth date is computed relative to the scene's story
        // date; gender surfaces on the card too.
        await Entities.SaveCharacterAsync(new CharacterData
        {
            Name = "Mira",
            Gender = "Female",
            AgeMode = "date",
            BirthDate = "1990-03-04",
            Age = "" // raw age empty on purpose
        });

        var chapter = await _workspace.Projects.CreateChapterAsync("Ch");
        var scene = await _workspace.Projects.CreateSceneAsync(chapter.Guid, "S");
        scene.Date = "2015-03-04";
        await Write(chapter, scene, "Mira stood at the gate.");

        var dto = await _rpc.AnalyzeAsync(chapter.Guid, scene.Id);

        var mira = dto.Characters.Single(c => c.Name == "Mira");
        Assert.Equal("Female", mira.Gender);
        Assert.Equal("25", mira.Age);
    }

    [Fact]
    public async Task CharacterCard_FallsBackToChapterDate_ThenOverrideAge()
    {
        // No scene date -> chapter date drives the computation.
        await Entities.SaveCharacterAsync(new CharacterData
        {
            Name = "Mira",
            AgeMode = "date",
            BirthDate = "1990-03-04"
        });
        var chapter = await _workspace.Projects.CreateChapterAsync("Ch");
        chapter.Date = "2010-03-04";
        var scene = await _workspace.Projects.CreateSceneAsync(chapter.Guid, "S");
        await Write(chapter, scene, "Mira waited.");

        var byChapter = await _rpc.AnalyzeAsync(chapter.Guid, scene.Id);
        Assert.Equal("20", byChapter.Characters.Single(c => c.Name == "Mira").Age);

        // A plain (non-date) character with a scene-scoped override age uses it.
        await Entities.SaveCharacterAsync(new CharacterData
        {
            Name = "Bram",
            Age = "30",
            ChapterOverrides =
            [
                new CharacterOverride { Chapter = chapter.Guid, Scene = "S", Age = "55" }
            ]
        });
        await Write(chapter, scene, "Mira and Bram waited.");

        var withOverride = await _rpc.AnalyzeAsync(chapter.Guid, scene.Id);
        Assert.Equal("55", withOverride.Characters.Single(c => c.Name == "Bram").Age);

        // Date mode but birth after the reference -> computation yields nothing, so
        // the raw age is used (fall-through arm).
        await Entities.SaveCharacterAsync(new CharacterData
        {
            Name = "Cade", Age = "unborn", AgeMode = "date", BirthDate = "2030-01-01"
        });
        await Write(chapter, scene, "Mira and Bram and Cade waited.");

        var fell = await _rpc.AnalyzeAsync(chapter.Guid, scene.Id);
        Assert.Equal("unborn", fell.Characters.Single(c => c.Name == "Cade").Age);
    }

    [Fact]
    public async Task StoredOverrides_TakePrecedenceOverAutoAnalysis()
    {
        var chapter = await _workspace.Projects.CreateChapterAsync("Ch");
        var scene = await _workspace.Projects.CreateSceneAsync(chapter.Guid, "S");
        await Write(chapter, scene, "Some plain narrative text here.");

        await _workspace.Projects.SetSceneAnalysisOverridesAsync(chapter.Guid, scene.Id, new SceneAnalysisOverrides
        {
            Pov = "Custom POV",
            Emotion = "joyful",
            Intensity = 7,
            Conflict = "Custom conflict",
            Tags = ["alpha", "beta"]
        });

        var analysis = (await _rpc.AnalyzeAsync(chapter.Guid, scene.Id)).Analysis;

        Assert.Equal("Custom POV", analysis.Pov);
        Assert.Equal("joyful", analysis.Emotion);
        Assert.Equal(7, analysis.Intensity);
        Assert.Equal("Custom conflict", analysis.Conflict);
        Assert.Equal(new[] { "alpha", "beta" }, analysis.Tags);
    }
}
