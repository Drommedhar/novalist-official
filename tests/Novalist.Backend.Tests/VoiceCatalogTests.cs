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
}
