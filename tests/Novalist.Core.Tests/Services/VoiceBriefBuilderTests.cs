using Novalist.Core.Models;
using Novalist.Core.Services;
using Xunit;

namespace Novalist.Core.Tests.Services;

/// <summary>
/// Covers the brief a voice is designed from.
///
/// The test that matters most is the last one: no emotion word, in any language
/// Novalist ships, may reach a design prompt. An emotional word in a brief is
/// baked into the timbre and cannot be got back out per line, which is exactly
/// the fixed-mood voice the two-stage design exists to prevent.
/// </summary>
[Collection(LexiconStaticsCollection.Name)]
public class VoiceBriefBuilderTests
{
    private static SceneAnalysisLexicon Language(string tag)
    {
        var lexicon = SceneAnalysisLexicon.For(tag);
        Assert.NotNull(lexicon);
        return lexicon!;
    }

    private static IReadOnlyList<string> EmotionWords(SceneAnalysisLexicon lexicon)
        => [.. lexicon.EmotionKeys, .. lexicon.Emotions.SelectMany(e => e.Words)];

    private static CharacterData Mira() => new()
    {
        Id = "mira",
        Name = "Mira",
        Surname = "Vance",
        Age = "34",
        Gender = "female",
        Build = "wiry",
        Height = "tall",
        DistinguishingFeatures = "a broken nose, badly set"
    };

    [Fact]
    public void Build_DescribesTheInstrument()
    {
        var draft = VoiceBriefBuilder.Build(Mira(), [], Language("en"));

        Assert.Equal(VoiceBriefRefusal.None, draft.Refusal);
        Assert.Contains("Age: 34", draft.Description);
        Assert.Contains("Gender: female", draft.Description);
        Assert.Contains("Build: wiry", draft.Description);
        Assert.Contains("Height: tall", draft.Description);
        Assert.Contains("broken nose", draft.Description);
    }

    [Fact]
    public void Build_LeavesOutFieldsTheWriterHasNotFilledIn()
    {
        var sparse = new CharacterData { Id = "x", Name = "Nobody", Age = "20" };

        var draft = VoiceBriefBuilder.Build(sparse, [], Language("en"));

        Assert.Equal("Age: 20", draft.Description);
    }

    [Fact]
    public void Build_AnEmptyEntryProducesAnEmptyBriefRatherThanNoise()
    {
        var draft = VoiceBriefBuilder.Build(
            new CharacterData { Id = "x", Name = "Nobody" }, [], Language("en"));

        Assert.Equal(string.Empty, draft.Description);
        Assert.Equal(VoiceBriefRefusal.None, draft.Refusal);
    }

    [Fact]
    public void Build_TakesTheWritersOwnWordsAboutHowSomebodySpeaks()
    {
        var character = Mira();
        character.Sections.Add(new EntitySection
        {
            Title = "Voice",
            Content = "Clipped. Never finishes a sentence she can end with a look."
        });
        character.Sections.Add(new EntitySection
        {
            Title = "Backstory",
            Content = "Grew up on the harbour wall."
        });

        var draft = VoiceBriefBuilder.Build(character, [], Language("en"));

        Assert.Contains("Clipped", draft.Description);
        // The rest of the entry is not about the voice and does not belong here.
        Assert.DoesNotContain("harbour wall", draft.Description);
    }

    [Fact]
    public void Build_HonoursASectionTheWriterWithheldFromModels()
    {
        var character = Mira();
        character.Sections.Add(new EntitySection
        {
            Title = "Speech",
            Content = "The accent she hides",
            AiHidden = true
        });

        var draft = VoiceBriefBuilder.Build(character, [], Language("en"));

        // Consent for the entry is not consent for the part of it they marked
        // private separately.
        Assert.DoesNotContain("accent she hides", draft.Description);
    }

    [Fact]
    public void Build_IgnoresASectionWithNoTitle()
    {
        var character = Mira();
        character.Sections.Add(new EntitySection { Title = "  ", Content = "Something." });

        var draft = VoiceBriefBuilder.Build(character, [], Language("en"));

        Assert.DoesNotContain("Something.", draft.Description);
    }

    [Fact]
    public void Build_TakesACustomPropertyThatNamesTheVoice()
    {
        var character = Mira();
        character.CustomProperties["Accent"] = "northern, softened";
        character.CustomProperties["Favourite knife"] = "the short one";

        var draft = VoiceBriefBuilder.Build(character, [], Language("en"));

        Assert.Contains("northern, softened", draft.Description);
        Assert.DoesNotContain("short one", draft.Description);
    }

    [Fact]
    public void Build_RefusesAnEntryTheWriterWithheldFromModels()
    {
        var character = Mira();
        character.Ai = AiInclusion.Never;

        var draft = VoiceBriefBuilder.Build(character, ["A line."], Language("en"));

        // A local model is still a model.
        Assert.Equal(VoiceBriefRefusal.WithheldFromAi, draft.Refusal);
        Assert.Equal(string.Empty, draft.Description);
        Assert.Empty(draft.SampleLines);
    }

    [Fact]
    public void Build_TheWriterCanOverruleThatDeliberately()
    {
        var character = Mira();
        character.Ai = AiInclusion.Never;

        var draft = VoiceBriefBuilder.Build(
            character, ["A line."], Language("en"), consentOverride: true);

        Assert.Equal(VoiceBriefRefusal.None, draft.Refusal);
        Assert.Contains("Age: 34", draft.Description);
    }

    [Fact]
    public void Build_CarriesAFewOfTheirOwnLines()
    {
        var lines = Enumerable.Range(1, 20).Select(i => $"Line {i}.").ToArray();

        var draft = VoiceBriefBuilder.Build(Mira(), lines, Language("en"));

        Assert.Equal(VoiceBriefBuilder.MaxSampleLines, draft.SampleLines.Count);
        Assert.Equal("Line 1.", draft.SampleLines[0]);
    }

    [Fact]
    public void Build_DropsSampleLinesThatSayNothingAboutTheVoice()
    {
        var draft = VoiceBriefBuilder.Build(
            Mira(),
            ["  ", "Short.", new string('x', 400), "Short.", "  Spaced   out  "],
            Language("en"));

        // Blank, over-long and duplicate lines all go; whitespace is collapsed.
        Assert.Equal(["Short.", "Spaced out"], draft.SampleLines);
    }

    [Fact]
    public void Strip_LeavesProseAloneWhenThereIsNoVocabularyToStrip()
    {
        // A language Novalist ships no lexicon for filters nothing rather than
        // filtering by English.
        Assert.Equal("Tall and angry.", VoiceBriefBuilder.Strip("  Tall and angry.  ", null));
        Assert.Equal(string.Empty, VoiceBriefBuilder.Strip(null, Language("en")));
        Assert.Equal(string.Empty, VoiceBriefBuilder.Strip("   ", Language("en")));
    }

    [Fact]
    public void Strip_JudgesAWordOnWhatItIsRatherThanWhatItSpells()
    {
        // "angry" goes; "wiry" stays, and nothing is cut out of the middle of a
        // word the writer meant.
        var stripped = VoiceBriefBuilder.Strip("An angry, wiry woman.", Language("en"));

        Assert.DoesNotContain("angry", stripped);
        Assert.Contains("wiry", stripped);
        Assert.Contains("woman", stripped);
    }

    [Fact]
    public void Strip_ReachesInsideARunOfCharactersInALanguageWithNoSpaces()
    {
        // The case the word-by-word pass cannot see: Chinese writes the emotion
        // inside a run with no space to find it by, so the filter has to work by
        // substring there or it does nothing at all.
        var chinese = Language("zh-CN");
        var emotion = chinese.EmotionKeys
            .SelectMany(key => chinese.Emotions.Single(e => e.Key == key).Words)
            .First(word => word.Length > 0);

        var stripped = VoiceBriefBuilder.Strip($"她的声音{emotion}。", chinese);

        Assert.DoesNotContain(emotion, stripped, StringComparison.CurrentCultureIgnoreCase);
        // The description of the instrument survives; only the mood is taken out.
        Assert.Contains("她的声音", stripped);
    }

    [Theory]
    [InlineData("en", " ")]
    [InlineData("de", " ")]
    // Run together, the way the language is actually written: a Chinese brief
    // has no spaces to find the emotion by.
    [InlineData("zh-CN", "")]
    public void Build_NoEmotionWordInAnyShippedLanguageReachesADesignPrompt(
        string language, string separator)
    {
        var lexicon = Language(language);
        var words = EmotionWords(lexicon);
        var character = Mira();
        // Every emotion word the language knows, written into the fields a brief
        // is built from - which is what a writer describing a character actually
        // does.
        character.DistinguishingFeatures = string.Join(separator, words);
        character.Sections.Add(new EntitySection
        {
            Title = "Voice",
            Content = string.Join(separator, words)
        });
        character.CustomProperties["Accent"] = string.Join(separator, words);

        var draft = VoiceBriefBuilder.Build(character, [], lexicon);

        Assert.All(words, word =>
            Assert.DoesNotContain(
                word,
                draft.Description,
                StringComparison.CurrentCultureIgnoreCase));
    }
}
