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

    /// <summary>
    /// The vocabulary that must never reach a design prompt: every emotion word
    /// the language knows, less the ones that describe how somebody
    /// <em>sounds</em>.
    ///
    /// The subtraction is not a loophole, it is the fix. "quiet", "soft",
    /// "steady", "heavy" and "low" are all in the emotion lists and all of them
    /// are how a voice is described, so removing them took the writer's most
    /// precise description of the instrument and left the punctuation behind.
    /// </summary>
    private static IReadOnlyList<string> EmotionWords(SceneAnalysisLexicon lexicon)
    {
        var timbre = lexicon.TimbreWords.ToHashSet(StringComparer.CurrentCultureIgnoreCase);
        return
        [
            .. lexicon.EmotionKeys.Where(w => !timbre.Contains(w)),
            .. lexicon.Emotions.SelectMany(e => e.Words).Where(w => !timbre.Contains(w))
        ];
    }

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
        Assert.DoesNotContain("Build", draft.Description);
        Assert.DoesNotContain("Height", draft.Description);
        Assert.DoesNotContain("broken nose", draft.Description);
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
    public void Build_ALineCarryingAMoodIsNotWhatAVoiceIsDesignedFrom()
    {
        // The clip a voice is designed as is what every later line is cloned
        // from, so a character designed speaking their worst scene has that
        // scene's delivery in their timbre for the whole book. These went
        // through unfiltered while the writer's own word "quiet" was being
        // scrubbed out of the description beside them.
        var lexicon = Language("en");
        var charged = EmotionWords(lexicon).First(w => w.Length > 3);

        var draft = VoiceBriefBuilder.Build(
            Mira(),
            [$"She was {charged}, and said so.", "Get out!", "The tide turned at four."],
            lexicon);

        var plain = Assert.Single(draft.SampleLines);
        Assert.Equal("The tide turned at four.", plain);
    }

    [Fact]
    public void Build_ACharacterWithNothingSaidPlainlyHasNoSampleLines()
    {
        // Not a failure: the engine falls back to a neutral sentence in the
        // book's own language, which is a better voice than one designed from a
        // scream.
        var lexicon = Language("en");
        var charged = EmotionWords(lexicon).First(w => w.Length > 3);

        var draft = VoiceBriefBuilder.Build(
            Mira(), ["Get out!", $"Everything was {charged}."], lexicon);

        Assert.Empty(draft.SampleLines);
    }

    [Fact]
    public void Build_AWellDocumentedCharacterStillArrivesAsABrief()
    {
        // A bound keeps even an explicitly voice-focused section concise enough
        // that its acoustic cues are not buried.
        var character = Mira();
        character.Sections.Add(new EntitySection
        {
            Title = "Voice",
            Content = "A northern burr she has never lost."
        });
        for (var i = 0; i < 12; i++)
        {
            character.Sections.Add(new EntitySection
            {
                Title = $"Section {i}",
                Content = string.Join(" ", Enumerable.Repeat("She carries herself well.", 12))
            });
        }

        var draft = VoiceBriefBuilder.Build(character, [], Language("en"));

        // What the writer said about the voice is in, and the brief is still a
        // brief.
        Assert.Contains("northern burr", draft.Description);
        Assert.InRange(draft.Description.Length, 1, 700);
    }

    [Fact]
    public void Build_ASectionCutForRoomIsCutAtAWordAndNotThroughOne()
    {
        var character = Mira();
        character.Sections.Add(new EntitySection
        {
            Title = "Voice",
            Content = string.Join(" ", Enumerable.Repeat("burr", 400))
        });
        character.Sections.Add(new EntitySection
        {
            Title = "Bearing",
            Content = "Stands very straight."
        });

        var draft = VoiceBriefBuilder.Build(character, [], Language("en"));

        // The long one filled the room, so the next one was cut to nothing
        // rather than to half a word.
        Assert.DoesNotContain("Stands very str", draft.Description);
        Assert.DoesNotContain("bur ", draft.Description);
    }

    [Fact]
    public void Build_ALineThatIsNothingButPunctuationIsNotAMood()
    {
        // Reached through the sample filter: a token that trims away to nothing
        // is not a word, and is certainly not a forbidden one.
        var draft = VoiceBriefBuilder.Build(Mira(), ["'' -- ''"], Language("en"));

        Assert.Single(draft.SampleLines);
    }

    [Fact]
    public void Build_ASectionOfOneUnbrokenRunIsDroppedRatherThanCutThrough()
    {
        var character = Mira();
        character.Sections.Add(new EntitySection
        {
            Title = "Voice",
            Content = string.Join(" ", Enumerable.Repeat("burr", 300))
        });
        character.Sections.Add(new EntitySection
        {
            Title = "Bearing",
            Content = new string('x', 400)
        });

        var draft = VoiceBriefBuilder.Build(character, [], Language("en"));

        Assert.DoesNotContain("xxx", draft.Description);
    }

    [Fact]
    public void Build_AChineseLineCarryingAMoodIsAlsoDropped()
    {
        // A language with no spaces has to be searched by substring here too,
        // exactly as the description is.
        var chinese = Language("zh-CN");
        var charged = EmotionWords(chinese).First(w => w.Length > 1);

        var draft = VoiceBriefBuilder.Build(
            Mira(), [$"她的声音{charged}。", "她转过身。"], chinese);

        var plain = Assert.Single(draft.SampleLines);
        Assert.Equal("她转过身。", plain);
    }

    [Fact]
    public void Build_WithNoLexiconEveryLineIsAsPlainAsAnyOther()
    {
        // Nothing to filter by, so nothing is filtered - rather than everything
        // being thrown away.
        var draft = VoiceBriefBuilder.Build(Mira(), ["She turned."], lexicon: null);

        Assert.Single(draft.SampleLines);
    }

    [Fact]
    public void Build_DoesNotGuessThatUntitledProseDescribesSound()
    {
        // A character entry has no description field, so everything a writer
        // writes about somebody is a section - and a section they never got
        // round to naming is still their description of the person.
        var character = Mira();
        character.Sections.Add(new EntitySection { Title = "  ", Content = "Speaks slowly." });

        var draft = VoiceBriefBuilder.Build(character, [], Language("en"));

        Assert.DoesNotContain("Speaks slowly.", draft.Description);
    }

    [Fact]
    public void Build_UsesVoiceSectionsAndLeavesAppearanceOut()
    {
        // The fault this ends: a fully written character whose headings never
        // used the word "voice" came back as five structured fields and nothing
        // else. Description, Appearance, Personality, Backstory, Beschreibung
        // and the Chinese equivalent all fell out of the brief.
        var character = Mira();
        character.Sections.Add(new EntitySection
        {
            Title = "Appearance",
            Content = "Tall, and older than she looks."
        });
        character.Sections.Add(new EntitySection
        {
            Title = "Voice",
            Content = "A northern burr she has never lost."
        });

        var draft = VoiceBriefBuilder.Build(character, [], Language("en"));

        Assert.DoesNotContain("older than she looks", draft.Description);
        Assert.Contains("northern burr", draft.Description);
    }

    [Fact]
    public void Build_TheWordsAVoiceIsDescribedWithSurviveTheFilter()
    {
        // This is what a writer types, and what came back was
        // "Voice: A , voice; low and , at the edges, with a  northern burr."
        var character = Mira();
        character.Sections.Add(new EntitySection
        {
            Title = "Voice",
            Content = "A quiet, gentle voice; low and steady, soft at the edges, "
                + "with a heavy northern burr."
        });

        var draft = VoiceBriefBuilder.Build(character, [], Language("en"));

        foreach (var word in new[] { "quiet", "gentle", "low", "steady", "soft", "heavy" })
            Assert.Contains(word, draft.Description, StringComparison.Ordinal);
        Assert.Contains("northern burr", draft.Description);
    }

    [Fact]
    public void Build_AMoodStillDoesNotReachTheBrief()
    {
        var character = Mira();
        character.Sections.Add(new EntitySection
        {
            Title = "Voice",
            Content = "A furious, grief-stricken woman with a northern burr."
        });

        var draft = VoiceBriefBuilder.Build(character, [], Language("en"));

        Assert.DoesNotContain("furious", draft.Description, StringComparison.OrdinalIgnoreCase);
        // The compound this class's own documentation names as the thing that
        // must never get through, and which whole-token matching let straight
        // past: "grief" was forbidden and "grief-stricken" was a different word.
        Assert.DoesNotContain("grief", draft.Description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("northern burr", draft.Description);
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
        var emotion = EmotionWords(chinese).First(word => word.Length > 0);

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
