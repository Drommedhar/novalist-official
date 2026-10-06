using System.Text.RegularExpressions;
using Novalist.Core.Models;
using Novalist.Core.Utilities;

namespace Novalist.Core.Services;

public static partial class DialogueAttributor
{
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
