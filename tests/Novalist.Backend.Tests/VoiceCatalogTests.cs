using Novalist.Backend.Speech;
using Xunit;

namespace Novalist.Backend.Tests;

/// <summary>
/// Choosing among the system's voices.
///
/// The renderer used the browser's speech list, which on Windows reads one
/// voice store while everything a writer installs to get more voices registers
/// in the other. A machine offering every other application three hundred
/// voices offered Novalist three, and no setting could change it.
/// </summary>
public class VoiceCatalogTests
{
    private static SystemVoice V(string id, string name, string language) => new(id, name, language);

    // ── Language ──

    [Theory]
    [InlineData("407", "de-DE")]
    [InlineData("409", "en-US")]
    [InlineData("809", "en-GB")]
    public void ASingleLcidBecomesItsTag(string lcids, string expected)
        => Assert.Equal(expected, VoiceCatalog.LanguageFromLcidList(lcids));

    [Fact]
    public void AVoiceClaimingSeveralIsFiledUnderTheFirst()
        => Assert.Equal("en-GB", VoiceCatalog.LanguageFromLcidList("809;409;407"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-number")]
    public void NoUsableLcidIsNoLanguage(string? lcids)
        => Assert.Equal(string.Empty, VoiceCatalog.LanguageFromLcidList(lcids));

    [Fact]
    public void ALocaleThisMachineDoesNotKnowIsSkipped()
    {
        // Better no language than a wrong one: the picker groups it with the
        // rest rather than promising it speaks something.
        Assert.Equal("de-DE", VoiceCatalog.LanguageFromLcidList("FFFF;407"));
    }

    [Theory]
    [InlineData("de-DE", "de")]
    [InlineData("de", "de")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void ThePrimaryTagIsTheLanguagePart(string? tag, string expected)
        => Assert.Equal(expected, VoiceCatalog.Primary(tag));

    // ── Rate ──

    [Theory]
    [InlineData(1.0, 0)]
    [InlineData(2.0, 10)]
    [InlineData(0.5, -10)]
    public void NormalIsCentreAndDoubleIsTheEnd(double multiplier, int expected)
        => Assert.Equal(expected, VoiceCatalog.ToSapiRate(multiplier));

    [Theory]
    [InlineData(8.0)]
    [InlineData(100.0)]
    public void AFasterRateThanTheEngineHasIsClamped(double multiplier)
        => Assert.Equal(10, VoiceCatalog.ToSapiRate(multiplier));

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    public void ARateThatMeansNothingReadsAsNormal(double multiplier)
        => Assert.Equal(0, VoiceCatalog.ToSapiRate(multiplier));

    // ── Choosing ──

    private static readonly SystemVoice[] Installed =
    [
        V("id-hazel", "Microsoft Hazel", "en-GB"),
        V("id-katja", "Microsoft Katja (Natural)", "de-DE"),
        V("id-ingrid", "Microsoft Ingrid", "de-AT")
    ];

    [Fact]
    public void AChosenVoiceAlwaysWins()
    {
        // Even against the writing language: the writer said which one.
        var picked = VoiceCatalog.Choose(Installed, "id-hazel", "de");

        Assert.Equal("id-hazel", picked?.Id);
    }

    [Fact]
    public void WithNoChoiceTheWritingLanguageDecides()
        => Assert.Equal("id-katja", VoiceCatalog.Choose(Installed, null, "de-DE")?.Id);

    [Fact]
    public void TheRegionDoesNotHaveToMatch()
    {
        // A German writer does not care whether the voice is filed de-DE or
        // de-AT; they care that it is not English.
        Assert.StartsWith("de", VoiceCatalog.Choose(Installed, null, "de-CH")?.Language);
    }

    [Fact]
    public void AVoiceThatHasBeenUninstalledFallsBackToTheLanguage()
    {
        var picked = VoiceCatalog.Choose(Installed, "id-gone", "de");

        Assert.Equal("id-katja", picked?.Id);
    }

    [Fact]
    public void NothingSpeakingTheLanguageLeavesItToTheEngine()
    {
        // Better the engine's default than ours pretending to know.
        Assert.Null(VoiceCatalog.Choose(Installed, null, "ja"));
        Assert.Null(VoiceCatalog.Choose(Installed, null, null));
    }

    // ── Ordering ──

    [Fact]
    public void TheWritingLanguageLeadsAndTheRestFollowByName()
    {
        var ordered = VoiceCatalog.ForPicker(Installed, "de");

        // An adapter can expose several hundred voices, and a list of three
        // hundred in no order is one nobody reads to the end of.
        Assert.Equal(["Microsoft Ingrid", "Microsoft Katja (Natural)", "Microsoft Hazel"],
            ordered.Select(v => v.Name));
    }

    [Fact]
    public void WithNoWritingLanguageTheOrderIsJustTheNames()
    {
        var ordered = VoiceCatalog.ForPicker(Installed, null);

        Assert.Equal(["Microsoft Hazel", "Microsoft Ingrid", "Microsoft Katja (Natural)"],
            ordered.Select(v => v.Name));
    }

    [Fact]
    public void AnEmptyEngineOrdersToNothing()
        => Assert.Empty(VoiceCatalog.ForPicker([], "de"));

    // ── The engines on the other platforms ──

    // macOS `say -v '?'`, verbatim. The interesting rows are the ones with a
    // space in the name - there was no reading of these that took the first word
    // as the name and did not list four voices all called Eddy.
    private const string SayOutput =
        "Alex                en_US    # Most people recognize me by my voice.\n"
        + "Bad News            en_US    # The light you see at the end of the tunnel...\n"
        + "Eddy (English (UK)) en_GB    # Hello! My name is Eddy.\n"
        + "Anna                de_DE    # Hallo, ich heiße Anna.\n";

    [Fact]
    public void TheMacVoiceList_KeepsWholeNamesAndTheirLanguages()
    {
        var voices = VoiceCatalog.ParseSayVoices(SayOutput);

        Assert.Equal(
            ["Alex", "Bad News", "Eddy (English (UK))", "Anna"], voices.Select(v => v.Name));
        Assert.Equal(["en-US", "en-US", "en-GB", "de-DE"], voices.Select(v => v.Language));
        // `say -v` takes the name, and there is nothing else on offer to
        // identify a voice by.
        Assert.Equal(voices.Select(v => v.Name), voices.Select(v => v.Id));
    }

    [Fact]
    public void TheMacVoiceList_IgnoresWhatIsNotAVoice()
    {
        // A blank line, a heading, and a sample sentence that itself contains a
        // hash - the sentences are prose in the voice's own language.
        var voices = VoiceCatalog.ParseSayVoices(
            "\n# a comment\nAlex  en_US  # I said # once.\nnot a voice line\n"
            + "Voices:\nAnna  en_US_x  # a tag nobody writes\n");

        // "line" is four letters, which is a shape a language tag never
        // has - reading it as one listed the heading as a voice called
        // "line". Nor is a three-part tag one: nothing writes "en_US_x".
        Assert.Equal("Alex", Assert.Single(voices).Name);
    }

    [Fact]
    public void TheMacVoiceList_OfNothingIsNothing()
        => Assert.Empty(VoiceCatalog.ParseSayVoices(string.Empty));

    // espeak-ng `--voices`, verbatim. The names have spaces and brackets in
    // them, so the columns are read from the header rather than by counting
    // fields - counting puts half of "English (Great Britain)" in the next
    // column and calls the voice "English".
    private const string EspeakOutput =
        "Pty Language       Age/Gender VoiceName          File                 Other Languages\n"
        + " 5  af              --/M      Afrikaans          gmw/af\n"
        + " 5  de              --/M      German             gmw/de\n"
        + " 5  en-gb           --/M      English (Great Britain) gmw/en\n"
        + " 5  en-gb           --/M      English (Scotland) gmw/en-GB-scotland\n";

    [Fact]
    public void TheEspeakVoiceList_KeepsWholeNamesAndTheLanguageThatSelectsThem()
    {
        var voices = VoiceCatalog.ParseEspeakVoices(EspeakOutput);

        Assert.Equal(["Afrikaans", "German", "English (Great Britain)"], voices.Select(v => v.Name));
        // The id is the language code, because that is what -v takes and the
        // only column guaranteed to select the voice it names.
        Assert.Equal(["af", "de", "en-gb"], voices.Select(v => v.Id));
        Assert.Equal(voices.Select(v => v.Id), voices.Select(v => v.Language));
    }

    [Fact]
    public void TheEspeakVoiceList_ListsALanguageOnceRatherThanOncePerVariant()
    {
        // espeak knows dozens of variants of English. The picker wants voices,
        // and thirty entries all reading "-v en-gb" are one voice.
        Assert.Equal(3, VoiceCatalog.ParseEspeakVoices(EspeakOutput).Count);
    }

    [Fact]
    public void TheEspeakVoiceList_WithNoHeaderIsNothingRatherThanRubbish()
    {
        Assert.Empty(VoiceCatalog.ParseEspeakVoices("command not found\n"));
        Assert.Empty(VoiceCatalog.ParseEspeakVoices(string.Empty));
        // A header naming the columns in an order this cannot read is still not
        // an excuse to invent voices from it.
        Assert.Empty(VoiceCatalog.ParseEspeakVoices("VoiceName Language\n en  English\n"));
    }

    [Fact]
    public void TheEspeakVoiceList_SurvivesARowShorterThanItsColumns()
    {
        var voices = VoiceCatalog.ParseEspeakVoices(
            "Pty Language       Age/Gender VoiceName          File\n"
            + " 5  af              --/M      Afrikaans\n"
            + " 5\n"
            // A row with a name and no language, and one with a language
            // and no name. Neither selects anything, so neither is a voice.
            + " 5                  --/M      Nameless\n"
            + " 5  cy              --/M\n");

        Assert.Equal("Afrikaans", Assert.Single(voices).Name);
    }

    [Fact]
    public void ASpeakingRate_IsWordsPerMinuteAndStaysInsideWhatTheEngineTakes()
    {
        Assert.Equal(175, VoiceCatalog.ToWordsPerMinute(1.0, isSay: true));
        Assert.Equal(350, VoiceCatalog.ToWordsPerMinute(2.0, isSay: true));
        // espeak refuses anything above 450 and reads at its default instead, so
        // a writer who pushed Speed to 2x got a reading slower than at 1.5x -
        // which reads as the control being broken.
        Assert.Equal(450, VoiceCatalog.ToWordsPerMinute(4.0, isSay: false));
        Assert.Equal(700, VoiceCatalog.ToWordsPerMinute(4.0, isSay: true));
        Assert.Equal(80, VoiceCatalog.ToWordsPerMinute(0.01, isSay: false));
    }

    [Fact]
    public void ASpeakingRateThatIsNotOne_IsNormalRatherThanSilence()
    {
        foreach (var rubbish in new[] { double.NaN, 0.0, -1.0 })
            Assert.Equal(175, VoiceCatalog.ToWordsPerMinute(rubbish, isSay: true));
    }
}
