using Novalist.Core.Utilities;
using Xunit;

namespace Novalist.Core.Tests.Utilities;

/// <summary>
/// Counting text in scripts that do not put spaces between words.
///
/// Novalist ships a Chinese interface and analysis lexicon and counted every
/// script by runs of letters, so a Chinese scene came out as a handful of words
/// and every figure built on the count - goals, targets, reading time - was
/// wrong for a language the app claims to support.
/// </summary>
public class ScriptAwareCountingTests
{
    [Fact]
    public void EnglishCountsTheWayItAlwaysDid()
    {
        Assert.Equal(5, ScriptAwareCounting.Count("She crossed the wet yard."));
        Assert.Equal(0, ScriptAwareCounting.Count("   "));
        Assert.Equal(0, ScriptAwareCounting.Count(null));
    }

    [Fact]
    public void HyphensAndApostrophesStayInsideOneWord()
        => Assert.Equal(3, ScriptAwareCounting.Count("O'Brien half-brother spoke"));

    [Fact]
    public void ChineseCountsPerCharacter()
    {
        // Twelve characters, twelve units - the convention Chinese publishers
        // use. Counted as runs of letters this was one word.
        Assert.Equal(12, ScriptAwareCounting.Count("她穿过潮湿的院子走向那扇门"[..12]));
    }

    [Fact]
    public void JapaneseAndKoreanCountPerCharacterToo()
    {
        Assert.Equal(5, ScriptAwareCounting.Count("かのじょは"));
        Assert.Equal(4, ScriptAwareCounting.Count("그녀는 문"));
    }

    [Fact]
    public void ThaiIsDividedByAnAverageRatherThanCountedAsOneWord()
    {
        // Ten Thai characters: an approximation, and vastly closer than one.
        var thai = new string('ก', 10);

        Assert.Equal(2, ScriptAwareCounting.Count(thai));
    }

    [Fact]
    public void MixedTextCountsEachPartOnce()
    {
        // A Chinese sentence with an English name in it: three characters and
        // one name, not one run and not the name twice.
        Assert.Equal(4, ScriptAwareCounting.Count("她见了 Mira"[..3] + " Mira"));
    }

    [Fact]
    public void ReadingTimeUsesCharactersForCjkAndWordsElsewhere()
    {
        // A thousand Chinese characters at 500 a minute is two minutes. At a
        // words-a-minute rate it would read as five times longer.
        Assert.Equal(2, ScriptAwareCounting.ReadingMinutes(new string('字', 1000)));

        // Four hundred English words at 200 a minute is two minutes.
        var english = string.Join(" ", Enumerable.Repeat("word", 400));
        Assert.Equal(2, ScriptAwareCounting.ReadingMinutes(english));
    }

    [Fact]
    public void AnythingWithProseInItTakesAtLeastAMinute()
    {
        // Reporting zero minutes for a paragraph is worse than rounding up.
        Assert.Equal(1, ScriptAwareCounting.ReadingMinutes("A short line."));
        Assert.Equal(0, ScriptAwareCounting.ReadingMinutes("   "));
    }

    [Fact]
    public void NonSpacingIsRecognisedByWhatTheTextIsMostlyIn()
    {
        Assert.True(ScriptAwareCounting.IsNonSpacing("她穿过潮湿的院子"));
        Assert.False(ScriptAwareCounting.IsNonSpacing("She crossed the wet yard."));
        // A Chinese name in an English sentence does not make it Chinese.
        Assert.False(ScriptAwareCounting.IsNonSpacing("She met 米拉 in the yard that evening."));
        Assert.False(ScriptAwareCounting.IsNonSpacing("   "));
    }
}
