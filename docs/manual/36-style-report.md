# Style report

The **Style report** view runs a set of craft checks over your prose and tells you what your habits are. Open it from the activity bar (Plan group) or the command palette.

Everything here is computed offline from your own text and the word lists for your writing language. The same manuscript produces the same numbers every time, nothing is sent anywhere, and no AI is involved. You do not need an extension installed.

## What it measures

Pick a scope at the top: **Whole book**, **This chapter**, or **This scene**. Chapter and scene are only available when one is open.

### Headline numbers

- **Words** and **Sentences** for the chosen scope.
- **Mean sentence** — average sentence length in words.
- **Length variation** — the standard deviation of sentence length. This is the one worth watching: a low number over a long stretch is what makes prose read as monotonous, and it is almost invisible while you are writing it.
- **Longest sentence** — in words.

### Reports

Each report shows a count and a rate per 1000 words. Click one to see examples with surrounding context.

- **Adverbs** — words ending in an adverb suffix. Often a sign the verb is doing too little on its own. Common false positives ("only", "family", "reply") are excluded.
- **Filter words** — verbs that put the narrator between the reader and the scene: "she saw the door open" rather than "the door opened".
- **Weak verbs** — verbs that usually have a more specific alternative.
- **Passive voice** — an auxiliary followed by a participle. Deliberately conservative: it would rather miss a few than flag prose that is fine, because you cannot argue with a wrong flag.
- **Stock phrases** — clichés matched literally against a bundled list.
- **Sticky sentences** — sentences where function words crowd out the images. Short sentences are ignored, since a high function-word share is normal there and says nothing.
- **Repeated sentence openers** — three or more sentences in a row starting with the same word.

## Your own flagged words

Every check above is one Novalist brings. **Settings → Writing Assistance → Your flagged words** adds one of yours: a list, one word per line, counted exactly like the bundled checks and reported as its own row.

It is for the two things a bundled lexicon cannot know — your own crutches (*suddenly*, *just*, *somehow*) and the spelling a series bible fixes, where you want to be told every time the other one appears.

The list is per writer, not per project: habits follow you from book to book. Matching ignores case, and a word repeated in the list is counted once. With no words in the list the row does not appear at all — an empty row reading zero would look like a check that found nothing rather than one that was never set up.

## Language support

The word lists live in the same per-language analysis file as the Inspector's scene analysis, so a language is supported exactly when that file exists. English and German ship with full lists.

Where a language has no list for a given report, Novalist says **"No word list for &lt;language&gt;"** rather than showing a zero. A zero would read as "your prose is clean", which is a different and untrue statement. This follows the same rule as scene analysis: Novalist does not guess for a language it has no data for.

Two consequences worth knowing:

- **German has no adverb report.** German does not mark adverbs with a suffix, so there is nothing to count and the report says so.
- **Repeated sentence openers always works**, in every language, because it compares words to each other rather than to a list.

You can add or correct a language yourself by dropping an `analysis.<tag>.json` file into your `Analysis/` folder — the same mechanism used for scene analysis. See [Custom themes & language packs](34-custom-themes-and-languages.md).

## How to read it

These are prompts to look again, not rules. A high adverb count in a first draft is information, not an error, and some of the best sentences in your book will trip several reports at once. The numbers are most useful compared against yourself over time — the same chapter before and after a revision pass — rather than against an absolute target.

## Where to go next

- [Editor](05-editor.md) — grammar and spelling as you type, which is a different job from these reports.
- [Dashboard](11-dashboard.md) — word goals, pacing, and echo phrases across the book.
- [Dialogue](33-dialogue.md) — every line one character speaks, for catching voice drift.
- [Custom themes & language packs](34-custom-themes-and-languages.md) — adding a writing language's word lists.
