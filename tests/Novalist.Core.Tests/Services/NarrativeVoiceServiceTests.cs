using Novalist.Core.Services;
using Xunit;

namespace Novalist.Core.Tests.Services;

/// <summary>
/// Reading a scene's person and tense, and holding it against what the book
/// says it is.
///
/// The failure mode that matters here is a false positive: telling a writer a
/// scene is broken when it is four sentences long, or when the language does not
/// mark tense the way the check counts it. Most of these are about staying quiet.
/// </summary>
public class NarrativeVoiceServiceTests
{
    private static SceneAnalysisLexicon English() => SceneAnalysisLexicon.For("en")!;

    /// <summary>Prose long enough to be evidence, in the requested voice.</summary>
    private static string Prose(string sentence, int times = 12)
        => string.Join(" ", Enumerable.Repeat(sentence, times));

    [Fact]
    public void FirstPersonProseReadsAsFirstPerson()
    {
        var (reading, confidence) = NarrativeVoiceService.ReadPerson(
            Prose("I walked to the door and I opened it slowly."), English());

        Assert.Equal(NarrativeReading.First, reading);
        Assert.True(confidence > 0);
    }

    [Fact]
    public void ThirdPersonProseReadsAsThirdPerson()
    {
        var (reading, _) = NarrativeVoiceService.ReadPerson(
            Prose("She walked to the door and opened it slowly."), English());

        Assert.Equal(NarrativeReading.Third, reading);
    }

    [Fact]
    public void AShortSceneIsNotEvidenceOfAnything()
    {
        // Four sentences is not a narrative mode, and reporting one as a
        // violation is worse than reporting nothing.
        Assert.Equal(
            NarrativeReading.Unknown,
            NarrativeVoiceService.ReadPerson("I went out.", English()).Reading);
        Assert.Equal(
            NarrativeReading.Unknown,
            NarrativeVoiceService.ReadTense("She went out.", English()).Reading);
    }

    [Fact]
    public void PastAndPresentProseAreToldApart()
    {
        Assert.Equal(
            NarrativeReading.Past,
            NarrativeVoiceService.ReadTense(
                Prose("She was late, and she knew it, so she said nothing."), English()).Reading);

        Assert.Equal(
            NarrativeReading.Present,
            NarrativeVoiceService.ReadTense(
                Prose("She is late, and she knows it, so she says nothing."), English()).Reading);
    }

    [Fact]
    public void ALanguageThatDoesNotMarkTenseByVerbFormSaysSoRatherThanGuessing()
    {
        // Chinese marks tense with particles and context. Counting verb forms
        // there would produce a confident wrong answer, so the lexicon ships no
        // markers and the reading is Unknown.
        var chinese = SceneAnalysisLexicon.For("zh-CN");
        Assert.NotNull(chinese);
        Assert.Empty(chinese!.PastTenseMarkers);

        var (reading, confidence) = NarrativeVoiceService.ReadTense(Prose("她走了。"), chinese);
        Assert.Equal(NarrativeReading.Unknown, reading);
        Assert.Equal(0, confidence);
    }

    [Fact]
    public void NoLexiconMeansNoReading()
    {
        Assert.Equal(
            NarrativeReading.Unknown,
            NarrativeVoiceService.ReadPerson(Prose("She walked out."), null).Reading);
        Assert.Equal(
            NarrativeReading.Unknown,
            NarrativeVoiceService.ReadTense(Prose("She walked out."), null).Reading);
    }

    [Fact]
    public void ProseWithTooFewTenseMarkersIsNotJudged()
    {
        // Long enough to be evidence of a person, but with almost no verb forms
        // the lexicon knows - a description, say. Better silent than certain.
        var (reading, _) = NarrativeVoiceService.ReadTense(
            Prose("Rain, everywhere, over the black roofs and the empty square."), English());

        Assert.Equal(NarrativeReading.Unknown, reading);
    }

    [Fact]
    public void ABookThatDeclaresNothingCannotDriftOutOfIt()
    {
        Assert.Null(NarrativeVoiceService.CheckPerson("", Prose("I walked out."), English()));
        Assert.Null(NarrativeVoiceService.CheckTense("  ", Prose("She walked out."), English()));
        Assert.Null(NarrativeVoiceService.CheckPerson("rhubarb", Prose("I walked out."), English()));
    }

    [Fact]
    public void AFirstPersonSceneInAThirdPersonBookIsFlagged()
    {
        var check = NarrativeVoiceService.CheckPerson(
            "third-limited", Prose("I walked to the door and I opened it slowly."), English());

        Assert.NotNull(check);
        Assert.False(check!.Agrees);
        Assert.Equal(NarrativeReading.First, check.Reading);
    }

    [Fact]
    public void ASceneThatAgreesIsNotFlagged()
    {
        var person = NarrativeVoiceService.CheckPerson(
            "first", Prose("I walked to the door and I opened it slowly."), English());
        var tense = NarrativeVoiceService.CheckTense(
            "past", Prose("She was late, and she knew it, so she said nothing."), English());

        Assert.True(person!.Agrees);
        Assert.True(tense!.Agrees);
    }

    [Fact]
    public void AnUnreadableSceneAgreesWithWhateverTheBookSays()
    {
        // Silence is not disagreement. A scene too short to read counts as
        // agreeing, so nobody is warned about prose nothing was measured on.
        var check = NarrativeVoiceService.CheckPerson("first", "Out.", English());

        Assert.True(check!.Agrees);
        Assert.Equal(NarrativeReading.Unknown, check.Reading);
    }

    [Theory]
    [InlineData("first", NarrativeReading.First)]
    [InlineData("third", NarrativeReading.Third)]
    [InlineData("Third-Limited", NarrativeReading.Third)]
    [InlineData("third-omniscient", NarrativeReading.Third)]
    [InlineData("second", NarrativeReading.Unknown)]
    public void ADeclarationIsReadCaseInsensitively(string declared, NarrativeReading expected)
        => Assert.Equal(expected, NarrativeVoiceService.ParsePerson(declared));

    [Theory]
    [InlineData("past", NarrativeReading.Past)]
    [InlineData("PRESENT", NarrativeReading.Present)]
    [InlineData(null, NarrativeReading.Unknown)]
    public void ATenseDeclarationIsReadTheSameWay(string? declared, NarrativeReading expected)
        => Assert.Equal(expected, NarrativeVoiceService.ParseTense(declared));
}
