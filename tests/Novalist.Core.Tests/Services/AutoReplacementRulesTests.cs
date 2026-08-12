using Novalist.Core.Models;
using Novalist.Core.Services;
using Xunit;

namespace Novalist.Core.Tests.Services;

/// <summary>
/// What a writer's own replacement rules are allowed to be.
///
/// A rule reaches two engines that share no implementation - the editor as you
/// type, the cleanup pass over prose already written - so it is checked once,
/// before it is stored, rather than accepted by one and skipped by the other.
/// </summary>
public class AutoReplacementRulesTests
{
    private static AutoReplacementPair Literal(string start, string replace) => new()
    {
        Start = start,
        End = start,
        StartReplace = replace,
        EndReplace = replace
    };

    private static AutoReplacementPair Pattern(string pattern, string replace) => new()
    {
        Kind = AutoReplacementKinds.Regex,
        Start = pattern,
        StartReplace = replace
    };

    [Fact]
    public void APlainRuleIsAccepted()
        => Assert.Null(AutoReplacementRules.Validate(Literal("(c)", "©")));

    [Fact]
    public void APatternIsAccepted()
        => Assert.Null(AutoReplacementRules.Validate(Pattern(@"\bteh\b", "the")));

    [Fact]
    public void ARuleWithNothingToTriggerOnIsRefused()
        => Assert.Equal("empty", AutoReplacementRules.Validate(Literal(string.Empty, "x")));

    [Fact]
    public void ANullRuleIsRefusedRatherThanThrown()
        => Assert.Equal("empty", AutoReplacementRules.Validate(null!));

    [Fact]
    public void ATriggerLongerThanAnyRuleNeedsIsRefused()
        => Assert.Equal("tooLong",
            AutoReplacementRules.Validate(Literal(new string('x', 201), "y")));

    [Fact]
    public void AReplacementLongerThanAnyRuleNeedsIsRefused()
        => Assert.Equal("tooLong",
            AutoReplacementRules.Validate(Literal("x", new string('y', 201))));

    [Fact]
    public void APatternThatWillNotCompileIsRefused()
        => Assert.Equal("badPattern", AutoReplacementRules.Validate(Pattern("(unclosed", "x")));

    [Fact]
    public void APatternThatMatchesNothingIsRefused()
        // It would fire before every keystroke, forever, replacing the empty
        // string in front of the caret.
        => Assert.Equal("matchesNothing", AutoReplacementRules.Validate(Pattern("x*", "y")));

    [Fact]
    public void TheKindIsReadWithoutRegardToCase()
        => Assert.True(new AutoReplacementPair { Kind = "REGEX" }.IsRegex);

    [Fact]
    public void ARuleIsPlainTextUnlessItSaysOtherwise()
        // Every settings file written before custom rules existed omits the
        // kind, and has to keep working untouched.
        => Assert.False(new AutoReplacementPair { Start = "--" }.IsRegex);

    [Fact]
    public void SanitizeKeepsTheRulesThatCanRun()
    {
        var kept = AutoReplacementRules.Sanitize([
            Literal("(c)", "©"),
            Pattern("(unclosed", "x"),
            Literal(string.Empty, "y")
        ]);

        Assert.Equal("(c)", Assert.Single(kept).Start);
    }

    [Fact]
    public void APlainRuleRewritesEveryOccurrence()
        => Assert.Equal("© and ©", AutoReplacementRules.Apply("(c) and (c)", [Literal("(c)", "©")]));

    [Fact]
    public void APatternPutsBackWhatItCaptured()
        => Assert.Equal("12×9",
            AutoReplacementRules.Apply("12x9", [Pattern(@"(\d+)x(\d+)", "$1×$2")]));

    [Fact]
    public void RulesRunInTheOrderTheyAreWritten()
        => Assert.Equal("C",
            AutoReplacementRules.Apply("a", [Literal("a", "b"), Literal("b", "C")]));

    [Fact]
    public void ARuleThatCouldNotBeStoredIsNotRunEither()
        // A settings file edited by hand reaches this with rules the UI would
        // have refused, and the pass must skip them rather than throw over a
        // whole book.
        => Assert.Equal("(unclosed", AutoReplacementRules.Apply("(unclosed", [Pattern("(unclosed", "x")]));

    [Fact]
    public void APatternThatWouldNeverFinishGivesUpAndLeavesTheProse()
    {
        // Nested quantifiers against a line that nearly matches: the classic
        // shape that takes exponential time. A writer can save this - it is a
        // valid pattern that matches real text - so the cleanup pass has to
        // survive meeting it in the middle of a book.
        var runaway = Pattern("(a+)+b", "x");
        var prose = new string('a', 40) + "c";

        Assert.Null(AutoReplacementRules.Validate(runaway));
        Assert.Equal(prose, AutoReplacementRules.Apply(prose, [runaway]));
    }

    [Fact]
    public void NothingToCleanIsLeftAlone()
        => Assert.Equal(string.Empty, AutoReplacementRules.Apply(string.Empty, [Literal("a", "b")]));
}
