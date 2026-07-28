using System.Text.RegularExpressions;
using Novalist.Core.Models;
using Novalist.Core.Utilities;

namespace Novalist.Core.Services;

/// <summary>How firmly a line is tied to the character it was assigned to. The
/// view shows this so the writer knows which attributions to trust and which to
/// check — a guessed line reads very differently from one the prose names.</summary>
public enum DialogueConfidence
{
    /// <summary>No speaker could be worked out.</summary>
    None,

    /// <summary>Alternation between two speakers in a back-and-forth run — a
    /// guess from position alone, with nothing in the prose confirming it.</summary>
    Low,

    /// <summary>A character is named beside the quote, but without a speech verb
    /// to say they are the one talking.</summary>
    Medium,

    /// <summary>The tag names no one but a pronoun, and exactly one character of
    /// that gender was named in the narration leading up to the line — so the
    /// pronoun can only refer to them.</summary>
    Inferred,

    /// <summary>A speech verb and a character name in the same breath, an
    /// author-confirmed entity mention in the dialogue tag, or a continuation of
    /// such a line inside the same paragraph.</summary>
    High,

    /// <summary>The writer assigned this line themselves.</summary>
    Manual
}

/// <summary>One character the line might belong to, with their share of the
/// total evidence. Shares across a line's candidates sum to 100, so a lone
/// candidate on thin evidence still reads as thin.</summary>
public sealed record DialogueCandidate(string CharacterId, int Percent);

/// <summary>A speaker verdict for one quoted line, plus the runners-up. The
/// candidate list is populated only where the verdict is worth second-guessing;
/// a line the prose names outright has nothing to choose between.</summary>
public sealed record DialogueAttribution(
    string? CharacterId,
    DialogueConfidence Confidence,
    IReadOnlyList<DialogueCandidate> Candidates);

/// <summary>The name forms one character answers to, precompiled for matching.</summary>
public sealed record DialogueSpeakerCandidate(
    string CharacterId, Regex Pattern, DialogueGender Gender);

/// <summary>The language-specific matchers attribution needs, built once per scan.</summary>
public sealed record DialogueLanguage(
    Regex SpeechVerbs, Regex MalePronouns, Regex FemalePronouns);

/// <summary>
/// Works out who speaks each quoted line in a scene.
///
/// Nothing here calls a model — the verdicts come from the prose itself. Each
/// candidate accumulates evidence weight from the signals below, the heaviest
/// wins, and the weights are normalised into the percentage shares the view
/// offers as one-click corrections:
///
/// <list type="bullet">
/// <item>an override the writer set (short-circuits everything else),</item>
/// <item>an author-confirmed <c>nv-entity-mention</c> span in the dialogue tag,</item>
/// <item>a speech verb beside a character's name,</item>
/// <item>a continuation of the previous line inside the same paragraph,</item>
/// <item>a speech verb whose subject is a pronoun that can only refer to one
/// character named in the narration above,</item>
/// <item>a bare name beside the line,</item>
/// <item>back-and-forth alternation in a two-hander,</item>
/// <item>and, as tie-breakers only, who the narration names nearby.</item>
/// </list>
///
/// A verdict is only recorded when the winning signal is one the prose actually
/// supports. Weak evidence produces no speaker at all — it becomes a ranked
/// suggestion instead, because a wrong speaker is worse for the writer than a
/// missing one.
/// </summary>
public static class DialogueAttributor
{
    // Evidence weights. The gaps between tiers matter more than the absolute
    // numbers: a signal must not be outvoted by a pile of weaker ones.
    private const int WeightMentionAfter = 100;
    private const int WeightMentionBefore = 90;
    private const int WeightContinuation = 80;
    private const int WeightVerbNameAfter = 70;
    private const int WeightVerbNameBefore = 64;
    private const int WeightPronoun = 44;
    private const int WeightNameAfter = 30;
    private const int WeightNameBefore = 26;
    private const int WeightAlternation = 20;
    private const int WeightNamedBefore = 6;
    private const int WeightNamedAfter = 3;
    private const int WeightSpeaksInScene = 2;

    /// <summary>The lightest signal that still counts as a verdict. Anything
    /// below it leaves the line unassigned with suggestions attached.</summary>
    private const int MinimumVerdictWeight = WeightAlternation;

    /// <summary>How far back the narration is read when resolving a pronoun or
    /// ranking candidates — roughly a couple of paragraphs.</summary>
    private const int NarrationWindow = 800;

    /// <summary>How many runners-up the view is offered.</summary>
    private const int MaxCandidates = 4;

    /// <summary>Builds the match patterns for a cast. Each character answers to
    /// their given name, their full name, and every alias in the Codex; longer
    /// forms are tried first so "Aldric Vane" beats the bare "Aldric".</summary>
    public static IReadOnlyList<DialogueSpeakerCandidate> BuildCandidates(
        IReadOnlyList<CharacterData> characters, bool wordBoundaries)
    {
        var candidates = new List<DialogueSpeakerCandidate>();
        foreach (var character in characters)
        {
            var names = new List<string>
            {
                EntityResolveIndex.Compose(character.Name, character.Surname),
                character.Name,
                character.Surname
            };
            names.AddRange(character.Aliases);

            var forms = names
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(n => n.Length)
                .ToArray();
            if (forms.Length == 0)
                continue;

            candidates.Add(new DialogueSpeakerCandidate(
                character.Id,
                BuildWordRegex(forms, wordBoundaries),
                SceneAnalysisLexicon.ClassifyGender(character.Gender)));
        }
        return candidates;
    }

    /// <summary>Assembles the language matchers from a lexicon. A language that
    /// ships none still attributes on names and mentions; it just never reaches
    /// a verb- or pronoun-backed verdict.</summary>
    public static DialogueLanguage BuildLanguage(SceneAnalysisLexicon? lexicon)
        => lexicon == null
            ? new DialogueLanguage(MatchNothing(), MatchNothing(), MatchNothing())
            : new DialogueLanguage(
                BuildSpeechVerbPattern(lexicon.SpeechVerbs, lexicon.WordBoundaries),
                lexicon.MalePronouns,
                lexicon.FemalePronouns);

    /// <summary>Compiles the language's speech verbs into one matcher. Matches
    /// nothing when the language ships no verb list, which keeps every verdict
    /// off verb evidence rather than inventing attributions.</summary>
    public static Regex BuildSpeechVerbPattern(IReadOnlyList<string> verbs, bool wordBoundaries)
        => verbs.Count == 0 ? MatchNothing() : BuildWordRegex(verbs, wordBoundaries);

    private static Regex MatchNothing() => new("(?!)", RegexOptions.CultureInvariant);

    private static Regex BuildWordRegex(IReadOnlyList<string> words, bool wordBoundaries)
    {
        // Longest first so "Aldric Vane" wins over the bare "Aldric" at the same spot.
        var alternation = string.Join(
            "|", words.OrderByDescending(w => w.Length).Select(Regex.Escape));
        var pattern = wordBoundaries
            ? $@"(?<![\p{{L}}\p{{N}}])(?:{alternation})(?![\p{{L}}\p{{N}}])"
            : $"(?:{alternation})";
        return new Regex(pattern, RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    }

    /// <summary>
    /// Assigns a speaker to every span in one scene, in document order.
    /// <paramref name="sceneText"/> is the scene's plain text, used to see which
    /// characters the narration names around each line. Overrides are keyed by
    /// <see cref="DialogueSpan.LineKey"/>; an override whose value is blank means
    /// the writer explicitly cleared the line, and it stays unassigned instead of
    /// being re-guessed.
    /// </summary>
    public static IReadOnlyList<DialogueAttribution> Attribute(
        IReadOnlyList<DialogueSpan> spans,
        string sceneText,
        IReadOnlyList<DialogueSpeakerCandidate> candidates,
        DialogueLanguage language,
        IReadOnlyDictionary<string, string>? overrides)
    {
        var known = candidates.Select(c => c.CharacterId).ToHashSet(StringComparer.Ordinal);
        var narration = BuildNarration(sceneText, spans);
        var results = new List<DialogueAttribution>(spans.Count);
        // The last two distinct speakers, newest first — the state an alternating
        // exchange needs to hand the next untagged line back to the right person.
        var recent = new List<string>();
        // The previous line's verdict, for same-paragraph continuation.
        DialogueAttribution? previous = null;
        var previousParagraph = -1;
        // Everyone credited with a line so far, a weak prior for the ranking.
        var speakersSoFar = new HashSet<string>(StringComparer.Ordinal);

        foreach (var span in spans)
        {
            var attribution = Resolve(
                span, narration, candidates, language, overrides, known, recent,
                previous, previousParagraph, speakersSoFar);
            results.Add(attribution);

            if (attribution.CharacterId != null)
            {
                speakersSoFar.Add(attribution.CharacterId);
                // Only a change of speaker advances the alternation state; two
                // consecutive lines by one character must not make them "recent" twice.
                if (recent.Count == 0 || recent[0] != attribution.CharacterId)
                    recent.Insert(0, attribution.CharacterId);
                if (recent.Count > 2)
                    recent.RemoveAt(2);
            }

            previous = attribution;
            previousParagraph = span.ParagraphIndex;
        }

        return results;
    }

    /// <summary>
    /// The scene text with every quoted passage blanked out. Antecedents are
    /// looked up here rather than in the raw text because a name inside a quote
    /// is nearly always the person being spoken *to* — "Guten Morgen, Liam"
    /// must not make Liam the antecedent of the next "er".
    /// </summary>
    private static string BuildNarration(string sceneText, IReadOnlyList<DialogueSpan> spans)
    {
        var chars = sceneText.ToCharArray();
        foreach (var span in spans)
        {
            for (var i = span.TextStart; i < span.TextEnd && i < chars.Length; i++)
            {
                if (chars[i] != '\n')
                    chars[i] = ' ';
            }
        }
        return new string(chars);
    }

    private static DialogueAttribution Resolve(
        DialogueSpan span,
        string narration,
        IReadOnlyList<DialogueSpeakerCandidate> candidates,
        DialogueLanguage language,
        IReadOnlyDictionary<string, string>? overrides,
        IReadOnlySet<string> known,
        IReadOnlyList<string> recent,
        DialogueAttribution? previous,
        int previousParagraph,
        IReadOnlySet<string> speakersSoFar)
    {
        if (overrides != null && overrides.TryGetValue(span.LineKey, out var manual))
        {
            // A blank override is the writer saying "not attributable" — honour it.
            return new DialogueAttribution(
                known.Contains(manual) ? manual : null, DialogueConfidence.Manual, []);
        }

        var scores = new Dictionary<string, int>(StringComparer.Ordinal);
        var reasons = new Dictionary<string, DialogueConfidence>(StringComparer.Ordinal);

        void Add(string? id, int weight, DialogueConfidence confidence)
        {
            if (id == null) return;
            scores[id] = scores.GetValueOrDefault(id) + weight;
            // Remember the strongest kind of evidence seen for this character,
            // so the verdict says how we know rather than merely how much.
            if (!reasons.TryGetValue(id, out var best) || confidence > best)
                reasons[id] = confidence;
        }

        // Author-confirmed markers first: these cannot be a false positive.
        Add(MatchMention(span.HtmlAfter, known), WeightMentionAfter, DialogueConfidence.High);
        Add(MatchMention(span.HtmlBefore, known), WeightMentionBefore, DialogueConfidence.High);

        // In the tag after a quote the speaker leads ("said Mira, ignoring
        // Aldric"), so the earliest name wins; in the lead-in before one the
        // speaker is whoever the verb belongs to, so the latest does.
        var after = MatchName(span.ContextAfter, candidates, language.SpeechVerbs, preferLate: false);
        var before = MatchName(span.ContextBefore, candidates, language.SpeechVerbs, preferLate: true);

        if (after.CharacterId != null)
        {
            Add(after.CharacterId,
                after.NearVerb ? WeightVerbNameAfter : WeightNameAfter,
                after.NearVerb ? DialogueConfidence.High : DialogueConfidence.Medium);
        }
        if (before.CharacterId != null)
        {
            Add(before.CharacterId,
                before.NearVerb ? WeightVerbNameBefore : WeightNameBefore,
                before.NearVerb ? DialogueConfidence.High : DialogueConfidence.Medium);
        }

        // A second quote in the same paragraph, with nobody else named between
        // them, is the same person still talking. It is only ever as good as the
        // line it continues, so it inherits that verdict's confidence.
        if (previous?.CharacterId != null
            && span.ParagraphIndex == previousParagraph
            && after.CharacterId == null
            && before.CharacterId == null
            && MatchMention(span.HtmlBefore, known) == null)
        {
            Add(previous.CharacterId, WeightContinuation,
                previous.Confidence == DialogueConfidence.Manual
                    ? DialogueConfidence.High
                    : previous.Confidence);
        }

        // "brummte er" — a verb whose subject is a pronoun. Resolvable only when
        // exactly one character of that gender was named in the narration above.
        var pronoun = ResolvePronoun(span, narration, candidates, language, after, before);
        Add(pronoun, WeightPronoun, DialogueConfidence.Inferred);

        // Untagged line in a two-hander: the turn goes back to whoever spoke
        // before last. Needs two distinct speakers on record to mean anything.
        if (recent.Count == 2 && after.CharacterId == null && before.CharacterId == null)
            Add(recent[1], WeightAlternation, DialogueConfidence.Low);

        AddProximityPriors(span, narration, candidates, speakersSoFar, Add);

        if (scores.Count == 0)
            return new DialogueAttribution(null, DialogueConfidence.None, []);

        var ranked = scores
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .ToArray();
        var winner = ranked[0];
        var confidence = winner.Value >= MinimumVerdictWeight
            ? reasons[winner.Key]
            : DialogueConfidence.None;
        var speaker = confidence == DialogueConfidence.None ? null : winner.Key;

        // A verdict the prose states outright leaves nothing to choose between;
        // anything weaker carries its runners-up for one-click correction.
        var suggestions = confidence == DialogueConfidence.High
            ? []
            : BuildShares(ranked);

        return new DialogueAttribution(speaker, confidence, suggestions);
    }

    /// <summary>Normalises the raw weights into percentage shares that sum to
    /// 100, so a candidate's number reads as "how much of the evidence points
    /// here" rather than as a bare score.</summary>
    private static IReadOnlyList<DialogueCandidate> BuildShares(
        IReadOnlyList<KeyValuePair<string, int>> ranked)
    {
        // Every signal adds a positive weight and this only runs with at least
        // one candidate scored, so the total is always safe to divide by.
        var top = ranked.Take(MaxCandidates).ToArray();
        var total = top.Sum(kv => kv.Value);

        var shares = new List<DialogueCandidate>(top.Length);
        var allocated = 0;
        for (var i = 0; i < top.Length; i++)
        {
            // The last share takes the rounding remainder so the row totals 100.
            var percent = i == top.Length - 1
                ? 100 - allocated
                : (int)Math.Round(100d * top[i].Value / total, MidpointRounding.AwayFromZero);
            allocated += percent;
            if (percent > 0)
                shares.Add(new DialogueCandidate(top[i].Key, percent));
        }
        return shares;
    }

    /// <summary>
    /// Resolves a pronoun-subject tag ("brummte er") to the character it can
    /// only be referring to: the narration must name exactly one character of
    /// that gender, and the tag itself must name nobody — otherwise the name
    /// rules already have it. Ambiguity yields nothing, so the line falls through
    /// to a suggestion rather than a guess.
    ///
    /// The narration above is tried first. Failing that — a scene that opens on
    /// "he" and only names him in the paragraph after — the narration below is
    /// tried on the same one-candidate terms.
    /// </summary>
    private static string? ResolvePronoun(
        DialogueSpan span,
        string narration,
        IReadOnlyList<DialogueSpeakerCandidate> candidates,
        DialogueLanguage language,
        NameHit after,
        NameHit before)
    {
        if (after.CharacterId != null || before.CharacterId != null)
            return null;

        // The pronoun has to sit in a tag, next to a speech verb — otherwise it
        // is just narration that happens to mention somebody.
        var tag = language.SpeechVerbs.IsMatch(span.ContextAfter) ? span.ContextAfter
            : language.SpeechVerbs.IsMatch(span.ContextBefore) ? span.ContextBefore
            : null;
        if (tag == null)
            return null;

        var male = language.MalePronouns.IsMatch(tag);
        var female = language.FemalePronouns.IsMatch(tag);
        // Neither, or both (German "sie" reads as she and they alike) is no help.
        if (male == female)
            return null;

        var gender = male ? DialogueGender.Male : DialogueGender.Female;
        return SoleMatch(Before(narration, span), candidates, gender)
            ?? SoleMatch(After(narration, span), candidates, gender);
    }

    /// <summary>The one character of this gender named in a stretch of
    /// narration, or null when none or several are.</summary>
    private static string? SoleMatch(
        string window, IReadOnlyList<DialogueSpeakerCandidate> candidates, DialogueGender gender)
    {
        string? found = null;
        foreach (var candidate in candidates)
        {
            if (candidate.Gender != gender || !candidate.Pattern.IsMatch(window))
                continue;
            if (found != null && found != candidate.CharacterId)
                return null;
            found = candidate.CharacterId;
        }
        return found;
    }

    private static string Before(string narration, DialogueSpan span)
        => narration[Math.Max(0, span.TextStart - NarrationWindow)..span.TextStart];

    private static string After(string narration, DialogueSpan span)
    {
        var from = Math.Min(narration.Length, span.TextEnd);
        return narration[from..Math.Min(narration.Length, from + NarrationWindow)];
    }

    /// <summary>
    /// Tie-breakers only. Being named in the narration around a line, or having
    /// spoken earlier in the scene, is far too weak to attribute on — but it is
    /// exactly what makes the suggestion list useful, and it is what puts the
    /// right name first for a line whose tag says nothing.
    /// </summary>
    private static void AddProximityPriors(
        DialogueSpan span,
        string narration,
        IReadOnlyList<DialogueSpeakerCandidate> candidates,
        IReadOnlySet<string> speakersSoFar,
        Action<string?, int, DialogueConfidence> add)
    {
        var back = Before(narration, span);
        var forward = After(narration, span);

        foreach (var candidate in candidates)
        {
            if (candidate.Pattern.IsMatch(back))
                add(candidate.CharacterId, WeightNamedBefore, DialogueConfidence.None);
            else if (candidate.Pattern.IsMatch(forward))
                add(candidate.CharacterId, WeightNamedAfter, DialogueConfidence.None);

            if (speakersSoFar.Contains(candidate.CharacterId))
                add(candidate.CharacterId, WeightSpeaksInScene, DialogueConfidence.None);
        }
    }

    /// <summary>The first character explicitly `@`-mentioned in a stretch of
    /// dialogue-tag markup. These spans are author-confirmed, so they cannot be
    /// a false positive the way a bare name match can.</summary>
    private static string? MatchMention(string html, IReadOnlySet<string> known)
    {
        if (html.Length == 0)
            return null;
        foreach (Match match in AppearanceIndexService.EntityIdRegex.Matches(html))
        {
            var id = match.Groups[1].Value;
            if (known.Contains(id))
                return id;
        }
        return null;
    }

    /// <summary>How close a name has to sit to a speech verb before the two are
    /// read as one dialogue tag. Wide enough for "Mira, still shaking, said",
    /// tight enough that a verb in the next clause does not reach back.</summary>
    private const int VerbProximity = 40;

    /// <summary>The name picked out of one stretch of context, and whether a
    /// speech verb sits close enough to make it a dialogue tag rather than an
    /// incidental mention.</summary>
    private readonly record struct NameHit(string? CharacterId, bool NearVerb);

    /// <summary>
    /// Picks the character most likely to own this stretch of prose. A name
    /// beside a speech verb wins outright; failing that the name nearest the
    /// quote does, which is the earliest in a trailing tag and the latest in a
    /// lead-in.
    /// </summary>
    private static NameHit MatchName(
        string context,
        IReadOnlyList<DialogueSpeakerCandidate> candidates,
        Regex speechVerbs,
        bool preferLate)
    {
        if (context.Length == 0)
            return new NameHit(null, false);

        var verbs = speechVerbs.Matches(context);
        string? best = null;
        var bestDistance = int.MaxValue;
        var bestPosition = 0;

        foreach (var candidate in candidates)
        {
            foreach (Match name in candidate.Pattern.Matches(context))
            {
                var distance = int.MaxValue;
                foreach (Match verb in verbs)
                {
                    // Gap between the two spans, zero when they touch or overlap.
                    var gap = Math.Max(
                        0,
                        Math.Max(name.Index - (verb.Index + verb.Length), verb.Index - (name.Index + name.Length)));
                    distance = Math.Min(distance, gap);
                }

                var better = distance < bestDistance
                    || (distance == bestDistance
                        && best != null
                        && (preferLate ? name.Index > bestPosition : name.Index < bestPosition))
                    || best == null;
                if (better)
                {
                    best = candidate.CharacterId;
                    bestDistance = distance;
                    bestPosition = name.Index;
                }
            }
        }

        return new NameHit(best, best != null && bestDistance <= VerbProximity);
    }
}
