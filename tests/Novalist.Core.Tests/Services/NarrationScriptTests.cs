using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Core.Utilities;
using Xunit;

namespace Novalist.Core.Tests.Services;

/// <summary>
/// Covers the run of segments a reading is made of: the quoted passages cast to
/// whoever the prose says is speaking, everything between them handed to the
/// narrator, and each one directed by what the writer already wrote down.
/// </summary>
[Collection(LexiconStaticsCollection.Name)]
public class NarrationScriptTests
{
    private static readonly CharacterData Mira = new() { Id = "mira", Name = "Mira" };
    private static readonly CharacterData Aldric = new() { Id = "hero", Name = "Aldric" };

    private static IReadOnlyList<NarrationSegment> Build(
        string html,
        string? sceneEmotion = null,
        int? sceneIntensity = null,
        IReadOnlyDictionary<string, string>? speakers = null,
        IReadOnlyDictionary<string, string>? directions = null,
        string language = "en")
    {
        var lexicon = SceneAnalysisLexicon.For(language);
        return NarrationScript.Build(
            html,
            DialogueAttributor.BuildCandidates([Mira, Aldric], lexicon?.WordBoundaries ?? true),
            DialogueAttributor.BuildLanguage(lexicon),
            EmotionDirector.BuildLanguage(lexicon),
            speakers,
            directions,
            sceneEmotion,
            sceneIntensity,
            UtteranceLanguage.From(lexicon));
    }

    // ─── one utterance is one sentence, on both kinds of segment ───

    [Theory]
    // A title, and the commonest of the lot.
    [InlineData("<p>Dr. Reyes crossed the room to where Mr. Vance stood.</p>", 1)]
    // A time. This was three utterances: "The bell rang at 10 a." / "m." /
    // "sharp." - which is not one breath, it is a stutter.
    [InlineData("<p>The bell rang at 10 a.m. sharp.</p>", 1)]
    // Initials.
    [InlineData("<p>J. R. R. Tolkien wrote it.</p>", 1)]
    // A decimal and a thousands separator.
    [InlineData("<p>It cost $1,200.50 in 1997.</p>", 1)]
    // An abbreviation the list does not carry, caught by the lower-case word
    // after it - which is why a language with no analysis pack still gets most
    // of this.
    [InlineData("<p>She counted twelve, thirteen, fourteen etc. and gave up.</p>", 1)]
    // And a stop that really is one, so the fix does not simply stop cutting.
    [InlineData("<p>She waited. He did not come.</p>", 2)]
    [InlineData("<p>Dr. Reyes waited. He did not come.</p>", 2)]
    public void Build_AFullStopIsOnlyAnEndingWhenItActuallyEndsSomething(
        string html, int expected)
        => Assert.Equal(expected, Build(html).Count);

    [Fact]
    public void Build_GermanOrdinalsAreNotSentenceEndings()
    {
        // German writes "3. Mai" where English writes "3rd May", so a point
        // after a number followed by a capital is ambiguous in a way no
        // surrounding evidence resolves.
        Assert.Single(Build("<p>Am 3. Mai kam der Brief.</p>", language: "de"));

        // But a year is not an ordinal, and the sentence after it is a sentence.
        Assert.Equal(
            2, Build("<p>Es geschah im Jahr 1997. Danach kam nichts.</p>", language: "de").Count);
    }

    [Fact]
    public void Build_AQuoteWithNothingInsideItIsStillTheWritersLine()
    {
        // The splitter can address no utterance in it, and the line is carried
        // through as it always was rather than disappearing out of the reading.
        // Nothing inside the marks but more marks. The scanner keeps it, because
        // the writer typed it; the splitter can address no utterance in it. It
        // is carried through as it always was rather than dropping out of the
        // reading.
        var segments = Build("<p>He said, \"‘’\" and nothing more.</p>");

        Assert.Contains(segments, s => s.Kind == NarrationSegmentKind.Dialogue);
    }

    [Fact]
    public void Build_ASpeechIsCutIntoSentencesLikeAnythingElse()
    {
        // The half the earlier fix missed. The gap between quotes became one
        // utterance per sentence while the quote itself stayed whole however
        // long it ran - and a speech past what the model will say in one go
        // came back cut off mid-word, with the clip written and its duration
        // reported as though nothing had happened.
        var segments = Build(
            "<p>\"I waited. You did not come. I will not wait again.\"</p>");

        Assert.Equal(3, segments.Count);
        Assert.All(segments, s => Assert.Equal(NarrationSegmentKind.Dialogue, s.Kind));
        Assert.Equal("I waited.", segments[0].Text);
        Assert.Equal("You did not come.", segments[1].Text);
        Assert.Equal("I will not wait again.", segments[2].Text);
    }

    [Fact]
    public void Build_TheQuoteMarksAreNotSpokenAndAreStillHighlighted()
    {
        var segments = Build("<p>\"I waited. I will not wait again.\"</p>");

        // Nothing a voice would say out loud.
        Assert.All(segments, s => Assert.DoesNotContain('"', s.Text));

        // But the tint the writer sees is unchanged: the first utterance is
        // marked from the opening mark and the last one to the closing one.
        var (text, spans) = DialogueScanner.ScanScene("<p>\"I waited. I will not wait again.\"</p>");
        Assert.Equal(spans[0].TextStart, segments[0].TextStart);
        Assert.Equal(spans[0].TextEnd, segments[^1].TextEnd);
        Assert.Equal('"', text[segments[0].TextStart]);
    }

    [Fact]
    public void Build_EveryUtteranceOfOneSpeechSharesItsLineAndItsDirection()
    {
        var segments = Build(
            "<p>\"I waited. You did not come,\" Mira snapped.</p>");

        var spoken = segments.Where(s => s.Kind == NarrationSegmentKind.Dialogue).ToArray();
        Assert.Equal(2, spoken.Length);

        // One line, so one speaker and one direction: a writer directs what
        // they wrote, not the breaths a model takes through it. The Dialogue
        // view's key is what a correction is addressed to, so it has to be the
        // same on both.
        Assert.Equal(spoken[0].LineKey, spoken[1].LineKey);
        Assert.Equal(spoken[0].SpeakerId, spoken[1].SpeakerId);
        Assert.Equal(spoken[0].Direction.Key, spoken[1].Direction.Key);

        // And the keys are distinct, because each is separately spoken, cached
        // and highlighted.
        Assert.NotEqual(spoken[0].Key, spoken[1].Key);
    }

    [Fact]
    public void Build_ALineThatDoesNotSplitKeepsExactlyTheKeyItAlreadyHad()
    {
        // Almost every quoted line is one sentence, and a writer's stored
        // directions and speaker corrections are addressed by this key. Changing
        // it would silently orphan every one of them.
        var segments = Build("<p>\"You are late,\" Mira snapped.</p>");

        var line = segments.First(s => s.Kind == NarrationSegmentKind.Dialogue);
        Assert.Equal(line.LineKey, line.Key);
        Assert.DoesNotContain('~', line.Key);
    }

    [Fact]
    public void Build_ANarrationSegmentIsItsOwnLine()
    {
        var segments = Build("<p>The tide turned.</p>");

        var prose = Assert.Single(segments);
        Assert.Equal(prose.Key, prose.LineKey);
    }

    [Fact]
    public void Build_SplitsTheQuoteFromItsTag()
    {
        // The single most audible difference between a performed reading and a
        // machine reading a script: "she said" is the narrator, not Mira.
        var segments = Build("<p>\"Get out,\" she said, not turning round.</p>");

        Assert.Equal(2, segments.Count);
        Assert.Equal(NarrationSegmentKind.Dialogue, segments[0].Kind);
        Assert.Equal("Get out,", segments[0].Text);
        Assert.Equal(NarrationSegmentKind.Narration, segments[1].Kind);
        Assert.Equal("she said, not turning round.", segments[1].Text);
        Assert.Null(segments[1].SpeakerId);
    }

    [Fact]
    public void Build_KeepsTheProseBeforeAndAfterAQuote()
    {
        var segments = Build(
            "<p>She had been on the wall since the tide turned. \"You are late,\" " +
            "Mira snapped. The cold had got into her jaw.</p>");

        Assert.Equal(
            [
                NarrationSegmentKind.Narration,
                NarrationSegmentKind.Dialogue,
                NarrationSegmentKind.Narration,
                NarrationSegmentKind.Narration
            ],
            segments.Select(s => s.Kind));
        Assert.Equal("She had been on the wall since the tide turned.", segments[0].Text);
        Assert.Equal("mira", segments[1].SpeakerId);
        // The tag and the sentence after it are two utterances now, and the tag
        // still belongs to the narrator.
        Assert.Equal("Mira snapped.", segments[2].Text);
        Assert.Equal("The cold had got into her jaw.", segments[3].Text);
    }

    [Fact]
    public void Build_NumbersSegmentsInReadingOrder()
    {
        var segments = Build(
            "<p>\"One,\" said Mira.</p><p>\"Two,\" said Aldric.</p>");

        Assert.Equal(Enumerable.Range(0, segments.Count), segments.Select(s => s.Index));
    }

    [Fact]
    public void Build_ProseIsCutIntoThingsAVoiceWouldSayInOneBreath()
    {
        var segments = Build("<p>The harbour was empty.</p><p>The tide turned.</p>");

        // A sentence each, not a scene each. The gap between two quotes is not
        // a unit of speech: handed over whole it was thirty seconds of audio
        // from one call, and the wrong thing to highlight while following along.
        Assert.Equal(2, segments.Count);
        Assert.All(segments, s => Assert.Equal(NarrationSegmentKind.Narration, s.Kind));
        Assert.Equal("The harbour was empty.", segments[0].Text);
        Assert.Equal("The tide turned.", segments[1].Text);
    }

    [Fact]
    public void Build_ASentenceIsNotBrokenAtEveryFullStopItContains()
    {
        var segments = Build("<p>She waited... and then she left.</p>");

        // An ellipsis is one ending, not three.
        Assert.Equal(2, segments.Count);
        Assert.Equal("She waited...", segments[0].Text);
        Assert.Equal("and then she left.", segments[1].Text);
    }

    [Fact]
    public void Build_AChineseSceneIsCutToo()
    {
        // A Chinese scene has no full stop in it at all, and came back as one
        // unbroken utterance.
        var segments = Build(
            "<p>\u5979\u8f6c\u8fc7\u8eab\u3002\u96e8\u4e0b\u5f97\u66f4\u5927\u4e86\u3002</p>",
            language: "zh-CN");

        Assert.Equal(2, segments.Count);
    }

    [Theory]
    // Nothing punctuated at all, and no paragraph break to end it either. Every
    // scene the projection produces ends in a newline, so this is reached only
    // by a caller handing over a bare run - but losing the last words of it
    // would be losing the writer's prose.
    [InlineData("no stop at all", 1)]
    [InlineData("One. Two", 2)]
    public void Utterances_ProseThatSimplyStopsIsStillYielded(string text, int expected)
    {
        var cuts = NarrationScript.Utterances(text, 0, text.Length).ToArray();

        Assert.Equal(expected, cuts.Length);
        Assert.Equal(text.Length, cuts[^1].End);
    }

    [Fact]
    public void Build_OneEnormousWordIsBrokenRatherThanSentWhole()
    {
        // No space to break at. Better a word cut in half than an utterance no
        // model will say.
        var wall = new string('x', 700);
        var segments = Build($"<p>{wall}</p>");

        Assert.True(segments.Count > 1);
        Assert.All(segments, s => Assert.True(s.Text.Length <= 320));
    }

    [Fact]
    public void Build_ProseThatStopsWithoutAFullStopIsStillSpoken()
    {
        // The last thing in a scene need not be punctuated, and losing it would
        // be losing the writer's words.
        var segments = Build("<p>The tide turned.</p><p>And then</p>");

        Assert.Equal(2, segments.Count);
        Assert.Equal("And then", segments[1].Text);
    }

    [Fact]
    public void Build_TrailingSpacesBeforeAParagraphBreakAreNotSpoken()
    {
        var segments = Build("<p>The tide turned   </p><p>It turned again.</p>");

        Assert.Equal("The tide turned", segments[0].Text);
    }

    [Fact]
    public void Build_ProseThatNeverEndsIsStillBrokenSomewhere()
    {
        // Stream of consciousness with no full stop in it. A speech model is
        // built to say a sentence, not a page.
        var runOn = string.Join(' ', Enumerable.Repeat("and on it went", 60));
        var segments = Build($"<p>{runOn}</p>");

        Assert.True(segments.Count > 1);
        Assert.All(segments, s => Assert.True(s.Text.Length <= 320, s.Text.Length.ToString()));
        // Broken between words, never through one.
        Assert.All(segments, s => Assert.DoesNotContain("  ", s.Text));
    }

    [Fact]
    public void Build_AnEmptySceneHasNothingToRead()
    {
        Assert.Empty(Build(""));
        Assert.Empty(Build(null!));
        Assert.Empty(Build("<p></p>"));
    }

    [Fact]
    public void Build_AdjacentQuotesProduceNoEmptyNarrationBetweenThem()
    {
        var segments = Build("<p>\"One.\" \"Two.\"</p>");

        Assert.Equal(2, segments.Count);
        Assert.All(segments, s => Assert.Equal(NarrationSegmentKind.Dialogue, s.Kind));
    }

    [Theory]
    [InlineData("\"Straight quotes,\" said Mira.")]
    [InlineData("“Curly quotes,” said Mira.")]
    [InlineData("„German quotes,“ said Mira.")]
    [InlineData("«Guillemets,» said Mira.")]
    [InlineData("»Reversed guillemets,« said Mira.")]
    [InlineData("‹Single guillemets,› said Mira.")]
    [InlineData("‚Single low quotes,‘ said Mira.")]
    public void Build_RecognisesEveryQuoteStyleTheAppSupports(string paragraph)
    {
        var segments = Build("<p>" + paragraph + "</p>");

        var spoken = Assert.Single(segments, s => s.Kind == NarrationSegmentKind.Dialogue);
        Assert.Equal("mira", spoken.SpeakerId);
    }

    [Fact]
    public void Build_ALineNobodyCanBeFoundForIsStillRead()
    {
        // The narrator takes it. Skipping it would leave a hole in the reading,
        // which sounds exactly like the feature is broken.
        var segments = Build("<p>\"Nobody asked you.\"</p>");

        var only = Assert.Single(segments);
        Assert.Equal(NarrationSegmentKind.Dialogue, only.Kind);
        Assert.Null(only.SpeakerId);
        Assert.Equal(DialogueConfidence.None, only.Confidence);
    }

    [Fact]
    public void Build_CarriesTheConfidenceAndCandidatesFromAttribution()
    {
        var segments = Build(
            "<p>\"A,\" said Mira.</p><p>\"B,\" said Aldric.</p><p>\"C.\"</p>");

        var spoken = segments.Where(s => s.Kind == NarrationSegmentKind.Dialogue).ToArray();
        Assert.Equal(DialogueConfidence.High, spoken[0].Confidence);
        Assert.Empty(spoken[0].Candidates);
        // The untagged third line is a guess from alternation, and says so.
        Assert.Equal(DialogueConfidence.Low, spoken[2].Confidence);
        Assert.NotEmpty(spoken[2].Candidates);
        Assert.Equal(100, spoken[2].Candidates.Sum(c => c.Percent));
    }

    [Fact]
    public void Build_HonoursTheWritersOwnSpeaker()
    {
        var plain = Build("<p>\"Nobody asked you.\"</p>");
        var key = plain[0].Key;

        var segments = Build(
            "<p>\"Nobody asked you.\"</p>",
            speakers: new Dictionary<string, string> { [key] = "hero" });

        Assert.Equal("hero", segments[0].SpeakerId);
        Assert.Equal(DialogueConfidence.Manual, segments[0].Confidence);
    }

    [Fact]
    public void Build_NarrationSegmentsAreKeyedByTheirOwnWords()
    {
        // Two identical stretches of prose in one scene have to stay
        // distinguishable, or a direction on one lands on both.
        var segments = Build("<p>The tide turned. \"One.\" The tide turned.</p>");

        var prose = segments.Where(s => s.Kind == NarrationSegmentKind.Narration).ToArray();
        Assert.Equal(2, prose.Length);
        Assert.Equal(prose[0].Text, prose[1].Text);
        Assert.NotEqual(prose[0].Key, prose[1].Key);
        Assert.All(prose, s => Assert.StartsWith("n:", s.Key));
    }

    [Fact]
    public void Build_NarrationKeysCannotCollideWithALineKey()
    {
        var segments = Build("<p>\"One,\" said Mira.</p>");

        var spoken = Assert.Single(segments, s => s.Kind == NarrationSegmentKind.Dialogue);
        Assert.DoesNotContain("n:", spoken.Key);
    }

    [Fact]
    public void Build_DirectsASpokenLineFromItsSpeechVerb()
    {
        var segments = Build("<p>\"You are late,\" Mira snapped.</p>");

        var spoken = segments[0];
        Assert.Equal("angry", spoken.Direction.Key);
        Assert.Equal(DirectionSource.Verb, spoken.Direction.Source);
        Assert.Equal("snapped", spoken.Direction.Evidence);
    }

    [Fact]
    public void Build_DirectsAnUntaggedLineFromTheScene()
    {
        var segments = Build("<p>\"You are late,\" said Mira.</p>", sceneEmotion: "tense");

        Assert.Equal("tense", segments[0].Direction.Key);
        Assert.Equal(DirectionSource.Scene, segments[0].Direction.Source);
    }

    [Fact]
    public void Build_NarrationIsNeverDirectedByADialogueTagsVerb()
    {
        // "she snapped" directs the line it introduces, not the introducing. The
        // scene is the only evidence narration gets, and it takes less of it.
        var segments = Build("<p>\"You are late,\" she snapped.</p>", sceneEmotion: "angry");

        var prose = Assert.Single(segments, s => s.Kind == NarrationSegmentKind.Narration);
        Assert.Equal("angry", prose.Direction.Key);
        Assert.Equal(DirectionSource.Scene, prose.Direction.Source);
        Assert.True(prose.Direction.Vector.Values.Sum() < segments[0].Direction.Vector.Values.Sum());
    }

    [Fact]
    public void Build_SceneIntensityScalesTheDirection()
    {
        var calm = Build("<p>\"Go.\" said Mira.</p>", "angry", -10);
        var unbearable = Build("<p>\"Go.\" said Mira.</p>", "angry", 10);

        Assert.True(
            calm[0].Direction.Vector.Values.Sum() < unbearable[0].Direction.Vector.Values.Sum());
    }

    [Fact]
    public void Build_HonoursTheWritersOwnDirectionOnEitherKindOfSegment()
    {
        const string html = "<p>The tide turned. \"You are late,\" Mira snapped.</p>";
        var plain = Build(html);
        var prose = plain.First(s => s.Kind == NarrationSegmentKind.Narration);
        var spoken = plain.Single(s => s.Kind == NarrationSegmentKind.Dialogue);

        var segments = Build(
            html,
            directions: new Dictionary<string, string>
            {
                [prose.Key] = "sorrowful",
                [spoken.Key] = "joyful"
            });

        Assert.Equal(
            "sorrowful",
            segments.First(s => s.Kind == NarrationSegmentKind.Narration).Direction.Key);
        var directed = segments.Single(s => s.Kind == NarrationSegmentKind.Dialogue);
        Assert.Equal("joyful", directed.Direction.Key);
        Assert.Equal(DirectionSource.Writer, directed.Direction.Source);
    }

    [Fact]
    public void Build_ADirectionSetOnAnotherLineIsNotAppliedToThisOne()
    {
        var segments = Build(
            "<p>\"You are late,\" Mira snapped.</p>",
            directions: new Dictionary<string, string> { ["n:deadbeef:0"] = "joyful" });

        Assert.Equal("angry", segments[0].Direction.Key);
    }

    [Fact]
    public void Build_WorksInALanguageWithoutWordBoundaries()
    {
        var segments = Build("<p>“你迟到了。”她低声说道。</p>", language: "zh-CN");

        var spoken = Assert.Single(segments, s => s.Kind == NarrationSegmentKind.Dialogue);
        Assert.Equal(DirectionSource.Verb, spoken.Direction.Source);
    }
}
