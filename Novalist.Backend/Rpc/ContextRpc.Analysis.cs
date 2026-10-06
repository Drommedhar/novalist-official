using System.Net;
using System.Text.RegularExpressions;
using Novalist.Core.Models;
using Novalist.Core.Services;
using Novalist.Core.Utilities;
using StreamJsonRpc;

namespace Novalist.Backend.Rpc;

public sealed partial class ContextRpc
{
    /// <summary>
    /// Where this scene reads differently from what the book declares, or null
    /// when there is nothing to say. Confidence rides along so a weak reading
    /// can be shown as a question rather than a verdict.
    /// </summary>
    private VoiceDriftDto? VoiceDrift(string content, SceneAnalysisLexicon? lexicon)
    {
        var book = _workspace.Projects.ActiveBook;
        if (book == null) return null;

        var person = NarrativeVoiceService.CheckPerson(book.NarrativePerson, content, lexicon);
        var tense = NarrativeVoiceService.CheckTense(book.Tense, content, lexicon);
        if (person == null && tense == null) return null;

        return new VoiceDriftDto(
            book.NarrativePerson,
            book.Tense,
            person?.Reading.ToString().ToLowerInvariant() ?? string.Empty,
            tense?.Reading.ToString().ToLowerInvariant() ?? string.Empty,
            person is { Agrees: false },
            tense is { Agrees: false },
            Math.Max(person?.Confidence ?? 0, tense?.Confidence ?? 0));
    }

    /// <summary>The project's writing language (the same setting that drives
    /// auto-replacements and the readability score).</summary>
    private string WritingLanguage()
    {
        var overrides = _workspace.Projects.ProjectRoot == null
            ? null
            : _workspace.Projects.ProjectSettings.Overrides;
        return overrides?.AutoReplacementLanguage
               ?? _workspace.Settings.Settings.AutoReplacementLanguage
               ?? "en";
    }

    /// <summary>Whether keyword-driven analysis (emotion, intensity, conflict, tags)
    /// is available for a language — that is, whether a lexicon ships for it.</summary>
    internal static bool SupportsKeywordAnalysis(string? language)
        => SceneAnalysisLexicon.Supports(language);

    private static string DetectPov(
        string content,
        IReadOnlyList<MatchedSource> currentSceneCharacters,
        ChapterData chapter,
        SceneData scene,
        SceneAnalysisLexicon? lexicon)
    {
        if (currentSceneCharacters.Count == 0)
        {
            // Without a lexicon there are no pronouns to count, so no first-person
            // guess; the character-name path below works in every language.
            return IsFirstPerson(content, lexicon)
                ? "pov.firstPerson"
                : string.Empty;
        }

        var bestMatch = currentSceneCharacters
            .Select(match => new
            {
                Match = match,
                Count = match.Source.FindMentionCount(content)
            })
            .OrderByDescending(entry => entry.Count)
            .ThenBy(entry => entry.Match.MatchIndex)
            .First();

        var character = (CharacterData)bestMatch.Match.Source.Entity;
        return ResolveCharacterDisplay(character, chapter, scene).Name;
    }

    private static SceneEmotionSnapshot DetectEmotion(
        string content, int intensity, SceneAnalysisLexicon lexicon)
    {
        var normalized = content.ToLowerInvariant();

        var best = lexicon.Emotions
            .Select(profile => new SceneEmotionSnapshot(
                profile.Key,
                profile.Key,
                profile.Words.Count(keyword => normalized.Contains(keyword, StringComparison.Ordinal))))
            .OrderByDescending(entry => entry.Score)
            .FirstOrDefault() ?? new SceneEmotionSnapshot(string.Empty, string.Empty, 0);

        if (best.Score <= 0)
        {
            // Fall back to a mood implied by the intensity, but only to a key the
            // lexicon actually declares.
            var fallback = intensity switch
            {
                <= -6 => "tense",
                >= 6 => "triumphant",
                _ => "neutral"
            };
            return lexicon.EmotionKeys.Contains(fallback, StringComparer.Ordinal)
                ? new SceneEmotionSnapshot(fallback, fallback, 1)
                : new SceneEmotionSnapshot(string.Empty, string.Empty, 0);
        }

        return best;
    }

    private static int ComputeIntensity(string content, SceneAnalysisLexicon lexicon)
    {
        var normalized = content.ToLowerInvariant();
        var positiveCount = lexicon.Positive.Count(keyword => normalized.Contains(keyword, StringComparison.Ordinal));
        var negativeCount = lexicon.Negative.Count(keyword => normalized.Contains(keyword, StringComparison.Ordinal));
        var conflictCount = lexicon.Conflict.Count(keyword => normalized.Contains(keyword, StringComparison.Ordinal));
        // Count the fullwidth exclamation mark too, for CJK prose.
        var exclamations = content.Count(character => character is '!' or '！');

        var score = ((positiveCount - negativeCount) * 2) - conflictCount;
        if (score > 0)
        {
            score += Math.Min(2, exclamations);
        }
        else if (score < 0)
        {
            score -= Math.Min(2, exclamations);
        }
        else if (conflictCount > 0)
        {
            score = -Math.Min(6, conflictCount + exclamations);
        }

        return Math.Clamp(score, -10, 10);
    }

    /// <summary>Four or more first-person pronouns reads as a first-person scene.</summary>
    private static bool IsFirstPerson(string content, SceneAnalysisLexicon? lexicon)
        => lexicon != null && lexicon.FirstPerson.Matches(content).Count >= 4;

    private static string ExtractConflictSnippet(string content, SceneAnalysisLexicon lexicon)
    {
        foreach (var sentence in ExtractSentences(content))
        {
            var normalized = sentence.ToLowerInvariant();
            if (!lexicon.Conflict.Any(keyword => normalized.Contains(keyword, StringComparison.Ordinal)))
            {
                continue;
            }

            return TrimExcerpt(sentence, 92);
        }

        return string.Empty;
    }

    private static IReadOnlyList<string> BuildSceneTags(
        string content,
        (int Characters, int Locations, int Items, int Lore) counts,
        (int Intensity, string Emotion, double DialogueRatio, int WordCount, string Conflict) analysis,
        SceneAnalysisLexicon? lexicon)
    {
        var tags = new List<string>();

        if (analysis.DialogueRatio >= 0.35)
        {
            tags.Add("sceneTag.dialogue");
        }

        if (Math.Abs(analysis.Intensity) >= 6)
        {
            tags.Add("sceneTag.highTension");
        }

        if (!string.IsNullOrWhiteSpace(analysis.Conflict))
        {
            tags.Add("sceneTag.conflict");
        }

        if (counts.Characters >= 3)
        {
            tags.Add("sceneTag.ensemble");
        }

        if (counts.Locations >= 2)
        {
            tags.Add("sceneTag.travel");
        }

        if (counts.Items + counts.Lore >= 2)
        {
            tags.Add("sceneTag.worldbuilding");
        }

        if (IsFirstPerson(content, lexicon))
        {
            tags.Add("sceneTag.interior");
        }

        if (analysis.WordCount >= 1200)
        {
            tags.Add("sceneTag.longScene");
        }

        if (!string.Equals(analysis.Emotion, "neutral", StringComparison.OrdinalIgnoreCase))
        {
            tags.Add($"emotion.{analysis.Emotion}");
        }

        return tags
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(4)
            .ToList();
    }

    private static double ComputeDialogueRatio(string content, int wordCount)
    {
        if (wordCount <= 0)
        {
            return 0;
        }

        var dialogueChars = DialogueRegex.Matches(content)
            .Select(match => match.Length)
            .Sum();

        // Whitespace-collapsed length is always positive when wordCount > 0, so the
        // Avalonia VM's extra totalChars<=0 guard is dead here and intentionally dropped.
        var totalChars = Regex.Replace(content, "\\s+", " ").Length;
        return Math.Clamp(dialogueChars / (double)totalChars, 0, 1);
    }

    private static double ComputeAverageSentenceLength(string content, int wordCount)
    {
        if (wordCount <= 0)
        {
            return 0;
        }

        var sentenceCount = Math.Max(1, SentenceRegex.Matches(content).Count);
        return Math.Round(wordCount / (double)sentenceCount, 1);
    }

    private static IEnumerable<string> ExtractSentences(string content)
        => SentenceRegex.Matches(content)
            .Select(match => match.Value.Trim())
            .Where(sentence => !string.IsNullOrWhiteSpace(sentence));

    private static int CountWords(string content)
        => WordRegex.Matches(content).Count;

    private static string NormalizeSceneContent(string content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return string.Empty;
        }

        if (!content.TrimStart().StartsWith('<'))
        {
            return content;
        }

        var text = Regex.Replace(content, "<[^>]+>", string.Empty);
        return WebUtility.HtmlDecode(text);
    }

    private static string TrimExcerpt(string value, int maxLength)
    {
        var normalized = value.Trim();
        if (normalized.Length <= maxLength)
        {
            return normalized;
        }

        return normalized[..Math.Max(0, maxLength - 3)].TrimEnd() + "...";
    }

    private sealed record EmotionProfile(string Key, string Label, IReadOnlyList<string> Keywords);

    private sealed record SceneEmotionSnapshot(string Key, string Label, int Score);
}
