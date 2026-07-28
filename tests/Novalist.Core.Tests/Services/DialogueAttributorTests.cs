using System.Text.RegularExpressions;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Core.Utilities;
using Xunit;

namespace Novalist.Core.Tests.Services;

public class DialogueAttributorTests
{
    private static readonly CharacterData Aldric = new()
    {
        Id = "hero",
        Name = "Aldric",
        Surname = "Vane",
        Aliases = ["the Warden"]
    };

    private static readonly CharacterData Mira = new() { Id = "mira", Name = "Mira" };

    private static readonly string[] Verbs = ["said", "asked", "whispered", "replied"];

    /// <summary>Runs the whole pipeline over a scene body the way the index
    /// service does, so the tests exercise scanning and attribution together.</summary>
    private static IReadOnlyList<DialogueAttribution> Attribute(
        string html,
        IReadOnlyDictionary<string, string>? overrides = null,
        params CharacterData[] cast)
    {
        var characters = cast.Length > 0 ? cast : [Aldric, Mira];
        var (text, spans) = DialogueScanner.ScanScene(html);
        return DialogueAttributor.Attribute(
            spans,
            text,
            DialogueAttributor.BuildCandidates(characters, wordBoundaries: true),
            Language(Verbs),
            overrides);
    }

    /// <summary>The English matchers, with the pronoun lists attribution needs
    /// to resolve a "he said" tag.</summary>
    private static DialogueLanguage Language(IReadOnlyList<string> verbs)
        => new(
            DialogueAttributor.BuildSpeechVerbPattern(verbs, wordBoundaries: true),
            new Regex(@"(?<![\p{L}\p{N}])(?:he|him|his)(?![\p{L}\p{N}])", RegexOptions.IgnoreCase),
            new Regex(@"(?<![\p{L}\p{N}])(?:she|her|hers)(?![\p{L}\p{N}])", RegexOptions.IgnoreCase));

    [Fact]
    public void Attribute_VerbAndNameAfterQuote_IsHigh()
    {
        var result = Assert.Single(Attribute("<p>\"I won't go,\" said Aldric.</p>"));

        Assert.Equal("hero", result.CharacterId);
        Assert.Equal(DialogueConfidence.High, result.Confidence);
    }

    [Fact]
    public void Attribute_VerbAndNameBeforeQuote_IsHigh()
    {
        var result = Assert.Single(Attribute("<p>Aldric said, \"I won't go.\"</p>"));

        Assert.Equal("hero", result.CharacterId);
        Assert.Equal(DialogueConfidence.High, result.Confidence);
    }

    [Fact]
    public void Attribute_LeadInCreditsTheVerbsOwner_NotTheNearestName()
    {
        // "Aldric said to Mira" — Mira sits closer to the quote but Aldric owns the verb.
        var result = Assert.Single(Attribute("<p>Aldric said to Mira, \"Stay put.\"</p>"));

        Assert.Equal("hero", result.CharacterId);
    }

    [Fact]
    public void Attribute_TrailingTagCreditsTheFirstName()
    {
        var result = Assert.Single(Attribute("<p>\"Stay put,\" said Mira, ignoring Aldric.</p>"));

        Assert.Equal("mira", result.CharacterId);
    }

    [Fact]
    public void Attribute_MatchesFullNameAndAlias()
    {
        var full = Assert.Single(Attribute("<p>\"Now,\" said Aldric Vane.</p>"));
        var alias = Assert.Single(Attribute("<p>\"Now,\" said the Warden.</p>"));

        Assert.Equal("hero", full.CharacterId);
        Assert.Equal("hero", alias.CharacterId);
    }

    [Fact]
    public void Attribute_NameWithoutSpeechVerb_IsMedium()
    {
        var result = Assert.Single(Attribute("<p>\"Not a chance.\" Aldric turned away.</p>"));

        Assert.Equal("hero", result.CharacterId);
        Assert.Equal(DialogueConfidence.Medium, result.Confidence);
    }

    [Fact]
    public void Attribute_DistantVerb_DoesNotReachBackToTheName()
    {
        var filler = new string('x', 60);
        var result = Assert.Single(Attribute($"<p>\"Now.\" Aldric {filler} said nothing more.</p>"));

        // Verb far past the name is a different clause — a name match, not a tag.
        Assert.Equal(DialogueConfidence.Medium, result.Confidence);
    }

    [Fact]
    public void Attribute_MentionSpanInTagBeatsNameMatching_AndIsHigh()
    {
        const string html =
            "<p>\"Now,\" murmured <span class=\"nv-entity-mention\" data-entity-id=\"mira\">the "
            + "Warden</span>.</p>";
        var result = Assert.Single(Attribute(html));

        // The alias text says Aldric; the author-confirmed marker says Mira and wins.
        Assert.Equal("mira", result.CharacterId);
        Assert.Equal(DialogueConfidence.High, result.Confidence);
    }

    [Fact]
    public void Attribute_MentionSpanBeforeQuoteIsUsed()
    {
        const string html =
            "<p><span class=\"nv-entity-mention\" data-entity-id=\"hero\">Someone</span> spoke: "
            + "\"Now.\"</p>";
        var result = Assert.Single(Attribute(html));

        Assert.Equal("hero", result.CharacterId);
    }

    [Fact]
    public void Attribute_IgnoresMentionsOfEntitiesOutsideTheCast()
    {
        const string html =
            "<p>\"Now,\" said <span class=\"nv-entity-mention\" data-entity-id=\"harbour\">the "
            + "docks</span>.</p>";
        var result = Assert.Single(Attribute(html));

        // A location mention must not be read as a speaker.
        Assert.Null(result.CharacterId);
    }

    [Fact]
    public void Attribute_UntaggedLineInTwoHander_AlternatesAtLowConfidence()
    {
        var results = Attribute(
            "<p>\"One,\" said Aldric.</p><p>\"Two,\" said Mira.</p><p>\"Three.\"</p>");

        Assert.Equal("hero", results[2].CharacterId);
        Assert.Equal(DialogueConfidence.Low, results[2].Confidence);
    }

    [Fact]
    public void Attribute_AlternationKeepsSwappingAcrossARun()
    {
        var results = Attribute(
            "<p>\"One,\" said Aldric.</p><p>\"Two,\" said Mira.</p>"
            + "<p>\"Three.\"</p><p>\"Four.\"</p><p>\"Five.\"</p>");

        Assert.Equal(["hero", "mira", "hero"], results.Skip(2).Select(r => r.CharacterId));
    }

    [Fact]
    public void Attribute_RepeatedSpeakerDoesNotPoisonAlternationState()
    {
        // Aldric twice in a row must not make him both "recent" speakers.
        var results = Attribute(
            "<p>\"One,\" said Aldric.</p><p>\"Two,\" said Aldric.</p>"
            + "<p>\"Three,\" said Mira.</p><p>\"Four.\"</p>");

        Assert.Equal("hero", results[3].CharacterId);
    }

    [Fact]
    public void Attribute_NoSecondSpeakerOnRecord_LeavesLineUnassigned()
    {
        var results = Attribute("<p>\"One,\" said Aldric.</p><p>\"Two.\"</p>");

        Assert.Null(results[1].CharacterId);
        Assert.Equal(DialogueConfidence.None, results[1].Confidence);
    }

    [Fact]
    public void Attribute_NoCluesAtAll_LeavesLineUnassigned()
    {
        var result = Assert.Single(Attribute("<p>\"Nobody asked you.\"</p>"));

        Assert.Null(result.CharacterId);
        Assert.Equal(DialogueConfidence.None, result.Confidence);
    }

    [Fact]
    public void Attribute_OverrideWins_AndIsMarkedManual()
    {
        var spans = DialogueScanner.Scan("<p>\"Now,\" said Aldric.</p>");
        var overrides = new Dictionary<string, string> { [spans[0].LineKey] = "mira" };

        var result = Assert.Single(Attribute("<p>\"Now,\" said Aldric.</p>", overrides));

        Assert.Equal("mira", result.CharacterId);
        Assert.Equal(DialogueConfidence.Manual, result.Confidence);
    }

    [Fact]
    public void Attribute_BlankOverride_ClearsTheLineWithoutReguessing()
    {
        var spans = DialogueScanner.Scan("<p>\"Now,\" said Aldric.</p>");
        var overrides = new Dictionary<string, string> { [spans[0].LineKey] = string.Empty };

        var result = Assert.Single(Attribute("<p>\"Now,\" said Aldric.</p>", overrides));

        Assert.Null(result.CharacterId);
        Assert.Equal(DialogueConfidence.Manual, result.Confidence);
    }

    [Fact]
    public void Attribute_OverrideNamingADeletedCharacter_FallsBackToUnassigned()
    {
        var spans = DialogueScanner.Scan("<p>\"Now,\" said Aldric.</p>");
        var overrides = new Dictionary<string, string> { [spans[0].LineKey] = "ghost" };

        var result = Assert.Single(Attribute("<p>\"Now,\" said Aldric.</p>", overrides));

        Assert.Null(result.CharacterId);
    }

    [Fact]
    public void Attribute_ManualLineStillFeedsAlternation()
    {
        var html = "<p>\"One.\"</p><p>\"Two,\" said Mira.</p><p>\"Three.\"</p>";
        var spans = DialogueScanner.Scan(html);
        var overrides = new Dictionary<string, string> { [spans[0].LineKey] = "hero" };

        var results = Attribute(html, overrides);

        Assert.Equal("hero", results[2].CharacterId);
    }

    [Fact]
    public void BuildCandidates_SkipsCharactersWithNoUsableName()
    {
        var nameless = new CharacterData { Id = "blank", Name = "  ", Surname = string.Empty };

        Assert.Empty(DialogueAttributor.BuildCandidates([nameless], wordBoundaries: true));
    }

    [Fact]
    public void BuildCandidates_MatchesWholeWordsOnly_WhenBoundariesApply()
    {
        var results = Attribute("<p>\"Now,\" said Aldrichson.</p>");

        // "Aldrichson" is a different word, not a longer spelling of Aldric.
        Assert.Null(Assert.Single(results).CharacterId);
    }

    [Fact]
    public void BuildCandidates_MatchesSubstrings_WhenLanguageHasNoWordBoundaries()
    {
        var han = new CharacterData { Id = "han", Name = "阿德" };
        var (text, spans) = DialogueScanner.ScanScene("<p>“我不去，”阿德说道。</p>");
        var attributions = DialogueAttributor.Attribute(
            spans,
            text,
            DialogueAttributor.BuildCandidates([han], wordBoundaries: false),
            new DialogueLanguage(
                DialogueAttributor.BuildSpeechVerbPattern(["说", "说道"], wordBoundaries: false),
                new Regex("他"),
                new Regex("她")),
            null);

        var result = Assert.Single(attributions);
        Assert.Equal("han", result.CharacterId);
        Assert.Equal(DialogueConfidence.High, result.Confidence);
    }

    [Fact]
    public void BuildSpeechVerbPattern_EmptyList_MatchesNothing()
    {
        var pattern = DialogueAttributor.BuildSpeechVerbPattern([], wordBoundaries: true);

        Assert.DoesNotMatch(pattern, "said asked whispered");
    }

    [Fact]
    public void Attribute_WithoutSpeechVerbs_DowngradesTaggedLinesToMedium()
    {
        var (text, spans) = DialogueScanner.ScanScene("<p>\"Now,\" said Aldric.</p>");
        var results = DialogueAttributor.Attribute(
            spans,
            text,
            DialogueAttributor.BuildCandidates([Aldric], wordBoundaries: true),
            Language([]),
            null);

        // A language shipping no verb list still attributes, just less confidently.
        Assert.Equal("hero", Assert.Single(results).CharacterId);
        Assert.Equal(DialogueConfidence.Medium, results[0].Confidence);
    }

    [Fact]
    public void Attribute_EmptySceneReturnsNothing()
        => Assert.Empty(Attribute("<p>Just narration.</p>"));

    // ── Same-paragraph continuation ─────────────────────────────────

    [Fact]
    public void Attribute_SecondQuoteInTheSameParagraph_StaysWithTheSameSpeaker()
    {
        var results = Attribute("<p>\"One,\" said Aldric. \"Still me.\"</p>");

        Assert.Equal("hero", results[1].CharacterId);
        // Only as good as the line it continues, which the prose named outright.
        Assert.Equal(DialogueConfidence.High, results[1].Confidence);
    }

    [Fact]
    public void Attribute_ContinuationDoesNotCrossAParagraphBreak()
    {
        var results = Attribute("<p>\"One,\" said Aldric.</p><p>\"New paragraph.\"</p>");

        // A fresh paragraph is conventionally a fresh speaker, so this is not a
        // continuation — and with only one speaker on record, alternation cannot help.
        Assert.Null(results[1].CharacterId);
    }

    [Fact]
    public void Attribute_ContinuationYieldsToATagOfItsOwn()
    {
        var results = Attribute("<p>\"One,\" said Aldric. \"Two,\" said Mira.</p>");

        Assert.Equal("mira", results[1].CharacterId);
    }

    [Fact]
    public void Attribute_ContinuationInheritsAWeakVerdictRatherThanUpgradingIt()
    {
        // The first line is a bare name (Medium); the continuation cannot be
        // more certain than the line it leans on.
        var results = Attribute("<p>\"One.\" Aldric turned away. \"Two.\"</p>");

        Assert.Equal("hero", results[1].CharacterId);
        Assert.Equal(DialogueConfidence.Medium, results[1].Confidence);
    }

    [Fact]
    public void Attribute_ContinuationOfAManualLineIsHigh()
    {
        var html = "<p>\"One.\" \"Two.\"</p>";
        var spans = DialogueScanner.Scan(html);
        var overrides = new Dictionary<string, string> { [spans[0].LineKey] = "mira" };

        var results = Attribute(html, overrides);

        Assert.Equal("mira", results[1].CharacterId);
        Assert.Equal(DialogueConfidence.High, results[1].Confidence);
    }

    [Fact]
    public void Attribute_ContinuationDoesNotOverrideAMentionInItsOwnTag()
    {
        const string html =
            "<p>\"One,\" said Aldric. \"Two,\" murmured "
            + "<span class=\"nv-entity-mention\" data-entity-id=\"mira\">the other</span>.</p>";
        var results = Attribute(html);

        Assert.Equal("mira", results[1].CharacterId);
    }

    // ── Pronoun resolution ──────────────────────────────────────────

    private static readonly CharacterData Male = new()
    {
        Id = "hero", Name = "Aldric", Gender = "male"
    };

    private static readonly CharacterData Female = new()
    {
        Id = "mira", Name = "Mira", Gender = "female"
    };

    private static readonly CharacterData SecondMale = new()
    {
        Id = "bram", Name = "Bram", Gender = "male"
    };

    [Fact]
    public void Attribute_PronounTagResolvesToTheOnlyCharacterOfThatGenderAbove()
    {
        var result = Assert.Single(Attribute(
            "<p>Aldric crossed the yard.</p><p>\"Not a chance,\" he said.</p>",
            null, Male, Female));

        Assert.Equal("hero", result.CharacterId);
        Assert.Equal(DialogueConfidence.Inferred, result.Confidence);
    }

    [Fact]
    public void Attribute_PronounTagLooksForwardWhenNothingIsNamedAbove()
    {
        // A scene that opens on "he" and only names him in the next paragraph.
        var result = Assert.Single(Attribute(
            "<p>\"Morning,\" he said.</p><p>Aldric rolled out of bed.</p>",
            null, Male, Female));

        Assert.Equal("hero", result.CharacterId);
        Assert.Equal(DialogueConfidence.Inferred, result.Confidence);
    }

    [Fact]
    public void Attribute_PronounTagIsAmbiguousWithTwoCharactersOfThatGender()
    {
        var result = Assert.Single(Attribute(
            "<p>Aldric and Bram crossed the yard.</p><p>\"Not a chance,\" he said.</p>",
            null, Male, SecondMale));

        // Either man could be "he" — leave it rather than pick wrong.
        Assert.Null(result.CharacterId);
        Assert.Equal(DialogueConfidence.None, result.Confidence);
    }

    [Fact]
    public void Attribute_PronounTagRespectsGender()
    {
        var result = Assert.Single(Attribute(
            "<p>Aldric crossed the yard.</p><p>\"Not a chance,\" she said.</p>",
            null, Male, Female));

        // "she" cannot be Aldric, and Mira is nowhere in the narration.
        Assert.Null(result.CharacterId);
    }

    [Fact]
    public void Attribute_PronounTagIgnoresCharactersWithNoGenderRecorded()
    {
        var ungendered = new CharacterData { Id = "x", Name = "Aldric" };

        var result = Assert.Single(Attribute(
            "<p>Aldric crossed the yard.</p><p>\"Not a chance,\" he said.</p>",
            null, ungendered));

        Assert.Null(result.CharacterId);
    }

    [Fact]
    public void Attribute_PronounNeedsASpeechVerbToCount()
    {
        var result = Assert.Single(Attribute(
            "<p>Aldric crossed the yard.</p><p>\"Not a chance.\" He kept walking.</p>",
            null, Male, Female));

        // A pronoun with no speech verb is narration, not a dialogue tag.
        Assert.Null(result.CharacterId);
    }

    [Fact]
    public void Attribute_NameInsideAQuoteIsNotAPronounAntecedent()
    {
        // "Morning, Aldric" is Aldric being addressed — it must not make him the
        // antecedent of the following "she".
        var result = Assert.Single(Attribute(
            "<p>\"Morning, Aldric,\" she said.</p>",
            null, Male, Female));

        Assert.Null(result.CharacterId);
    }

    [Fact]
    public void Attribute_NamedTagStillWinsOverThePronounRule()
    {
        var result = Assert.Single(Attribute(
            "<p>Aldric crossed the yard.</p><p>\"Not a chance,\" said Mira.</p>",
            null, Male, Female));

        Assert.Equal("mira", result.CharacterId);
        Assert.Equal(DialogueConfidence.High, result.Confidence);
    }

    // ── Ranked candidates ───────────────────────────────────────────

    [Fact]
    public void Attribute_ConfidentLineOffersNoAlternatives()
    {
        var result = Assert.Single(Attribute("<p>\"Now,\" said Aldric.</p>"));

        Assert.Empty(result.Candidates);
    }

    [Fact]
    public void Attribute_UncertainLineRanksCandidatesAndTheSharesSumTo100()
    {
        var result = Assert.Single(Attribute(
            "<p>Aldric and Mira waited.</p><p>\"Not a chance.\" Aldric turned away.</p>"));

        Assert.Equal(DialogueConfidence.Medium, result.Confidence);
        Assert.Equal("hero", result.Candidates[0].CharacterId);
        Assert.Equal(100, result.Candidates.Sum(c => c.Percent));
        // The bare-name winner should dominate the merely-nearby runner-up.
        Assert.True(result.Candidates[0].Percent > result.Candidates[^1].Percent);
    }

    [Fact]
    public void Attribute_UnassignedLineStillSuggestsWhoIsNearby()
    {
        var result = Assert.Single(Attribute(
            "<p>Aldric crossed the yard.</p><p>\"Nobody asked you.\"</p>"));

        Assert.Null(result.CharacterId);
        Assert.Equal("hero", Assert.Single(result.Candidates).CharacterId);
        Assert.Equal(100, result.Candidates[0].Percent);
    }

    [Fact]
    public void Attribute_LineWithNothingNearbyHasNoCandidates()
    {
        var result = Assert.Single(Attribute("<p>\"Nobody asked you.\"</p>"));

        Assert.Empty(result.Candidates);
    }

    [Fact]
    public void Attribute_ManualLineOffersNoAlternatives()
    {
        var spans = DialogueScanner.Scan("<p>\"Now,\" said Aldric.</p>");
        var overrides = new Dictionary<string, string> { [spans[0].LineKey] = "mira" };

        var result = Assert.Single(Attribute("<p>\"Now,\" said Aldric.</p>", overrides));

        Assert.Empty(result.Candidates);
    }

    [Fact]
    public void Attribute_CandidateListIsCapped()
    {
        var cast = Enumerable.Range(0, 8)
            .Select(i => new CharacterData { Id = $"c{i}", Name = $"Name{i}" })
            .ToArray();
        var narration = "<p>" + string.Join(" and ", cast.Select(c => c.Name)) + " waited.</p>";

        var result = Assert.Single(Attribute(narration + "<p>\"Who said that?\"</p>", null, cast));

        Assert.True(result.Candidates.Count <= 4);
        Assert.Equal(100, result.Candidates.Sum(c => c.Percent));
    }

    [Fact]
    public void Attribute_SpeakingEarlierInTheSceneKeepsACharacterInTheRunning()
    {
        var results = Attribute(
            "<p>\"One,\" said Aldric.</p><p>Mira watched.</p><p>\"Who now?\"</p>");

        // Mira is named right above; Aldric has already spoken. Both are offered.
        Assert.Contains(results[1].Candidates, c => c.CharacterId == "hero");
        Assert.Contains(results[1].Candidates, c => c.CharacterId == "mira");
    }

    // ── Language matchers ───────────────────────────────────────────

    [Fact]
    public void BuildLanguage_NullLexicon_MatchesNothing()
    {
        var language = DialogueAttributor.BuildLanguage(null);

        Assert.DoesNotMatch(language.SpeechVerbs, "said");
        Assert.DoesNotMatch(language.MalePronouns, "he");
        Assert.DoesNotMatch(language.FemalePronouns, "she");
    }

    [Fact]
    public void BuildLanguage_ShippedLexicon_MatchesItsWords()
    {
        var language = DialogueAttributor.BuildLanguage(SceneAnalysisLexicon.For("en"));

        Assert.Matches(language.SpeechVerbs, "she said quietly");
        Assert.Matches(language.MalePronouns, "he waited");
        Assert.Matches(language.FemalePronouns, "she waited");
    }
}
