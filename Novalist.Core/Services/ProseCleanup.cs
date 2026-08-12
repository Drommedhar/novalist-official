using System.Text;
using System.Text.RegularExpressions;
using Novalist.Core.Models;

namespace Novalist.Core.Services;

/// <summary>One thing a cleanup pass can put right.</summary>
public enum CleanupRule
{
    /// <summary>Straight quotes become the pair this language actually uses.</summary>
    SmartenQuotes,

    /// <summary>Two hyphens become an em dash, three dots become an ellipsis.</summary>
    Typography,

    /// <summary>The writer's own replacement rules, run over prose already written.</summary>
    CustomRules,

    /// <summary>Runs of spaces collapse to one, including the double space after a full stop.</summary>
    CollapseSpaces,

    /// <summary>Spaces left hanging at the start or end of a paragraph go.</summary>
    TrimParagraphs,

    /// <summary>Paragraphs holding nothing are dropped.</summary>
    DropEmptyParagraphs,

    /// <summary>A paragraph of asterisks, hyphens or hashes becomes the one scene break.</summary>
    NormaliseSceneBreaks
}

/// <summary>What a cleanup pass was asked to do.</summary>
public sealed class CleanupOptions
{
    /// <summary>Rules to run. Nothing runs when this is empty.</summary>
    public HashSet<CleanupRule> Rules { get; set; } = [];

    /// <summary>
    /// Which quote pair to smarten to. The book's writing language, because a
    /// German novel wants low-9 quotes and an English one wants raised ones -
    /// and getting this wrong is worse than leaving the straight quotes alone.
    /// </summary>
    public string Language { get; set; } = "en";

    /// <summary>
    /// The writer's own rules, for <see cref="CleanupRule.CustomRules"/>.
    ///
    /// Passed in rather than read from a preset, because these are the rules
    /// they wrote: the whole point is that they are not the preset.
    /// </summary>
    public IReadOnlyList<AutoReplacementPair> CustomRules { get; set; } = [];

    public bool Has(CleanupRule rule) => Rules.Contains(rule);
}

/// <summary>
/// A cleanup pass over prose the writer already has.
///
/// Auto-replacements fire while typing and deliberately skip pasted text, so
/// imported prose kept its straight quotes, its double spaces and its hyphen
/// pairs permanently. Find and Replace could be driven at each of them by hand,
/// one pattern at a time, if the writer knew what to look for.
///
/// Scene content is HTML, and every rule here has to leave the markup alone: a
/// straight quote inside class="..." is not a quotation mark, and collapsing
/// the spaces in a style attribute would rewrite the formatting.
/// </summary>
public static class ProseCleanup
{
    /// <summary>The scene break every writer emits, so a normalised one matches it.</summary>
    public const string SceneBreak = "* * *";

    /// <summary>Tags and text, so a rule can be run over the text alone.</summary>
    private static readonly Regex TagOrText = new(@"<[^>]*>|[^<]+", RegexOptions.Compiled);

    /// <summary>One block, kept whole so a paragraph rule can look at all of it.</summary>
    private static readonly Regex Block = new(@"<(p|h[1-6]|blockquote|li)\b[^>]*>.*?</\1>",
        RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase);

    /// <summary>A paragraph that is only asterisks, hyphens, hashes or bullets.</summary>
    private static readonly Regex SceneBreakLine = new(@"^[\s ]*[*\-#•_~]([\s ]*[*\-#•_~])*[\s ]*$",
        RegexOptions.Compiled);

    /// <summary>Runs the rules over a scene's HTML and hands back the result.</summary>
    public static string Apply(string html, CleanupOptions options)
    {
        if (string.IsNullOrEmpty(html) || options.Rules.Count == 0) return html;

        var result = ApplyToText(html, options);
        if (options.Has(CleanupRule.DropEmptyParagraphs) || options.Has(CleanupRule.NormaliseSceneBreaks))
            result = ApplyToBlocks(result, options);
        return result;
    }

    /// <summary>True when the pass would change this scene, without writing it.</summary>
    public static bool Changes(string html, CleanupOptions options)
        => !string.Equals(html, Apply(html, options), StringComparison.Ordinal);

    // ─── Rules over the text between the tags ────────────────────────

    private static string ApplyToText(string html, CleanupOptions options)
    {
        (string Open, string Close)? quotes = options.Has(CleanupRule.SmartenQuotes)
            ? QuotePair(options.Language)
            : null;
        // Whether the next straight quote opens or closes. Carried across text
        // runs on purpose: a quotation with emphasis inside it is three runs,
        // and restarting at each one would close a quotation that never opened.
        var open = true;
        var pairs = options.Has(CleanupRule.Typography)
            ? AutoReplacementDefaults.GetPreset(options.Language)
                .Where(p => p.Start != "'" && p.Start != "\"")
                .ToList()
            : [];

        return TagOrText.Replace(html, match =>
        {
            var token = match.Value;
            // A quote inside class="..." is not a quotation mark, and the
            // spaces in a style attribute are not prose.
            if (token.StartsWith('<')) return token;

            var text = token;
            foreach (var pair in pairs)
                if (!string.IsNullOrEmpty(pair.Start))
                    text = text.Replace(pair.Start, pair.StartReplace, StringComparison.Ordinal);

            // The writer's own rules run before the quote pass, so a rule that
            // produces a straight quote still gets curled like any other.
            if (options.Has(CleanupRule.CustomRules))
                text = AutoReplacementRules.Apply(text, options.CustomRules);

            if (quotes != null) text = Smarten(text, quotes.Value, ref open);
            if (options.Has(CleanupRule.CollapseSpaces)) text = CollapseSpaces(text);
            return text;
        });
    }

    /// <summary>
    /// Straight quotes become the language's pair, alternating.
    ///
    /// An apostrophe is not a closing quote: "don't" and "the boys' coats" are
    /// ordinary prose, and turning either into a quotation mark is worse than
    /// leaving every straight quote as it was.
    /// </summary>
    private static string Smarten(string text, (string Open, string Close) quotes, ref bool open)
    {
        var sb = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '"')
            {
                sb.Append(open ? quotes.Open : quotes.Close);
                open = !open;
            }
            else if (c == '\'')
            {
                var betweenLetters = i > 0 && char.IsLetter(text[i - 1])
                    && i + 1 < text.Length && char.IsLetter(text[i + 1]);
                var afterLetter = i > 0 && char.IsLetter(text[i - 1]);
                // Between letters is an elision; after one is a plural
                // possessive. Anything else is left alone rather than guessed.
                sb.Append(betweenLetters || afterLetter ? '’' : c);
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    private static string CollapseSpaces(string text)
    {
        var sb = new StringBuilder(text.Length);
        var lastWasSpace = false;
        foreach (var c in text)
        {
            // A no-break space is deliberate - French punctuation and a name
            // held together are both authored, not stray.
            var isSpace = c == ' ' || c == '\t';
            if (isSpace && lastWasSpace) continue;
            sb.Append(isSpace ? ' ' : c);
            lastWasSpace = isSpace;
        }
        return sb.ToString();
    }

    // ─── Rules over whole paragraphs ─────────────────────────────────

    private static string ApplyToBlocks(string html, CleanupOptions options)
        => Block.Replace(html, match =>
        {
            var inner = InnerText(match.Value);
            if (options.Has(CleanupRule.NormaliseSceneBreaks)
                && inner.Length > 0 && SceneBreakLine.IsMatch(inner))
                return "<p>" + SceneBreak + "</p>";

            if (options.Has(CleanupRule.DropEmptyParagraphs) && inner.Trim().Length == 0)
            {
                // An image or a horizontal rule holds no text and is still the
                // whole point of the paragraph it sits in.
                var stripped = Regex.Replace(match.Value, @"<[^>]*>", string.Empty);
                var carriesSomething = match.Value.Contains("<img", StringComparison.OrdinalIgnoreCase)
                    || match.Value.Contains("<hr", StringComparison.OrdinalIgnoreCase);
                if (stripped.Trim().Length == 0 && !carriesSomething) return string.Empty;
            }

            return options.Has(CleanupRule.TrimParagraphs) ? Trim(match.Value) : match.Value;
        });

    /// <summary>The words in a block, with the markup taken off.</summary>
    private static string InnerText(string block)
        => System.Net.WebUtility.HtmlDecode(Regex.Replace(block, @"<[^>]*>", string.Empty))
            .Replace(' ', ' ');

    /// <summary>Spaces left hanging inside the outer tags of a block.</summary>
    private static string Trim(string block)
    {
        var openEnd = block.IndexOf('>');
        var closeStart = block.LastIndexOf('<');
        if (openEnd < 0 || closeStart <= openEnd) return block;

        var head = block[..(openEnd + 1)];
        var body = block[(openEnd + 1)..closeStart];
        var tail = block[closeStart..];
        // Only the plain spaces: a &nbsp; a writer typed is content.
        return head + body.Trim(' ', '\t') + tail;
    }

    // ─── Quote pairs ─────────────────────────────────────────────────

    /// <summary>
    /// The opening and closing quote for a language, read from the same presets
    /// the typing-time replacements use - so a cleanup pass and a keystroke
    /// cannot disagree about what a German quotation looks like.
    /// </summary>
    private static (string Open, string Close) QuotePair(string language)
    {
        var pair = AutoReplacementDefaults.GetPreset(language)
            .FirstOrDefault(p => p.Start == "'" || p.Start == "\"");
        return pair == null || string.IsNullOrEmpty(pair.StartReplace)
            ? ("“", "”")
            : (pair.StartReplace, pair.EndReplace);
    }
}
