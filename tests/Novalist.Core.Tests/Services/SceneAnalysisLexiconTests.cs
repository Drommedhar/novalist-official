using Novalist.Core.Services;
using Xunit;

namespace Novalist.Core.Tests.Services;

[Collection(LexiconStaticsCollection.Name)]
public class SceneAnalysisLexiconTests
{
    [Fact]
    public void AvailableLanguages_ShipsTheBundledLocales()
    {
        // One lexicon per bundled UI language; adding a JSON is all it takes.
        Assert.Contains("en", SceneAnalysisLexicon.AvailableLanguages);
        Assert.Contains("de", SceneAnalysisLexicon.AvailableLanguages);
        Assert.Contains("zh-CN", SceneAnalysisLexicon.AvailableLanguages);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("de")]
    [InlineData("zh-CN")]
    public void For_LoadsEveryBundledLexicon(string language)
    {
        var lexicon = SceneAnalysisLexicon.For(language);

        Assert.NotNull(lexicon);
        Assert.NotEmpty(lexicon!.Positive);
        Assert.NotEmpty(lexicon.Negative);
        Assert.NotEmpty(lexicon.Conflict);
        Assert.NotEmpty(lexicon.Emotions);
        Assert.All(lexicon.Emotions, e => Assert.NotEmpty(e.Words));
    }

    [Fact]
    public void EveryLanguage_DeclaresTheSameEmotionKeysInTheSameOrder()
    {
        // Keys are stable identifiers the renderer localizes and scenes persist, so
        // switching writing language must not invalidate a stored emotion.
        var english = SceneAnalysisLexicon.For("en")!.EmotionKeys;
        Assert.NotEmpty(english);

        foreach (var language in SceneAnalysisLexicon.AvailableLanguages)
            Assert.Equal(english, SceneAnalysisLexicon.For(language)!.EmotionKeys);
    }

    [Theory]
    [InlineData("de-AT", "de")]     // regional tag falls back to its base language
    [InlineData("en-GB", "en")]
    [InlineData("zh", "zh-CN")]     // base tag finds the one regional variant
    [InlineData("EN", "en")]        // case-insensitive
    public void For_ResolvesRelatedTags(string requested, string expected)
        => Assert.Equal(expected, SceneAnalysisLexicon.For(requested)!.Language);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void For_BlankLanguage_DefaultsToEnglish(string? language)
        => Assert.Equal("en", SceneAnalysisLexicon.For(language)!.Language);

    [Theory]
    [InlineData("fr")]
    [InlineData("klingon")]
    public void For_UnknownLanguage_IsNullAndUnsupported(string language)
    {
        Assert.Null(SceneAnalysisLexicon.For(language));
        Assert.False(SceneAnalysisLexicon.Supports(language));
    }

    [Fact]
    public void Supports_KnownLanguages()
    {
        Assert.True(SceneAnalysisLexicon.Supports("en"));
        Assert.True(SceneAnalysisLexicon.Supports("de"));
        Assert.True(SceneAnalysisLexicon.Supports("zh-CN"));
        Assert.True(SceneAnalysisLexicon.Supports(null));   // blank means English
    }

    [Fact]
    public void FirstPerson_MatchesWholeWordsOnlyForSpaceDelimitedLanguages()
    {
        var english = SceneAnalysisLexicon.For("en")!;
        Assert.Single(english.FirstPerson.Matches("I walked"));
        // "I" must not match inside another word.
        Assert.Empty(english.FirstPerson.Matches("Winter is inside"));

        var german = SceneAnalysisLexicon.For("de")!;
        Assert.Equal(2, german.FirstPerson.Matches("Ich sah mich um.").Count);

        // Chinese is not space-delimited, so boundaries are off and plain
        // substring matching applies.
        var chinese = SceneAnalysisLexicon.For("zh-CN")!;
        Assert.Equal(2, chinese.FirstPerson.Matches("我看着我的手").Count);
    }

    [Fact]
    public void Words_AreLowercasedTrimmedAndDeduplicated()
    {
        var lexicon = SceneAnalysisLexicon.For("en")!;
        Assert.All(lexicon.Positive, w => Assert.Equal(w.Trim().ToLowerInvariant(), w));
        Assert.Equal(lexicon.Positive.Distinct().Count(), lexicon.Positive.Count);
    }

    [Fact]
    public void For_CachesTheSameInstance()
        => Assert.Same(SceneAnalysisLexicon.For("en"), SceneAnalysisLexicon.For("en"));

    [Fact]
    public void Parse_NoPronouns_YieldsAMatcherThatNeverMatches()
    {
        // A language file may legitimately omit first-person pronouns; POV detection
        // must then simply never claim first person.
        var lexicon = SceneAnalysisLexicon.Parse(
            """{ "firstPerson": [], "positive": ["a"], "emotions": [{ "key": "neutral", "words": ["x"] }] }""",
            "test");

        Assert.NotNull(lexicon);
        Assert.Empty(lexicon!.FirstPerson.Matches("I me my we us anything at all"));
    }

    [Fact]
    public void Parse_DropsBlankWordsAndKeylessEmotions()
    {
        var lexicon = SceneAnalysisLexicon.Parse(
            """
            {
              "firstPerson": ["I", "  "],
              "positive": ["Hope", " hope ", ""],
              "emotions": [
                { "key": "neutral", "words": ["x"] },
                { "key": "  ", "words": ["y"] }
              ]
            }
            """,
            "test");

        Assert.NotNull(lexicon);
        Assert.Equal(["hope"], lexicon!.Positive);          // trimmed, lowercased, deduped
        Assert.Equal(["neutral"], lexicon.EmotionKeys);     // the keyless entry is dropped
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("{ \"emotions\": 5 }")]
    public void Parse_MalformedJson_ReturnsNull(string json)
        => Assert.Null(SceneAnalysisLexicon.Parse(json, "test"));

    [Fact]
    public void Parse_JsonNull_ReturnsNull()
        => Assert.Null(SceneAnalysisLexicon.Parse("null", "test"));

    [Theory]
    [InlineData("en", "said")]
    [InlineData("de", "sagte")]
    [InlineData("zh-CN", "说")]
    public void ShippedLexicons_CarrySpeechVerbs(string tag, string expected)
    {
        var lexicon = SceneAnalysisLexicon.For(tag);

        Assert.NotNull(lexicon);
        Assert.Contains(expected, lexicon!.SpeechVerbs);
    }

    [Fact]
    public void Parse_SpeechVerbsAreTrimmedLowercasedAndDeduped()
    {
        var lexicon = SceneAnalysisLexicon.Parse(
            """{ "speechVerbs": ["Said", " said ", "", "asked"], "emotions": [] }""",
            "test");

        Assert.NotNull(lexicon);
        Assert.Equal(["said", "asked"], lexicon!.SpeechVerbs);
    }

    [Fact]
    public void Parse_MissingSpeechVerbs_LeavesTheListEmpty()
    {
        var lexicon = SceneAnalysisLexicon.Parse("""{ "emotions": [] }""", "test");

        Assert.NotNull(lexicon);
        Assert.Empty(lexicon!.SpeechVerbs);
    }

    [Theory]
    [InlineData("en", true)]
    [InlineData("zh-CN", false)]
    public void ShippedLexicons_DeclareWhetherWordsAreSpaceDelimited(string tag, bool expected)
        => Assert.Equal(expected, SceneAnalysisLexicon.For(tag)!.WordBoundaries);

    [Theory]
    [InlineData("en", "he waited", "she waited")]
    [InlineData("de", "er wartete", "sie wartete")]
    [InlineData("zh-CN", "他等着", "她等着")]
    public void ShippedLexicons_CarryGenderedPronouns(string tag, string male, string female)
    {
        var lexicon = SceneAnalysisLexicon.For(tag)!;

        Assert.Matches(lexicon.MalePronouns, male);
        Assert.Matches(lexicon.FemalePronouns, female);
        Assert.DoesNotMatch(lexicon.MalePronouns, female);
    }

    [Fact]
    public void Parse_MissingPronounLists_MatchNothing()
    {
        var lexicon = SceneAnalysisLexicon.Parse("""{ "emotions": [] }""", "test")!;

        Assert.DoesNotMatch(lexicon.MalePronouns, "he him his she her");
        Assert.DoesNotMatch(lexicon.FemalePronouns, "he him his she her");
    }

    [Theory]
    [InlineData("male", DialogueGender.Male)]
    [InlineData("Female", DialogueGender.Female)]
    [InlineData("  m  ", DialogueGender.Male)]
    [InlineData("männlich", DialogueGender.Male)]     // written in the UI language,
    [InlineData("weiblich", DialogueGender.Female)]   // not the manuscript's
    [InlineData("女", DialogueGender.Female)]
    [InlineData("nonbinary", DialogueGender.Unknown)]
    [InlineData("", DialogueGender.Unknown)]
    [InlineData(null, DialogueGender.Unknown)]
    public void ClassifyGender_ReadsTheFieldInAnyShippedLanguage(string? value, DialogueGender expected)
        => Assert.Equal(expected, SceneAnalysisLexicon.ClassifyGender(value));
}
