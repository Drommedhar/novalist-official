using System.Text.RegularExpressions;

namespace Novalist.Core.Services;

/// <summary>One flagged span of prose, with enough context to find it.</summary>
public sealed class ProseStyleHit
{
    /// <summary>The word or phrase that matched, as written.</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>Character offset into the plain text the report ran over.</summary>
    public int Offset { get; init; }

    /// <summary>A short window of surrounding text, for display.</summary>
    public string Context { get; init; } = string.Empty;
}

/// <summary>Result of one report over one body of text.</summary>
public sealed class ProseStyleFinding
{
    /// <summary>Stable identifier the renderer localizes
    /// (<c>proseStyle.report.&lt;key&gt;</c>). Never shown raw.</summary>
    public string Key { get; init; } = string.Empty;

    /// <summary>How many times the report matched.</summary>
    public int Count { get; init; }

    /// <summary>Matches per 1000 words, rounded to one decimal. Zero when the
    /// report is not a density measure.</summary>
    public double Per1000Words { get; init; }

    /// <summary>
    /// False when the writing language has no data for this report. The UI says
    /// so rather than showing a zero, which would read as "clean".
    /// </summary>
    public bool Supported { get; init; } = true;

    /// <summary>Example matches, capped.</summary>
    public IReadOnlyList<ProseStyleHit> Examples { get; init; } = [];
}

/// <summary>
/// Which part of the prose a report is measured over.
///
/// A character written to speak in cliches is not a writing problem, and a
/// report that counts their dialogue alongside the narration says otherwise -
/// which is the most common complaint about tools of this kind. Novalist has
/// segmented dialogue with high fidelity all along and never used it for this.
/// </summary>
public enum ProseScope
{
    /// <summary>Everything. What the report has always measured.</summary>
    Everything,

    /// <summary>Narration only: every quoted line taken out.</summary>
    ProseOnly,

    /// <summary>Quoted speech only.</summary>
    DialogueOnly
}

/// <summary>Every report over one body of text.</summary>
public sealed class ProseStyleReport
{
    public string Language { get; init; } = "en";
    public int WordCount { get; init; }
    public int SentenceCount { get; init; }

    /// <summary>Mean sentence length in words.</summary>
    public double MeanSentenceWords { get; init; }

    /// <summary>
    /// Standard deviation of sentence length. Low variation over a long stretch
    /// is what makes prose read as monotonous, and it is invisible while writing.
    /// </summary>
    public double SentenceLengthStdDev { get; init; }

    public int LongestSentenceWords { get; init; }

    /// <summary>Which part of the prose this was measured over.</summary>
    public ProseScope Scope { get; init; } = ProseScope.Everything;

    public int ParagraphCount { get; init; }

    /// <summary>Mean paragraph length in words.</summary>
    public double MeanParagraphWords { get; init; }

    /// <summary>
    /// Standard deviation of paragraph length.
    ///
    /// Sentence variation is the well-known one; a chapter of identically-sized
    /// paragraphs reads as flat for the same reason and is just as invisible
    /// while writing it.
    /// </summary>
    public double ParagraphLengthStdDev { get; init; }

    public IReadOnlyList<ProseStyleFinding> Findings { get; init; } = [];

    /// <summary>
    /// One row per sense, in the order sight, sound, smell, taste, touch.
    ///
    /// Kept apart from <see cref="Findings"/> because these are not problems.
    /// A count of sight words is not something to reduce; the reading is which
    /// senses the prose forgot, and nearly every writer forgets the same three.
    /// </summary>
    public IReadOnlyList<ProseStyleFinding> Senses { get; init; } = [];
}

/// <summary>
/// Deterministic, offline craft reports over a body of prose: adverbs, filter
/// words, passive voice, weak verbs, cliches, sticky sentences, repeated
/// sentence openers, and sentence-length variation.
///
/// Nothing here is a model call or a network call - the results are the same on
/// every machine and every run, which is the point. Word lists come from the
/// per-language analysis lexicon, so a language with no list gets an honest
/// "not supported for this language" rather than a zero that reads as clean.
/// </summary>
public static partial class ProseStyleAnalyzer
{
    /// <summary>Matches at most this many examples per report.</summary>
    internal const int MaxExamples = 25;

    /// <summary>Characters of context shown either side of a hit.</summary>
    private const int ContextRadius = 40;

    /// <summary>A sentence is flagged sticky above this share of glue words.</summary>
    internal const double StickyGlueThreshold = 0.45;

    /// <summary>Sticky detection ignores very short sentences, where a high glue
    /// share is normal and says nothing.</summary>
    internal const int StickyMinWords = 8;

    /// <summary>How many consecutive sentences must share an opening word.</summary>
    internal const int RepeatedOpenerRun = 3;

    [GeneratedRegex(@"[^.!?]+[.!?]*", RegexOptions.Compiled)]
    private static partial Regex SentenceRegex();

    [GeneratedRegex(@"\p{L}[\p{L}\p{M}'’-]*", RegexOptions.Compiled)]
    private static partial Regex WordRegex();

    /// <summary>Runs of spaces and tabs, but never a line break: paragraph
    /// boundaries have to survive scoping.</summary>
    [GeneratedRegex(@"[ 	]{2,}", RegexOptions.Compiled)]
    private static partial Regex SpaceRunRegex();

    /// <summary>
    /// Runs the craft checks over some prose.
    /// </summary>
    /// <param name="watchWords">
    /// Words the writer asked to be told about - their own crutches, or the
    /// spellings a series bible fixes. Counted like any other word list, and
    /// reported under its own key so it can be shown as theirs rather than as
    /// one of ours.
    /// </param>
    /// <param name="scope">
    /// Which part of the prose to measure. Defaults to everything, which is what
    /// every caller written before scoping existed expects.
    /// </param>
    public static ProseStyleReport Analyze(
        string? text, string language, IReadOnlyCollection<string>? watchWords = null,
        ProseScope scope = ProseScope.Everything)
    {
        var plain = Scoped((text ?? string.Empty).Trim(), scope);
        var lexicon = SceneAnalysisLexicon.For(language);

        var sentences = SplitSentences(plain);
        var words = WordRegex().Matches(plain);
        var wordCount = words.Count;

        var lengths = sentences.Select(s => WordRegex().Matches(s.Text).Count).Where(n => n > 0).ToArray();

        var findings = new List<ProseStyleFinding>
        {
            WordListFinding("adverbs", plain, words, AdverbMatcher(lexicon), lexicon?.AdverbSuffixes.Count > 0),
            WordListFinding("filterWords", plain, words, SetMatcher(lexicon?.FilterWords), lexicon?.FilterWords.Count > 0),
            WordListFinding("weakVerbs", plain, words, SetMatcher(lexicon?.WeakVerbs), lexicon?.WeakVerbs.Count > 0),
            PassiveFinding(plain, sentences, wordCount, lexicon),
            ClicheFinding(plain, wordCount, lexicon),
            StickyFinding(sentences, wordCount, lexicon),
            RepeatedOpenersFinding(sentences, wordCount)
        };

        // Only when the writer has a list. An empty "your words" row reporting
        // zero would read as a check that found nothing rather than one that
        // was never set up.
        var watch = (watchWords ?? [])
            .Select(w => (w ?? string.Empty).Trim())
            .Where(w => w.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (watch.Count > 0)
            findings.Add(WordListFinding("watchWords", plain, words, SetMatcher(watch), true));

        // Paragraph shape, measured over the same scoped text: a chapter of
        // identically-sized paragraphs reads as flat for the same reason a run
        // of identically-sized sentences does.
        var paragraphs = ParagraphWordCounts(plain);

        return new ProseStyleReport
        {
            Language = lexicon?.Language ?? language,
            Senses = SenseFindings(plain, words, lexicon),
            WordCount = wordCount,
            SentenceCount = lengths.Length,
            MeanSentenceWords = lengths.Length == 0 ? 0 : Math.Round(lengths.Average(), 1),
            SentenceLengthStdDev = StdDev(lengths),
            LongestSentenceWords = lengths.Length == 0 ? 0 : lengths.Max(),
            Scope = scope,
            ParagraphCount = paragraphs.Count,
            MeanParagraphWords = paragraphs.Count == 0 ? 0 : Math.Round(paragraphs.Average(), 1),
            ParagraphLengthStdDev = StdDev([.. paragraphs]),
            Findings = findings
        };
    }

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

    /// <summary>
    /// The requested part of the text.
    ///
    /// Quoted runs are removed or kept whole rather than being re-flowed, so
    /// sentence boundaries either side of a cut stay where the writer put them.
    /// </summary>
    internal static string Scoped(string text, ProseScope scope)
    {
        if (scope == ProseScope.Everything || text.Length == 0) return text;

        var spans = Utilities.DialogueScanner.QuoteRegex.Matches(text);
        if (spans.Count == 0)
        {
            // No quoted speech at all: prose-only is the whole thing, and
            // dialogue-only is nothing rather than everything.
            return scope == ProseScope.ProseOnly ? text : string.Empty;
        }

        var output = new System.Text.StringBuilder();
        var cursor = 0;
        foreach (Match span in spans)
        {
            if (scope == ProseScope.ProseOnly)
            {
                output.Append(text, cursor, span.Index - cursor);
                // A space in place of the cut, so the words either side of a
                // removed line do not run together into one.
                output.Append(' ');
            }
            else
            {
                output.Append(span.Value).Append(' ');
            }
            cursor = span.Index + span.Length;
        }
        if (scope == ProseScope.ProseOnly) output.Append(text, cursor, text.Length - cursor);

        // Cutting a quoted line out from between two spaces leaves a run of
        // them. Harmless to the counts, but the scoped text is readable output
        // as much as it is input, so it comes back looking like prose.
        return SpaceRunRegex().Replace(output.ToString(), " ").Trim();
    }

    /// <summary>
    /// Paragraph lengths in words. A blank line is a paragraph boundary, which
    /// is what the plain-text projection of a scene leaves behind.
    /// </summary>
    internal static IReadOnlyList<int> ParagraphWordCounts(string text)
        => [.. text
            .Replace("\r\n", "\n")
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
            .Select(p => WordRegex().Matches(p).Count)
            .Where(count => count > 0)];

    private sealed record Sentence(string Text, int Offset);

    private static List<Sentence> SplitSentences(string text)
    {
        var result = new List<Sentence>();
        foreach (Match m in SentenceRegex().Matches(text))
        {
            var trimmed = m.Value.Trim();
            if (trimmed.Length > 0)
                result.Add(new Sentence(trimmed, m.Index));
        }
        return result;
    }

    private static double StdDev(IReadOnlyCollection<int> values)
    {
        if (values.Count < 2)
            return 0;

        var mean = values.Average();
        var variance = values.Sum(v => (v - mean) * (v - mean)) / values.Count;
        return Math.Round(Math.Sqrt(variance), 1);
    }

    private static double Density(int count, int wordCount) =>
        wordCount == 0 ? 0 : Math.Round(count * 1000.0 / wordCount, 1);

    private static string Context(string text, int offset, int length)
    {
        var start = Math.Max(0, offset - ContextRadius);
        var end = Math.Min(text.Length, offset + length + ContextRadius);
        var slice = text[start..end].Replace('\n', ' ').Replace('\r', ' ').Trim();
        return slice;
    }

    /// <summary>Word-level report driven by a predicate over the lowercased word.</summary>
    private static ProseStyleFinding WordListFinding(
        string key, string text, MatchCollection words, Func<string, bool>? predicate, bool? supported)
    {
        if (predicate == null || supported != true)
            return new ProseStyleFinding { Key = key, Supported = false };

        var hits = new List<ProseStyleHit>();
        var count = 0;
        foreach (Match w in words)
        {
            if (!predicate(w.Value.ToLowerInvariant()))
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
        string text, List<Sentence> sentences, int wordCount, SceneAnalysisLexicon? lexicon)
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

                for (var j = i + 1; j < Math.Min(i + 3, words.Count); j++)
                {
                    if (!LooksLikeParticiple(words[j].Value.ToLowerInvariant()))
                        continue;

                    count++;
                    if (hits.Count < MaxExamples)
                    {
                        var offset = sentence.Offset + words[i].Index;
                        var length = words[j].Index + words[j].Length - words[i].Index;
                        hits.Add(new ProseStyleHit
                        {
                            Text = sentence.Text[words[i].Index..(words[j].Index + words[j].Length)],
                            Offset = offset,
                            Context = Context(text, offset, length)
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
