using Novalist.Core.Services;
using Xunit;

namespace Novalist.Core.Tests.Services;

/// <summary>
/// Covers the one string a hand-written direction is stored as.
///
/// The property that matters most is the oldest one: every direction written
/// before the sliders existed is a bare emotion name, and all of them have to
/// go on meaning what they meant.
/// </summary>
public class DirectionCodecTests
{
    // ─── what was already stored ────────────────────────────────────

    [Fact]
    public void ANameIsAName_WhichIsWhatEveryDirectionEverStoredIs()
    {
        var code = DirectionCodec.Decode("angry");

        Assert.NotNull(code);
        Assert.Equal("angry", code!.Key);
        Assert.Null(code.Vector);
        Assert.Null(code.ReferenceClip);
    }

    [Fact]
    public void NothingStoredIsNothing_WhichIsNotTheSameAsPlainly()
    {
        // Null means the writer never said and the prose decides. Empty means
        // they said "read this plainly". Collapsing the two loses a decision.
        Assert.Null(DirectionCodec.Decode(null));
        Assert.Equal(string.Empty, DirectionCodec.Decode(string.Empty)!.Key);
        Assert.Equal(string.Empty, DirectionCodec.Decode("   ")!.Key);
    }

    // ─── the sliders ────────────────────────────────────────────────

    [Fact]
    public void AHandPushedVectorSurvivesTheRoundTrip()
    {
        var written = DirectionCodec.Encode(
            null, new Dictionary<string, double> { ["happy"] = 0.8, ["surprised"] = 0.3 });

        var code = DirectionCodec.Decode(written);

        Assert.Equal(0.8, code!.Vector!["happy"], 3);
        Assert.Equal(0.3, code.Vector["surprised"], 3);
    }

    [Fact]
    public void DimensionsAreWrittenInTheOrderTheEngineDeclaresThem()
    {
        // Two identical directions must be the same string, or the audiobook's
        // fingerprint changes because a dictionary enumerated differently and a
        // chapter is rendered again for nothing.
        var first = DirectionCodec.Encode(
            null, new Dictionary<string, double> { ["surprised"] = 0.3, ["happy"] = 0.8 });
        var second = DirectionCodec.Encode(
            null, new Dictionary<string, double> { ["happy"] = 0.8, ["surprised"] = 0.3 });

        Assert.Equal(first, second);
    }

    [Fact]
    public void ADimensionNoEngineTakes_IsDropped()
    {
        var code = DirectionCodec.Decode("v:happy=0.5,smug=0.9");

        Assert.Equal(["happy"], code!.Vector!.Keys);
    }

    [Theory]
    [InlineData("v:happy=2", 1)]
    [InlineData("v:happy=-3", 0)]
    public void AValueOutsideTheRange_IsBroughtInsideIt(string stored, double expected)
        => Assert.Equal(expected, DirectionCodec.Decode(stored)!.Vector!["happy"], 3);

    [Theory]
    [InlineData("v:happy")]
    [InlineData("v:=0.5")]
    [InlineData("v:happy=quite")]
    [InlineData("v:")]
    public void AVectorNobodyCanRead_IsNoVectorRatherThanAFault(string stored)
        => Assert.Null(DirectionCodec.Decode(stored)!.Vector);

    [Fact]
    public void AVectorOfNothing_IsNotWrittenAsAVector()
        => Assert.Equal("angry", DirectionCodec.Encode("angry", new Dictionary<string, double>()));

    [Fact]
    public void ADimensionAtZero_IsNotWorthWritingDown()
        => Assert.Equal(
            string.Empty,
            DirectionCodec.Encode(null, new Dictionary<string, double> { ["happy"] = 0 }));

    // ─── like that line ─────────────────────────────────────────────

    [Fact]
    public void AReferenceClipSurvivesTheRoundTrip()
    {
        var written = DirectionCodec.Encode(null, null, "a1b2c3.wav");

        Assert.Equal("a1b2c3.wav", DirectionCodec.Decode(written)!.ReferenceClip);
    }

    [Fact]
    public void ALineCanCarryBoth_ForAnEngineThatTakesOneAndNotTheOther()
    {
        var written = DirectionCodec.Encode(
            "angry", new Dictionary<string, double> { ["angry"] = 0.9 }, "a1b2.wav");

        var code = DirectionCodec.Decode(written);

        Assert.Equal("angry", code!.Key);
        Assert.Equal(0.9, code.Vector!["angry"], 3);
        Assert.Equal("a1b2.wav", code.ReferenceClip);
    }

    [Fact]
    public void AReferenceThatNamesNothing_IsNoReference()
        => Assert.Null(DirectionCodec.Decode("ref:   ")!.ReferenceClip);

    [Fact]
    public void AVectorWithNoNameBesideIt_StillDecodesToAVector()
    {
        var code = DirectionCodec.Decode("v:calm=0.4");

        Assert.Equal(string.Empty, code!.Key);
        Assert.Equal(0.4, code.Vector!["calm"], 3);
    }

    [Fact]
    public void NothingToSay_WritesTheNameAlone()
        => Assert.Equal("angry", DirectionCodec.Encode("angry"));

    [Fact]
    public void NothingAtAll_WritesNothing()
        => Assert.Equal(string.Empty, DirectionCodec.Encode(null));
}
