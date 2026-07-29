using Novalist.Core.Services;
using Xunit;

namespace Novalist.Core.Tests.Services;

/// <summary>
/// The craft reports are deterministic and offline, so these assert exact
/// counts rather than ranges. A language with no word list must report the
/// report unsupported, never a zero that would read as clean prose.
/// </summary>
[Collection(LexiconStaticsCollection.Name)]
public class ProseStyleAnalyzerTests
{
    private static ProseStyleFinding Find(ProseStyleReport report, string key) =>
        report.Findings.Single(f => f.Key == key);

    [Fact]
    public void Analyze_EmptyText_ReportsNothingWithoutThrowing()
    {
        var report = ProseStyleAnalyzer.Analyze("", "en");

        Assert.Equal(0, report.WordCount);
        Assert.Equal(0, report.SentenceCount);
        Assert.Equal(0, report.MeanSentenceWords);
        Assert.Equal(0, report.SentenceLengthStdDev);
        Assert.Equal(0, report.LongestSentenceWords);
        Assert.All(report.Findings, f => Assert.Equal(0, f.Count));
    }

    [Fact]
    public void Analyze_NullText_IsTreatedAsEmpty()
    {
        Assert.Equal(0, ProseStyleAnalyzer.Analyze(null, "en").WordCount);
    }

    [Fact]
    public void Analyze_CountsWordsAndSentences()
    {
        var report = ProseStyleAnalyzer.Analyze("One two three. Four five! Six?", "en");

        Assert.Equal(6, report.WordCount);
        Assert.Equal(3, report.SentenceCount);
        Assert.Equal(2, report.MeanSentenceWords);
        Assert.Equal(3, report.LongestSentenceWords);
    }

    [Fact]
    public void Analyze_UniformSentencesHaveZeroVariation()
    {
        var report = ProseStyleAnalyzer.Analyze("A b c. D e f. G h i.", "en");
        Assert.Equal(0, report.SentenceLengthStdDev);
    }

    [Fact]
    public void Analyze_VariedSentencesHaveNonZeroVariation()
    {
        var report = ProseStyleAnalyzer.Analyze("Short. This one runs considerably longer than the first one did.", "en");
        Assert.True(report.SentenceLengthStdDev > 0);
    }

    [Fact]
    public void Analyze_SingleSentenceHasNoVariation()
    {
        Assert.Equal(0, ProseStyleAnalyzer.Analyze("Only one sentence here.", "en").SentenceLengthStdDev);
    }

    // ── Adverbs ──

    [Fact]
    public void Adverbs_CountsLyWordsAndReportsDensity()
    {
        var report = ProseStyleAnalyzer.Analyze("She walked slowly and spoke quietly.", "en");
        var adverbs = Find(report, "adverbs");

        Assert.True(adverbs.Supported);
        Assert.Equal(2, adverbs.Count);
        Assert.Contains(adverbs.Examples, e => e.Text == "slowly");
        Assert.Contains(adverbs.Examples, e => e.Text == "quietly");
        Assert.True(adverbs.Per1000Words > 0);
    }

    [Fact]
    public void Adverbs_SkipsListedExceptions()
    {
        var adverbs = Find(ProseStyleAnalyzer.Analyze("The only family reply.", "en"), "adverbs");
        Assert.Equal(0, adverbs.Count);
    }

    [Fact]
    public void Adverbs_UnsupportedWhereTheLanguageHasNoSuffix()
    {
        // German does not mark adverbs with a suffix, so the list is empty and
        // the report says so instead of reporting zero.
        var adverbs = Find(ProseStyleAnalyzer.Analyze("Sie ging langsam durch den Raum.", "de"), "adverbs");
        Assert.False(adverbs.Supported);
        Assert.Equal(0, adverbs.Count);
    }

    // ── Filter words ──

    [Fact]
    public void FilterWords_CountsNarratorInterposition()
    {
        var filters = Find(
            ProseStyleAnalyzer.Analyze("She saw the door open and felt the cold.", "en"), "filterWords");

        Assert.True(filters.Supported);
        Assert.Equal(2, filters.Count);
    }

    [Fact]
    public void FilterWords_GermanIsSupported()
    {
        var filters = Find(ProseStyleAnalyzer.Analyze("Sie sah die Tür.", "de"), "filterWords");
        Assert.True(filters.Supported);
        Assert.Equal(1, filters.Count);
    }

    // ── Weak verbs ──

    [Fact]
    public void WeakVerbs_AreCounted()
    {
        var weak = Find(ProseStyleAnalyzer.Analyze("He went there and got it.", "en"), "weakVerbs");
        Assert.Equal(2, weak.Count);
    }

    // ── Passive voice ──

    [Fact]
    public void PassiveVoice_MatchesAuxiliaryPlusParticiple()
    {
        var passive = Find(ProseStyleAnalyzer.Analyze("The door was opened by the guard.", "en"), "passiveVoice");

        Assert.True(passive.Supported);
        Assert.Equal(1, passive.Count);
        Assert.Contains("was opened", passive.Examples[0].Text);
    }

    [Fact]
    public void PassiveVoice_IgnoresPlainPastTense()
    {
        var passive = Find(ProseStyleAnalyzer.Analyze("The guard opened the door.", "en"), "passiveVoice");
        Assert.Equal(0, passive.Count);
    }

    [Fact]
    public void PassiveVoice_CountsEachSentenceAtMostOncePerAuxiliary()
    {
        var passive = Find(
            ProseStyleAnalyzer.Analyze("It was opened. It was closed. It was locked.", "en"), "passiveVoice");
        Assert.Equal(3, passive.Count);
    }

    // ── Cliches ──

    [Fact]
    public void Cliches_AreMatchedLiterallyAndCaseInsensitively()
    {
        var cliches = Find(
            ProseStyleAnalyzer.Analyze("At the end of the day, time stood still.", "en"), "cliches");

        Assert.True(cliches.Supported);
        Assert.Equal(2, cliches.Count);
    }

    [Fact]
    public void Cliches_RepeatedPhraseCountsEachOccurrence()
    {
        var cliches = Find(
            ProseStyleAnalyzer.Analyze("Crystal clear. Also crystal clear.", "en"), "cliches");
        Assert.Equal(2, cliches.Count);
    }

    // ── Sticky sentences ──

    [Fact]
    public void StickySentences_FlagHighGlueShare()
    {
        var sticky = Find(
            ProseStyleAnalyzer.Analyze(
                "It was the one that was in the box that was on the table by the door.", "en"),
            "stickySentences");

        Assert.True(sticky.Supported);
        Assert.Equal(1, sticky.Count);
    }

    [Fact]
    public void StickySentences_IgnoreShortSentences()
    {
        // Short sentences run glue-heavy as a matter of course; flagging them
        // would be noise.
        var sticky = Find(ProseStyleAnalyzer.Analyze("It was on the table.", "en"), "stickySentences");
        Assert.Equal(0, sticky.Count);
    }

    [Fact]
    public void StickySentences_IgnoreImageHeavySentences()
    {
        var sticky = Find(
            ProseStyleAnalyzer.Analyze(
                "Rain hammered cracked cobblestones while distant thunder rolled across blackened rooftops.", "en"),
            "stickySentences");
        Assert.Equal(0, sticky.Count);
    }

    // ── Repeated openers ──

    [Fact]
    public void RepeatedOpeners_FlagARunOfThree()
    {
        var repeated = Find(
            ProseStyleAnalyzer.Analyze("She ran. She stopped. She turned.", "en"), "repeatedOpeners");

        Assert.True(repeated.Supported);
        Assert.Equal(1, repeated.Count);
        Assert.Equal("she", repeated.Examples[0].Text);
    }

    [Fact]
    public void RepeatedOpeners_TwoInARowIsNotEnough()
    {
        var repeated = Find(
            ProseStyleAnalyzer.Analyze("She ran. She stopped. He turned.", "en"), "repeatedOpeners");
        Assert.Equal(0, repeated.Count);
    }

    [Fact]
    public void RepeatedOpeners_AreLanguageNeutral()
    {
        // No word list involved, so this one works even where nothing else does.
        var repeated = Find(
            ProseStyleAnalyzer.Analyze("Sie lief. Sie hielt an. Sie drehte sich um.", "de"), "repeatedOpeners");
        Assert.Equal(1, repeated.Count);
    }

    [Fact]
    public void RepeatedOpeners_FinalRunAtEndOfTextIsCounted()
    {
        var repeated = Find(
            ProseStyleAnalyzer.Analyze("A one. Then two. Then three. Then four.", "en"), "repeatedOpeners");
        Assert.Equal(1, repeated.Count);
    }

    // ── Unsupported languages ──

    [Fact]
    public void Analyze_UnknownLanguage_ReportsEveryWordListUnsupported()
    {
        var report = ProseStyleAnalyzer.Analyze("Some text here. More text here. Even more text.", "xx");

        Assert.False(Find(report, "adverbs").Supported);
        Assert.False(Find(report, "filterWords").Supported);
        Assert.False(Find(report, "weakVerbs").Supported);
        Assert.False(Find(report, "passiveVoice").Supported);
        Assert.False(Find(report, "cliches").Supported);
        Assert.False(Find(report, "stickySentences").Supported);
        // Language-neutral, so still measured.
        Assert.True(Find(report, "repeatedOpeners").Supported);
        Assert.True(report.WordCount > 0);
    }

    [Fact]
    public void Analyze_ExamplesAreCapped()
    {
        var text = string.Join(" ", Enumerable.Repeat("She moved slowly.", 60));
        var adverbs = Find(ProseStyleAnalyzer.Analyze(text, "en"), "adverbs");

        Assert.Equal(60, adverbs.Count);
        Assert.Equal(ProseStyleAnalyzer.MaxExamples, adverbs.Examples.Count);
    }

    [Fact]
    public void Analyze_ExamplesCarryContext()
    {
        var adverbs = Find(
            ProseStyleAnalyzer.Analyze("The horse moved slowly across the frozen field.", "en"), "adverbs");

        Assert.Contains("horse", adverbs.Examples[0].Context);
        Assert.True(adverbs.Examples[0].Offset > 0);
    }

    // ── The writer's own flagged words ──
    //
    // Novalist had no local flagged-word list at all: no way to catch every
    // "suddenly", or to hold one spelling of a series-bible term.

    [Fact]
    public void WatchWords_AreCountedAndReportedUnderTheirOwnKey()
    {
        var report = ProseStyleAnalyzer.Analyze(
            "Suddenly the door opened. She just stood there, and suddenly it closed.",
            "en",
            ["suddenly", "  just  ", "SUDDENLY", "   "]);

        var finding = Assert.Single(report.Findings, f => f.Key == "watchWords");
        // Case-insensitive, repeats in the list counted once, blanks ignored.
        Assert.Equal(3, finding.Count);
        Assert.True(finding.Supported);
        Assert.NotEmpty(finding.Examples);
    }

    [Fact]
    public void WatchWords_WithNoListThereIsNoRow()
    {
        // An empty "your words" row reporting zero would read as a check that
        // found nothing rather than one that was never set up.
        Assert.DoesNotContain(
            ProseStyleAnalyzer.Analyze("Suddenly it closed.", "en").Findings,
            f => f.Key == "watchWords");
        Assert.DoesNotContain(
            ProseStyleAnalyzer.Analyze("Suddenly it closed.", "en", ["  "]).Findings,
            f => f.Key == "watchWords");
    }
}
