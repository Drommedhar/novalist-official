using System.Text.RegularExpressions;

namespace Novalist.Core.Services;

public static partial class ProseStyleAnalyzer
{
    /// <summary>The senses, in the order a report should read them.</summary>
    internal static readonly string[] SenseOrder = ["sight", "sound", "smell", "taste", "touch"];

    /// <summary>
    /// How much of each sense is in the prose.
    ///
    /// Always all five rows, always in the same order, even at zero: the row
    /// that reads zero is the whole point, and a list that omits the senses
    /// nobody used is a list that hides them.
    /// </summary>
    private static IReadOnlyList<ProseStyleFinding> SenseFindings(
        string plain, MatchCollection words, SceneAnalysisLexicon? lexicon)
    {
        var senses = lexicon?.Senses;
        return [.. SenseOrder.Select(sense =>
        {
            var list = senses != null && senses.TryGetValue(sense, out var w) ? w : null;
            // A language nobody has written the lists for reports as
            // unsupported rather than as prose with no senses in it.
            return WordListFinding(sense, plain, words, SetMatcher(list), list is { Count: > 0 });
        })];
    }

    /// <summary>Word-level report driven by a predicate over the lowercased word.</summary>
    private static ProseStyleFinding WordListFinding(
        string key, string text, MatchCollection words, Func<string, bool>? predicate, bool? supported,
        bool[]? ignoredOffsets = null)
    {
        if (predicate == null || supported != true)
            return new ProseStyleFinding { Key = key, Supported = false };

        var hits = new List<ProseStyleHit>();
        var count = 0;
        foreach (Match w in words)
        {
            if (!predicate(w.Value.ToLowerInvariant()))
                continue;
            if (ignoredOffsets != null && IsInside(w.Index, ignoredOffsets))
                continue;

            count++;
            if (hits.Count < MaxExamples)
                hits.Add(new ProseStyleHit
                {
                    Text = w.Value,
                    Offset = w.Index,
                    Context = Context(text, w.Index, w.Length)
                });
        }

        return new ProseStyleFinding
        {
            Key = key,
            Count = count,
            Per1000Words = Density(count, words.Count),
            Examples = hits
        };
    }

    private static Func<string, bool>? SetMatcher(IReadOnlyList<string>? list)
    {
        if (list == null || list.Count == 0)
            return null;
        var set = new HashSet<string>(list, StringComparer.Ordinal);
        return word => set.Contains(word);
    }

    private static Func<string, bool>? AdverbMatcher(SceneAnalysisLexicon? lexicon)
    {
        if (lexicon == null || lexicon.AdverbSuffixes.Count == 0)
            return null;

        var suffixes = lexicon.AdverbSuffixes;
        var exceptions = new HashSet<string>(lexicon.AdverbExceptions, StringComparer.Ordinal);
        return word =>
            word.Length > 3
            && !exceptions.Contains(word)
            && suffixes.Any(s => word.EndsWith(s, StringComparison.Ordinal));
    }

    /// <summary>
    /// Passive voice, matched as an auxiliary followed within two words by a
    /// participle-shaped word. A heuristic, not a parser: it is deliberately
    /// conservative, because a false "you wrote passive voice" is worse than a
    /// miss when the writer cannot argue with it.
    /// </summary>
    private static ProseStyleFinding PassiveFinding(
        string text, List<Sentence> sentences, int wordCount, SceneAnalysisLexicon? lexicon,
        bool[] dialogueMask)
    {
        if (lexicon == null || lexicon.PassiveAuxiliaries.Count == 0)
            return new ProseStyleFinding { Key = "passiveVoice", Supported = false };

        var auxiliaries = new HashSet<string>(lexicon.PassiveAuxiliaries, StringComparer.Ordinal);
        var hits = new List<ProseStyleHit>();
        var count = 0;

        foreach (var sentence in sentences)
        {
            var words = WordRegex().Matches(sentence.Text);
            for (var i = 0; i < words.Count; i++)
            {
                if (!auxiliaries.Contains(words[i].Value.ToLowerInvariant()))
                    continue;

                var auxiliaryOffset = sentence.Offset + words[i].Index;
                if (IsInside(auxiliaryOffset, dialogueMask))
                    continue;

                for (var j = i + 1; j < Math.Min(i + 3, words.Count); j++)
                {
                    if (!LooksLikeParticiple(words[j].Value.ToLowerInvariant()))
                        continue;

                    var length = words[j].Index + words[j].Length - words[i].Index;
                    if (IntersectsDialogue(auxiliaryOffset, length, dialogueMask))
                        continue;

                    count++;
                    if (hits.Count < MaxExamples)
                    {
                        hits.Add(new ProseStyleHit
                        {
                            Text = sentence.Text[words[i].Index..(words[j].Index + words[j].Length)],
                            Offset = auxiliaryOffset,
                            Context = Context(text, auxiliaryOffset, length)
                        });
                    }
                    break;
                }
            }
        }

        return new ProseStyleFinding
        {
            Key = "passiveVoice",
            Count = count,
            Per1000Words = Density(count, wordCount),
            Examples = hits
        };
    }

    /// <summary>English "-ed"/"-en" and German "ge-" participle shapes.</summary>
    private static bool LooksLikeParticiple(string word) =>
        word.Length > 3
        && (word.EndsWith("ed", StringComparison.Ordinal)
            || word.EndsWith("en", StringComparison.Ordinal)
            || word.StartsWith("ge", StringComparison.Ordinal));

    private static ProseStyleFinding ClicheFinding(string text, int wordCount, SceneAnalysisLexicon? lexicon)
    {
        if (lexicon == null || lexicon.Cliches.Count == 0)
            return new ProseStyleFinding { Key = "cliches", Supported = false };

        var lower = text.ToLowerInvariant();
        var hits = new List<ProseStyleHit>();
        var count = 0;

        foreach (var phrase in lexicon.Cliches)
        {
            var from = 0;
            while (from < lower.Length)
            {
                var at = lower.IndexOf(phrase, from, StringComparison.Ordinal);
                if (at < 0) break;

                count++;
                if (hits.Count < MaxExamples)
                    hits.Add(new ProseStyleHit
                    {
                        Text = text.Substring(at, Math.Min(phrase.Length, text.Length - at)),
                        Offset = at,
                        Context = Context(text, at, phrase.Length)
                    });
                from = at + phrase.Length;
            }
        }

        return new ProseStyleFinding
        {
            Key = "cliches",
            Count = count,
            Per1000Words = Density(count, wordCount),
            Examples = hits
        };
    }

    /// <summary>Sentences whose glue-word share is high enough that the images
    /// get lost between the function words.</summary>
    private static ProseStyleFinding StickyFinding(
        List<Sentence> sentences, int wordCount, SceneAnalysisLexicon? lexicon)
    {
        if (lexicon == null || lexicon.GlueWords.Count == 0)
            return new ProseStyleFinding { Key = "stickySentences", Supported = false };

        var glue = new HashSet<string>(lexicon.GlueWords, StringComparer.Ordinal);
        var hits = new List<ProseStyleHit>();
        var count = 0;

        foreach (var sentence in sentences)
        {
            var words = WordRegex().Matches(sentence.Text);
            if (words.Count < StickyMinWords)
                continue;

            var glueCount = words.Count(w => glue.Contains(w.Value.ToLowerInvariant()));
            if (glueCount / (double)words.Count < StickyGlueThreshold)
                continue;

            count++;
            if (hits.Count < MaxExamples)
                hits.Add(new ProseStyleHit
                {
                    Text = sentence.Text,
                    Offset = sentence.Offset,
                    Context = sentence.Text
                });
        }

        return new ProseStyleFinding
        {
            Key = "stickySentences",
            Count = count,
            Per1000Words = Density(count, wordCount),
            Examples = hits
        };
    }

    /// <summary>Runs of consecutive sentences opening on the same word. Language
    /// neutral, so it is always supported.</summary>
    private static ProseStyleFinding RepeatedOpenersFinding(List<Sentence> sentences, int wordCount)
    {
        var openers = sentences
            .Select(s => (Sentence: s, First: WordRegex().Match(s.Text)))
            .Where(x => x.First.Success)
            .Select(x => (x.Sentence, Word: x.First.Value.ToLowerInvariant()))
            .ToArray();

        var hits = new List<ProseStyleHit>();
        var count = 0;
        var runStart = 0;

        for (var i = 1; i <= openers.Length; i++)
        {
            var sameAsPrevious = i < openers.Length
                && string.Equals(openers[i].Word, openers[runStart].Word, StringComparison.Ordinal);
            if (sameAsPrevious)
                continue;

            var runLength = i - runStart;
            if (runLength >= RepeatedOpenerRun)
            {
                count++;
                if (hits.Count < MaxExamples)
                {
                    var first = openers[runStart].Sentence;
                    hits.Add(new ProseStyleHit
                    {
                        Text = openers[runStart].Word,
                        Offset = first.Offset,
                        Context = string.Join(
                            " ",
                            openers.Skip(runStart).Take(runLength).Select(o => o.Sentence.Text))
                    });
                }
            }
            runStart = i;
        }

        return new ProseStyleFinding
        {
            Key = "repeatedOpeners",
            Count = count,
            Per1000Words = Density(count, wordCount),
            Examples = hits
        };
    }
}
