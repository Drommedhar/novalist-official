using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Novalist.Sdk.Hooks;
using Novalist.Sdk.Services;

namespace Novalist.Extensions.AiAssistant.Services;

/// <summary>
/// Provides AI-powered grammar, punctuation, and style checking via an LLM.
/// Only sends changed sentences to keep prompts short and responses fast,
/// while retaining suggestions for unchanged sentences across checks.
/// </summary>
public sealed class AiGrammarCheckService : IGrammarCheckContributor
{
    private readonly IAiService _aiService;
    private bool _enabled = true;
    private string _lastCheckedText = string.Empty;
    private List<GrammarIssue> _lastIssues = [];

    public string GrammarCheckName => "AI Grammar Check";

    public bool IsGrammarCheckEnabled
    {
        get => _enabled;
        set => _enabled = value;
    }

    public AiGrammarCheckService(IAiService aiService)
    {
        _aiService = aiService;
    }

    /// <summary>
    /// Checks the text for grammar/punctuation/style issues using the LLM.
    /// Only sends sentences that changed since the last check, but merges
    /// results with retained issues from unchanged sentences.
    /// </summary>
    public async Task<GrammarCheckResult> CheckAsync(string plainText, string language, CancellationToken cancellationToken = default)
    {
        if (!_enabled || string.IsNullOrWhiteSpace(plainText))
        {
            _lastCheckedText = plainText;
            _lastIssues = [];
            return new GrammarCheckResult();
        }

        var oldSentences = SplitSentences(_lastCheckedText);
        var newSentences = SplitSentences(plainText);

        // Retain issues from sentences that haven't changed
        var retainedIssues = RetainIssuesFromUnchangedSentences(
            _lastIssues, _lastCheckedText, plainText, oldSentences, newSentences);

        _lastCheckedText = plainText;

        // Determine which sentences changed (by index) and need re-checking
        var changedIndices = GetChangedSentenceIndices(oldSentences, newSentences);
        var changedSentences = changedIndices
            .Where(i => i < newSentences.Count)
            .Select(i => newSentences[i])
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();

        List<GrammarIssue> newIssues = [];
        if (changedSentences.Count > 0)
        {
            var prompt = BuildPrompt(changedSentences, language);
            var messages = new List<AiChatMessage>
            {
                new() { Role = "user", Content = prompt }
            };

            try
            {
                var result = await _aiService.GenerateChatAsync(
                    messages,
                    temperature: 0.1,
                    cancellationToken: cancellationToken).ConfigureAwait(false);

                if (!cancellationToken.IsCancellationRequested)
                {
                    newIssues = ParseResponse(result.Response, plainText);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AiGrammarCheck] Error: {ex.Message}");
            }
        }

        // Merge retained issues with new ones, avoiding overlaps
        var merged = MergeIssues(retainedIssues, newIssues);
        _lastIssues = merged;
        return new GrammarCheckResult { Issues = merged };
    }

    /// <summary>
    /// Keeps issues whose sentence is still present unchanged in the new text,
    /// updating offsets to match the new text positions.
    /// </summary>
    private static List<GrammarIssue> RetainIssuesFromUnchangedSentences(
        List<GrammarIssue> oldIssues, string oldText, string newText,
        List<string> oldSentences, List<string> newSentences)
    {
        var retained = new List<GrammarIssue>();
        if (oldIssues.Count == 0 || string.IsNullOrEmpty(oldText)) return retained;

        var oldRanges = GetSentenceRanges(oldText, oldSentences);
        var newRanges = GetSentenceRanges(newText, newSentences);

        foreach (var issue in oldIssues)
        {
            // Find which old sentence this issue belongs to
            int oldSentIndex = -1;
            for (int i = 0; i < oldRanges.Count; i++)
            {
                var (start, end) = oldRanges[i];
                if (issue.Offset >= start && issue.Offset < end)
                {
                    oldSentIndex = i;
                    break;
                }
            }

            if (oldSentIndex < 0) continue;
            var oldSent = oldSentences[oldSentIndex];

            // Find the same sentence in the new text (by content)
            int newSentIndex = -1;
            for (int i = 0; i < newSentences.Count; i++)
            {
                if (string.Equals(newSentences[i], oldSent, StringComparison.Ordinal))
                {
                    newSentIndex = i;
                    break;
                }
            }

            if (newSentIndex < 0) continue; // Sentence was removed or changed

            // Extract the flagged text and try to locate it in the new sentence
            if (issue.Offset + issue.Length > oldText.Length) continue;
            var flaggedText = oldText.Substring(issue.Offset, issue.Length);

            var (newStart, newEnd) = newRanges[newSentIndex];
            var searchLen = Math.Max(0, newEnd - newStart);
            var newOffset = searchLen > 0
                ? newText.IndexOf(flaggedText, newStart, searchLen, StringComparison.Ordinal)
                : -1;

            if (newOffset < 0)
            {
                // Fallback: search entire new text
                newOffset = newText.IndexOf(flaggedText, StringComparison.Ordinal);
            }

            if (newOffset < 0) continue;

            retained.Add(new GrammarIssue
            {
                Message = issue.Message,
                Offset = newOffset,
                Length = issue.Length,
                Type = issue.Type,
                Replacements = issue.Replacements
            });
        }

        return retained;
    }

    /// <summary>
    /// Builds (start, end) character offsets for each sentence within the full text.
    /// </summary>
    private static List<(int Start, int End)> GetSentenceRanges(string text, List<string> sentences)
    {
        var ranges = new List<(int, int)>();
        int offset = 0;
        foreach (var sent in sentences)
        {
            var start = text.IndexOf(sent, offset, StringComparison.Ordinal);
            if (start < 0) start = offset;
            var end = start + sent.Length;
            ranges.Add((start, end));
            offset = end;
        }
        return ranges;
    }

    /// <summary>
    /// Returns indices of sentences that differ between old and new text.
    /// </summary>
    private static List<int> GetChangedSentenceIndices(List<string> oldSentences, List<string> newSentences)
    {
        var changed = new List<int>();
        int maxLen = Math.Max(oldSentences.Count, newSentences.Count);
        for (int i = 0; i < maxLen; i++)
        {
            var oldSent = i < oldSentences.Count ? oldSentences[i] : null;
            var newSent = i < newSentences.Count ? newSentences[i] : null;
            if (oldSent == null || newSent == null || !string.Equals(oldSent, newSent, StringComparison.Ordinal))
            {
                changed.Add(i);
            }
        }
        return changed;
    }

    /// <summary>
    /// Merges retained and new issues, dropping new ones that overlap with retained.
    /// </summary>
    private static List<GrammarIssue> MergeIssues(List<GrammarIssue> retained, List<GrammarIssue> newIssues)
    {
        var merged = new List<GrammarIssue>(retained);

        foreach (var newIssue in newIssues)
        {
            var overlaps = merged.Any(existing =>
                newIssue.Offset < existing.Offset + existing.Length &&
                newIssue.Offset + newIssue.Length > existing.Offset);

            if (!overlaps)
            {
                merged.Add(newIssue);
            }
        }

        return merged;
    }

    private static string BuildPrompt(List<string> sentences, string language)
    {
        var langName = language switch
        {
            "de" or "de-low" or "de-guillemet" => "German",
            "fr" => "French",
            "es" => "Spanish",
            "it" => "Italian",
            "pt" => "Portuguese",
            "nl" => "Dutch",
            "pl" => "Polish",
            "ru" => "Russian",
            _ => "English"
        };

        var textBlock = string.Join("\n", sentences.Select((s, i) => $"{i + 1}. {s}"));

        return $"""
            Check these sentences for grammar, punctuation, and style issues in {langName}.
            For each issue, respond with: line number | original | correction | brief reason
            Only list actual errors. Keep responses minimal. No explanations outside the list.

            {textBlock}
            """;
    }

    private static List<string> SplitSentences(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];

        var sentences = new List<string>();
        var sb = new StringBuilder();
        for (int i = 0; i < text.Length; i++)
        {
            sb.Append(text[i]);
            if (text[i] is '.' or '?' or '!')
            {
                if (i + 1 >= text.Length || char.IsWhiteSpace(text[i + 1]))
                {
                    var sent = sb.ToString().Trim();
                    if (sent.Length > 0)
                        sentences.Add(sent);
                    sb.Clear();
                }
            }
        }
        var remaining = sb.ToString().Trim();
        if (remaining.Length > 0)
            sentences.Add(remaining);

        return sentences;
    }

    private static List<GrammarIssue> ParseResponse(string response, string fullText)
    {
        var issues = new List<GrammarIssue>();
        if (string.IsNullOrWhiteSpace(response)) return issues;

        var lines = response.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0) continue;

            var parts = trimmed.Split('|', StringSplitOptions.TrimEntries);
            if (parts.Length < 3) continue;

            var original = parts[1];
            var replacement = parts[2];
            var message = parts.Length >= 4 ? parts[3] : "Suggestion";

            var offset = fullText.IndexOf(original, StringComparison.Ordinal);
            if (offset < 0) continue;

            issues.Add(new GrammarIssue
            {
                Message = message,
                Offset = offset,
                Length = original.Length,
                Type = GrammarIssueType.Style,
                Replacements = [replacement]
            });
        }

        return issues;
    }
}
