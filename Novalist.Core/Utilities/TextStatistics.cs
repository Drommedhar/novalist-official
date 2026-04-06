using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Novalist.Core.Utilities;

public sealed class TextStatisticsResult
{
    public int WordCount { get; init; }
    public int CharacterCount { get; init; }
    public int CharacterCountWithoutSpaces { get; init; }
    public int ReadingTimeMinutes { get; init; }
    public ReadabilityResult Readability { get; init; } = new();
}

public sealed class ReadabilityResult
{
    public int Score { get; init; }
    public ReadabilityLevel Level { get; init; } = ReadabilityLevel.VeryDifficult;
    public string Method { get; init; } = "N/A";
    public double WordsPerSentence { get; init; }
    public double CharactersPerWord { get; init; }
    public int SentenceCount { get; init; }
}

public enum ReadabilityLevel
{
    VeryEasy,
    Easy,
    Moderate,
    Difficult,
    VeryDifficult
}

public static partial class TextStatistics
{
    private const int DefaultWordsPerMinute = 200;

    public static TextStatisticsResult Calculate(string text, string language)
    {
        var cleanText = Normalize(text);
        var wordCount = CountWords(cleanText);
        var characterCount = cleanText.Length;
        var characterCountWithoutSpaces = CountCharactersWithoutSpaces(cleanText);

        return new TextStatisticsResult
        {
            WordCount = wordCount,
            CharacterCount = characterCount,
            CharacterCountWithoutSpaces = characterCountWithoutSpaces,
            ReadingTimeMinutes = EstimateReadingTime(wordCount),
            Readability = CalculateReadability(cleanText, language, wordCount, characterCountWithoutSpaces)
        };
    }

    public static int EstimateReadingTime(int wordCount, int wordsPerMinute = DefaultWordsPerMinute)
    {
        if (wordCount <= 0)
            return 0;

        return (int)Math.Ceiling(wordCount / (double)wordsPerMinute);
    }

    public static string FormatCompactCount(int count)
    {
        if (count >= 1_000_000)
            return $"{count / 1_000_000d:0.0}M";

        if (count >= 1_000)
            return $"{count / 1_000d:0.0}k";

        return count.ToString("N0", CultureInfo.InvariantCulture);
    }

    public static string FormatReadingTime(int minutes)
    {
        if (minutes < 1)
            return "<1 min";

        if (minutes < 60)
            return $"{minutes} min";

        var hours = minutes / 60;
        var remainingMinutes = minutes % 60;
        return remainingMinutes == 0 ? $"{hours} h" : $"{hours} h {remainingMinutes} m";
    }

    public static string FormatReadabilityScore(ReadabilityResult readability)
        => $"{readability.Score}/100";

    public static string FormatReadabilityLevel(ReadabilityLevel level)
        => level switch
        {
            ReadabilityLevel.VeryEasy => "Very easy",
            ReadabilityLevel.Easy => "Easy",
            ReadabilityLevel.Moderate => "Moderate",
            ReadabilityLevel.Difficult => "Difficult",
            _ => "Very difficult"
        };

    public static string GetReadabilityColor(ReadabilityLevel level)
        => level switch
        {
            ReadabilityLevel.VeryEasy => "#16A34A",
            ReadabilityLevel.Easy => "#22863A",
            ReadabilityLevel.Moderate => "#B08800",
            ReadabilityLevel.Difficult => "#C05621",
            _ => "#B91C1C"
        };

    private static ReadabilityResult CalculateReadability(string text, string language, int wordCount, int charCount)
    {
        var sentenceCount = CountSentences(text);
        if (wordCount == 0 || sentenceCount == 0)
        {
            return new ReadabilityResult
            {
                Score = 0,
                Level = ReadabilityLevel.VeryDifficult,
                Method = "N/A",
                SentenceCount = 0
            };
        }

        var syllableCount = EstimateSyllables(text, language);
        var wordsPerSentence = wordCount / (double)sentenceCount;
        var charsPerWord = charCount / (double)wordCount;

        double score;
        string method;

        switch (language)
        {
            case "en":
                score = 206.835 - (1.015 * wordsPerSentence) - (84.6 * (syllableCount / (double)wordCount));
                method = "Flesch-Kincaid (EN)";
                break;
            case "de-low":
            case "de-guillemet":
                score = 180 - wordsPerSentence - (58.5 * (syllableCount / (double)wordCount));
                method = "Amstad (DE)";
                break;
            case "fr":
                score = 207 - (1.015 * wordsPerSentence) - (73.6 * (syllableCount / (double)wordCount));
                method = "Flesch-Kincaid (FR)";
                break;
            case "es":
                score = 206.84 - (0.6 * wordsPerSentence) - (102 * (syllableCount / (double)wordCount));
                method = "Fernandez Huerta (ES)";
                break;
            case "it":
                score = (300d * sentenceCount - 10d * charCount) / wordCount;
                method = "Gulpease Index (IT)";
                break;
            case "pt":
                score = 206.84 - (0.6 * wordsPerSentence) - (102 * (syllableCount / (double)wordCount));
                method = "Flesch Adapted (PT)";
                break;
            case "ru":
                score = 206.835 - (1.3 * wordsPerSentence) - (60.1 * (syllableCount / (double)wordCount));
                method = "Flesch Adapted (RU)";
                break;
            case "pl":
            case "cs":
            case "sk":
                score = 100 - CalculateAri(wordCount, sentenceCount, charCount) * 3;
                method = $"ARI Adapted ({language.ToUpperInvariant()})";
                break;
            default:
                score = 100 - CalculateAri(wordCount, sentenceCount, charCount) * 3;
                method = "Universal (ARI-based)";
                break;
        }

        score = Math.Clamp(score, 0, 100);

        return new ReadabilityResult
        {
            Score = (int)Math.Round(score),
            Level = GetReadabilityLevel(score, language),
            Method = method,
            WordsPerSentence = Math.Round(wordsPerSentence, 1),
            CharactersPerWord = Math.Round(charsPerWord, 1),
            SentenceCount = sentenceCount
        };
    }

    private static ReadabilityLevel GetReadabilityLevel(double score, string language)
    {
        if (language == "it")
        {
            if (score >= 80) return ReadabilityLevel.VeryEasy;
            if (score >= 60) return ReadabilityLevel.Easy;
            if (score >= 40) return ReadabilityLevel.Moderate;
            if (score >= 20) return ReadabilityLevel.Difficult;
            return ReadabilityLevel.VeryDifficult;
        }

        if (score >= 90) return ReadabilityLevel.VeryEasy;
        if (score >= 70) return ReadabilityLevel.Easy;
        if (score >= 50) return ReadabilityLevel.Moderate;
        if (score >= 30) return ReadabilityLevel.Difficult;
        return ReadabilityLevel.VeryDifficult;
    }

    private static double CalculateAri(int wordCount, int sentenceCount, int charCount)
    {
        if (wordCount == 0 || sentenceCount == 0)
            return 0;

        return 4.71 * (charCount / (double)wordCount) + 0.5 * (wordCount / (double)sentenceCount) - 21.43;
    }

    private static string Normalize(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var withoutComments = CommentRegex().Replace(text, string.Empty);
        var builder = new StringBuilder(withoutComments.Length);
        foreach (var character in withoutComments)
        {
            if (character is '#' or '*' or '_' or '[' or ']' or '(' or ')' or '|' or '`')
                continue;

            builder.Append(character);
        }

        return builder.ToString();
    }

    private static int CountWords(string text)
        => WordRegex().Matches(text).Count;

    private static int CountCharactersWithoutSpaces(string text)
        => text.Count(character => !char.IsWhiteSpace(character));

    private static int CountSentences(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;

        var normalized = text.Replace("...", ".", StringComparison.Ordinal);
        var count = SentenceRegex().Split(normalized)
            .Count(part => !string.IsNullOrWhiteSpace(part) && part.Any(character => char.IsLetterOrDigit(character)));

        return Math.Max(1, count);
    }

    private static int EstimateSyllables(string text, string language)
    {
        var words = WordRegex().Matches(text).Select(match => match.Value.ToLowerInvariant());
        var total = 0;

        foreach (var word in words)
            total += CountSyllables(word, language);

        return Math.Max(total, 1);
    }

    private static int CountSyllables(string word, string language)
        => language switch
        {
            "en" => CountEnglishSyllables(word),
            "de-low" or "de-guillemet" => CountVowelGroups(word, "aeiou\u00E4\u00F6\u00FC", new[] { "ei", "ai", "au", "eu", "\u00E4u", "ie" }),
            "fr" => CountVowelGroups(word, "aeiouy\u00E0\u00E2\u00E4\u00E9\u00E8\u00EA\u00EB\u00EF\u00EE\u00F4\u00F9\u00FB\u00FC", new[] { "oi", "ai", "ei", "eu", "au", "ou", "ie" }),
            "es" or "it" or "pt" => CountVowelGroups(word, "aeiou\u00E0\u00E1\u00E2\u00E3\u00E4\u00E8\u00E9\u00EA\u00EB\u00EC\u00ED\u00EE\u00EF\u00F2\u00F3\u00F4\u00F5\u00F6\u00F9\u00FA\u00FB\u00FC", new[] { "ai", "ei", "oi", "ui", "au", "eu", "ou", "ia", "ie", "io", "iu", "ua", "ue", "uo" }),
            "ru" => CountVowelGroups(word, "\u0430\u0435\u0451\u0438\u043E\u0443\u044B\u044D\u044E\u044F", Array.Empty<string>()),
            "pl" or "cs" or "sk" => CountVowelGroups(word, "aeiouy\u00E1\u00E9\u00ED\u00F3\u00FA\u00FD\u00E0\u00E8\u00EC\u00F2\u00F9\u00E4\u00EB\u00EF\u00F6\u00FC", Array.Empty<string>()),
            _ => CountGenericSyllables(word)
        };

    private static int CountEnglishSyllables(string word)
    {
        var trimmed = word.EndsWith('e') ? word[..^1] : word;
        return CountVowelGroups(trimmed, "aeiouy", Array.Empty<string>());
    }

    private static int CountVowelGroups(string word, string vowels, IReadOnlyCollection<string> diphthongs)
    {
        var vowelSet = vowels.ToHashSet();
        var count = 0;
        var inGroup = false;

        foreach (var character in word)
        {
            var isVowel = vowelSet.Contains(char.ToLowerInvariant(character));
            if (isVowel && !inGroup)
            {
                count++;
            }

            inGroup = isVowel;
        }

        foreach (var diphthong in diphthongs)
        {
            count -= Regex.Matches(word, Regex.Escape(diphthong), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Count;
        }

        return Math.Max(1, count);
    }

    private static int CountGenericSyllables(string word)
    {
        var count = CountVowelGroups(
            word,
            "aeiouy\u00E0\u00E1\u00E2\u00E3\u00E4\u00E5\u00E6\u00E8\u00E9\u00EA\u00EB\u00EC\u00ED\u00EE\u00EF\u00F2\u00F3\u00F4\u00F5\u00F6\u00F8\u00F9\u00FA\u00FB\u00FC\u00FD\u00FF\u03B1\u03B5\u03B7\u03B9\u03BF\u03C5\u03C9\u0430\u0435\u0451\u0438\u043E\u0443\u044B\u044D\u044E\u044F",
            Array.Empty<string>());
        if (count > 0)
            return count;

        return Math.Max(1, (int)Math.Round(word.Length / 2.5));
    }

    [GeneratedRegex("%%[\\s\\S]*?%%|<!--[\\s\\S]*?-->", RegexOptions.CultureInvariant)]
    private static partial Regex CommentRegex();

    [GeneratedRegex("[\\p{L}\\p{N}]+(?:['’-][\\p{L}\\p{N}]+)*", RegexOptions.CultureInvariant)]
    private static partial Regex WordRegex();

    [GeneratedRegex("[.!?]+", RegexOptions.CultureInvariant)]
    private static partial Regex SentenceRegex();
}