using Novalist.Core.Services;
using Xunit;

namespace Novalist.Core.Tests.Services;

/// <summary>
/// Covers the verb-to-emotion map a language ships: which entries survive
/// loading, and which are dropped because they could never fire. A map entry is
/// only useful when the verb is one the language declares and the emotion is one
/// it can name.
/// </summary>
[Collection(LexiconStaticsCollection.Name)]
public sealed class SceneAnalysisLexiconVerbEmotionsTests : IDisposable
{
    private readonly string _dir;

    public SceneAnalysisLexiconVerbEmotionsTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "nl-verbemo-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        SceneAnalysisLexicon.RegisterUserDirectory(null);
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    /// <summary>A complete minimal lexicon, with whatever verb-to-emotion block
    /// the test is about spliced in.</summary>
    private static string LexiconJson(string? verbEmotions) => $$"""
        {
          "positive": ["bona"],
          "negative": ["malbona"],
          "conflict": ["batalo"],
          "speechVerbs": ["diris", "kriis"],
          {{(verbEmotions == null ? "" : $"\"speechVerbEmotions\": {verbEmotions},")}}
          "firstPerson": ["mi"],
          "pronounsMale": ["li"],
          "pronounsFemale": ["ŝi"],
          "genderMale": ["viro"],
          "genderFemale": ["virino"],
          "wordBoundaries": true,
          "emotions": [{ "key": "joy", "words": ["ĝojo"] }]
        }
        """;

    private SceneAnalysisLexicon Load(string? verbEmotions)
    {
        File.WriteAllText(Path.Combine(_dir, "analysis.eo.json"), LexiconJson(verbEmotions));
        SceneAnalysisLexicon.RegisterUserDirectory(_dir);
        var lexicon = SceneAnalysisLexicon.For("eo");
        Assert.NotNull(lexicon);
        return lexicon!;
    }

    [Fact]
    public void ALanguageShippingNoMap_DirectsNothing()
    {
        // Which is what every language did before the map existed, and is the
        // right behaviour: those lines fall back to the scene's own emotion.
        Assert.Empty(Load(null).SpeechVerbEmotions);
    }

    [Fact]
    public void AnEmptyMap_DirectsNothing()
    {
        Assert.Empty(Load("{}").SpeechVerbEmotions);
    }

    [Fact]
    public void AVerbTheLanguageDoesNotShip_IsDropped()
    {
        var lexicon = Load("{ \"nekonata\": \"joy\", \"diris\": \"joy\" }");

        Assert.Equal(["diris"], lexicon.SpeechVerbEmotions.Keys);
    }

    [Fact]
    public void AnEmotionTheLanguageDoesNotDeclare_IsDropped()
    {
        // It would resolve to a direction nothing can localize.
        var lexicon = Load("{ \"kriis\": \"nedeklarita\", \"diris\": \"joy\" }");

        Assert.Equal(["diris"], lexicon.SpeechVerbEmotions.Keys);
    }

    [Fact]
    public void AMapWrittenWithCapitals_StillMatches()
    {
        var lexicon = Load("{ \"  DIRIS \": \" joy \" }");

        Assert.Equal("joy", lexicon.SpeechVerbEmotions["diris"]);
        Assert.Equal("joy", lexicon.SpeechVerbEmotions["Diris"]);
    }

    [Fact]
    public void AVerbListedTwice_IsCarriedOnce()
    {
        var lexicon = Load("{ \"diris\": \"joy\", \"DIRIS\": \"joy\" }");

        Assert.Single(lexicon.SpeechVerbEmotions);
    }

    [Fact]
    public void EntriesWithNothingOnEitherSide_AreDropped()
    {
        var lexicon = Load("{ \"\": \"joy\", \"diris\": \"\" }");

        Assert.Empty(lexicon.SpeechVerbEmotions);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("de")]
    [InlineData("zh-CN")]
    public void EveryShippedLanguage_MapsOnlyItsOwnVerbsAndItsOwnEmotions(string tag)
    {
        var lexicon = SceneAnalysisLexicon.For(tag);

        Assert.NotNull(lexicon);
        Assert.NotEmpty(lexicon!.SpeechVerbEmotions);
        Assert.All(lexicon.SpeechVerbEmotions, entry =>
        {
            Assert.Contains(entry.Key, lexicon.SpeechVerbs);
            Assert.Contains(entry.Value, lexicon.EmotionKeys);
        });
    }
}
