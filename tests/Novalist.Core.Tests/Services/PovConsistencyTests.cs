using Novalist.Core.Services;
using Xunit;

namespace Novalist.Core.Tests.Services;

/// <summary>
/// Head-hopping: a scene written in one character's point of view that reports
/// what somebody else is thinking.
///
/// Novalist detected and stored a POV per scene and let the writer override it,
/// and then nothing ever read the prose against it - so a third-limited scene
/// marked Mira could describe what Tomas was thinking with no warning.
/// </summary>
public class PovConsistencyTests
{
    private static PovReport Check(string text, string? pov = "Mira",
        IEnumerable<string>? others = null, string language = "en")
        => PovConsistency.Analyze(text, pov, others ?? ["Mira", "Tomas"], language);

    [Fact]
    public void SomebodyElseThinkingIsFound()
    {
        var report = Check("She crossed the yard. Tomas knew she would not come back.");

        Assert.True(report.Checked);
        var slip = Assert.Single(report.Slips);
        Assert.Equal("Tomas", slip.Name);
        Assert.Equal("knew", slip.Verb);
        Assert.Contains("Tomas knew", slip.Context);
    }

    [Fact]
    public void ThePovCharacterThinkingIsTheSceneWorking()
    {
        var report = Check("Mira knew she would not come back. Mira felt the cold.");

        // The whole point of a POV is that this character's interiority is
        // available. Flagging it would flag the scene for working.
        Assert.Empty(report.Slips);
        Assert.True(report.Checked);
    }

    [Fact]
    public void ANameFarFromTheVerbIsNotASlip()
    {
        var report = Check(
            "Tomas crossed the yard, closed the gate behind him, and much later she knew why.");

        // Only a name followed closely by the verb is the shape of the slip.
        // Anything wider matches a name and a verb in the same paragraph.
        Assert.Empty(report.Slips);
    }

    [Fact]
    public void ANameInsideAnotherWordIsNotAName()
    {
        var report = Check("The tomatoes knew nothing.", others: ["Mira", "Toma"]);

        Assert.Empty(report.Slips);
    }

    [Fact]
    public void SlipsComeBackInReadingOrder()
    {
        var report = Check(
            "Tomas felt the cold. She walked on. Tomas wondered why. Later Tomas hoped.");

        // The writer reads the scene in that order, and a list sorted by name
        // sends them back and forth through it.
        Assert.Equal(3, report.Slips.Count);
        Assert.True(report.Slips[0].Offset < report.Slips[1].Offset);
        Assert.True(report.Slips[1].Offset < report.Slips[2].Offset);
    }

    [Fact]
    public void OneRunawaySceneCannotFloodTheReport()
    {
        var text = string.Join(" ", Enumerable.Repeat("Tomas knew it.", 200));

        Assert.Equal(PovConsistency.MaxSlips, Check(text).Slips.Count);
    }

    // ─── When the answer would be meaningless ────────────────────────

    [Fact]
    public void ASceneWithNoPovIsNotChecked()
    {
        var report = Check("Tomas knew everything.", pov: "   ");

        // A zero from a check that never ran reads as a clean scene, which is
        // the worse failure.
        Assert.False(report.Checked);
        Assert.Equal(PovConsistency.NoPov, report.SkippedBecause);
        Assert.Empty(report.Slips);
    }

    [Fact]
    public void ACastOfOneHasNobodyToSlipInto()
    {
        var report = Check("Mira knew everything.", others: ["Mira"]);

        Assert.False(report.Checked);
        Assert.Equal(PovConsistency.NoOtherCast, report.SkippedBecause);
        Assert.Equal("Mira", report.Pov);
    }

    [Fact]
    public void ALanguageWithNoVerbListSaysSo()
    {
        var report = Check("Tomas knew everything.", language: "kl");

        Assert.False(report.Checked);
        Assert.Equal(PovConsistency.NoVerbList, report.SkippedBecause);
    }

    [Fact]
    public void AOneLetterNameIsNotMatched()
    {
        // A cast entry of "T" would match every T in the scene.
        var report = Check("Tomas knew everything.", others: ["Mira", "T"]);

        Assert.False(report.Checked);
        Assert.Equal(PovConsistency.NoOtherCast, report.SkippedBecause);
    }

    [Fact]
    public void TheSameNameTwiceIsOneName()
    {
        var report = Check("Tomas knew it.", others: ["Tomas", "tomas", " Tomas "]);

        Assert.Single(report.Slips);
    }

    [Fact]
    public void EmptyProseIsCheckedAndClean()
    {
        var report = Check(string.Empty);

        Assert.True(report.Checked);
        Assert.Empty(report.Slips);
    }

    [Fact]
    public void GermanInteriorityIsFoundToo()
    {
        var report = PovConsistency.Analyze(
            "Sie ging über den Hof. Tomas wusste, dass sie nicht zurückkäme.",
            "Mira", ["Mira", "Tomas"], "de");

        Assert.True(report.Checked);
        Assert.Equal("Tomas", Assert.Single(report.Slips).Name);
    }

    [Fact]
    public void ANullCastIsNoCast()
    {
        var report = PovConsistency.Analyze("Tomas knew.", "Mira", null, "en");

        Assert.False(report.Checked);
        Assert.Equal(PovConsistency.NoOtherCast, report.SkippedBecause);
    }
}
