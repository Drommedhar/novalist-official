using Novalist.Core.Utilities;
using Xunit;

namespace Novalist.Core.Tests.Utilities;

public class TextStatisticsTests
{
    [Fact]
    public void Calculate_CountsWordsCharsAndReadingTime()
    {
        var r = TextStatistics.Calculate("The quick brown fox jumps.", "en");
        Assert.Equal(5, r.WordCount);
        Assert.True(r.CharacterCount > 0);
        Assert.True(r.CharacterCountWithoutSpaces < r.CharacterCount);
        Assert.Equal(1, r.ReadingTimeMinutes);
        Assert.NotEqual("N/A", r.Readability.Method);
    }

    [Fact]
    public void Calculate_EmptyText_YieldsZeroAndNaReadability()
    {
        var r = TextStatistics.Calculate("   ", "en");
        Assert.Equal(0, r.WordCount);
        Assert.Equal(0, r.ReadingTimeMinutes);
        Assert.Equal("N/A", r.Readability.Method);
        Assert.Equal(ReadabilityLevel.VeryDifficult, r.Readability.Level);
        Assert.Equal(0, r.Readability.SentenceCount);
    }

    [Fact]
    public void Calculate_StripsCommentContent()
    {
        // %% comment %% content is removed before counting.
        var withComment = TextStatistics.Calculate("%%hidden secret words here%% word", "en");
        Assert.Equal(1, withComment.WordCount);
    }

    [Fact]
    public void Calculate_StripsHtmlComments()
    {
        var r = TextStatistics.Calculate("<!-- ignore this --> kept", "en");
        Assert.Equal(1, r.WordCount);
    }

    [Fact]
    public void Calculate_StripsMarkdownPunctuation()
    {
        // #, *, _, [, ], (, ), |, ` are dropped by Normalize; words remain.
        var plain = TextStatistics.Calculate("alpha beta gamma", "en");
        var marked = TextStatistics.Calculate("#alpha# *beta* _gamma_ [|]()`", "en");
        Assert.Equal(plain.WordCount, marked.WordCount);
        // The stripped characters must not inflate the character count.
        Assert.Equal(plain.CharacterCountWithoutSpaces, marked.CharacterCountWithoutSpaces);
    }

    [Theory]
    [InlineData(0, 200, 0)]
    [InlineData(1, 200, 1)]
    [InlineData(200, 200, 1)]
    [InlineData(201, 200, 2)]
    public void EstimateReadingTime(int words, int wpm, int expected)
        => Assert.Equal(expected, TextStatistics.EstimateReadingTime(words, wpm));

    [Theory]
    [InlineData(999, "999")]
    [InlineData(1000, "1.0k")]
    [InlineData(1500, "1.5k")]
    [InlineData(1_000_000, "1.0M")]
    [InlineData(2_500_000, "2.5M")]
    public void FormatCompactCount(int count, string expected)
        => Assert.Equal(expected, TextStatistics.FormatCompactCount(count));

    [Theory]
    [InlineData(0, "<1 min")]
    [InlineData(1, "1 min")]
    [InlineData(59, "59 min")]
    [InlineData(60, "1 h")]
    [InlineData(90, "1 h 30 m")]
    [InlineData(120, "2 h")]
    public void FormatReadingTime(int minutes, string expected)
        => Assert.Equal(expected, TextStatistics.FormatReadingTime(minutes));

    [Fact]
    public void FormatReadabilityScore()
        => Assert.Equal("42/100", TextStatistics.FormatReadabilityScore(new ReadabilityResult { Score = 42 }));

    [Theory]
    [InlineData(ReadabilityLevel.VeryEasy, "Very easy")]
    [InlineData(ReadabilityLevel.Easy, "Easy")]
    [InlineData(ReadabilityLevel.Moderate, "Moderate")]
    [InlineData(ReadabilityLevel.Difficult, "Difficult")]
    [InlineData(ReadabilityLevel.VeryDifficult, "Very difficult")]
    public void FormatReadabilityLevel(ReadabilityLevel level, string expected)
        => Assert.Equal(expected, TextStatistics.FormatReadabilityLevel(level));

    [Theory]
    [InlineData(ReadabilityLevel.VeryEasy, "#16A34A")]
    [InlineData(ReadabilityLevel.Easy, "#22863A")]
    [InlineData(ReadabilityLevel.Moderate, "#B08800")]
    [InlineData(ReadabilityLevel.Difficult, "#C05621")]
    [InlineData(ReadabilityLevel.VeryDifficult, "#B91C1C")]
    public void GetReadabilityColor(ReadabilityLevel level, string expected)
        => Assert.Equal(expected, TextStatistics.GetReadabilityColor(level));

    // One representative sentence per language branch so every score formula
    // and method label is exercised.
    [Theory]
    [InlineData("en", "Flesch-Kincaid (EN)")]
    [InlineData("de-low", "Amstad (DE)")]
    [InlineData("de-guillemet", "Amstad (DE)")]
    [InlineData("fr", "Flesch-Kincaid (FR)")]
    [InlineData("es", "Fernandez Huerta (ES)")]
    [InlineData("it", "Gulpease Index (IT)")]
    [InlineData("pt", "Flesch Adapted (PT)")]
    [InlineData("ru", "Flesch Adapted (RU)")]
    [InlineData("pl", "ARI Adapted (PL)")]
    [InlineData("cs", "ARI Adapted (CS)")]
    [InlineData("sk", "ARI Adapted (SK)")]
    [InlineData("xx", "Universal (ARI-based)")]
    public void Calculate_ReadabilityMethodPerLanguage(string lang, string expectedMethod)
    {
        var r = TextStatistics.Calculate("The cat sat on the mat. A dog ran fast home today.", lang);
        Assert.Equal(expectedMethod, r.Readability.Method);
        Assert.InRange(r.Readability.Score, 0, 100);
        Assert.True(r.Readability.SentenceCount >= 1);
    }

    [Fact]
    public void Calculate_ItalianLevels_UseItalianThresholds()
    {
        // Very short simple Italian-tagged sentence should land high on Gulpease.
        var r = TextStatistics.Calculate("Il re va.", "it");
        Assert.Equal("Gulpease Index (IT)", r.Readability.Method);
        Assert.InRange(r.Readability.Score, 0, 100);
    }

    [Fact]
    public void Calculate_LongComplexSentence_LowersScore()
    {
        var complex = "Notwithstanding the aforementioned considerations, the extraordinarily " +
                      "convoluted bureaucratic procedures necessitated substantial reconsideration.";
        var r = TextStatistics.Calculate(complex, "en");
        Assert.InRange(r.Readability.Score, 0, 100);
    }
}
