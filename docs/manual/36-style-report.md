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

### Sensory coverage

Five counts, one per sense: sight, sound, smell, taste, touch. They sit apart from the reports above because they are **not problems**. A count of sight words is not something to reduce.

The useful reading is the row showing **zero**. Nearly every writer defaults to sight and sound without noticing, and a single "how sensory is this" total would hide exactly that — so all five are always shown, always in the same order, and a sense the prose never reached is drawn dashed rather than dropped from the list.

Hover a sense for its rate per thousand words. Like every other report here, this is a word-list count in your writing language: a language with no sense lists shows a dash and says it is unsupported, rather than a zero that would read as prose with no senses in it.

## Point of view

With **Scene** selected, the report also reads the prose against the point of view recorded on that scene.

Novalist has always detected and stored a POV per scene and let you override it — and then nothing ever checked the prose against it, so a third-limited scene marked Mira could describe what Tomas was thinking with no warning.

The check looks for a character being named and then, within a few words, an interiority verb: *knew*, *felt*, *wondered*, *hoped*. That is the shape of head-hopping. Character **aliases count** — somebody named by their role is still that character, and the slip reads the same to a reader.

These are **questions, not errors**. Omniscient narration does this deliberately, and a word list cannot tell the difference. Nothing is flagged in the editor and nothing is counted as a problem; the list is there to be read and dismissed.

The POV character's own interiority is never flagged. That is the scene working as intended.

When the check **cannot** run it says so instead of reporting a clean scene — no POV recorded, no other characters to slip into, or no word list for your writing language. A zero from a check that never ran is the worse failure.

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

## Sentence by sentence, in the editor

The report grades a whole scene. For the same judgement one sentence at a time, turn on **Mark hard-to-read sentences** in the editor toolbar: it tints the difficult and very difficult sentences in place, using the same readability method. See [Readability marking](05-editor.md#readability-marking).

## Narration or dialogue

A drop-down beside the scope buttons chooses **Everything**, **Narration only**, or **Dialogue only**.

This matters more than it sounds. A character written to speak in cliches is not a writing problem, and a report that counts their lines alongside your narration says otherwise — which is the most common complaint about tools of this kind. Novalist has segmented dialogue precisely for a long time and never used it here.

**Narration only** takes every quoted line out and leaves a space where each one was, so the sentences either side of a cut stay whole. **Dialogue only** keeps just the quoted speech.

## Paragraph shape

Alongside the sentence figures, the report gives **paragraphs**, **mean paragraph** length, and **paragraph variation** — the standard deviation of paragraph length.

Sentence variation is the well-known one. A chapter of identically-sized paragraphs reads as flat for exactly the same reason, and is just as invisible while you are writing it. Higher is more varied; zero means every paragraph is the same length.

## Continuity

Everything else in the Style view reads the prose of one scene. This reads the book as a book, which is where a character standing two chapters after their own funeral actually shows up.

Every rule is deterministic and offline. No model is asked, and nothing is guessed from names — so an empty report means **these rules found nothing**, not that the book is right.

| Rule | What it looks for |
| --- | --- |
| Somebody appears after they are gone | An entry whose [state](06-codex.md) is marked **gone from here** in one scene, cast in a later one. The scene they leave in is not a finding — that is the writer's own sentence. |
| A scene casts an entry the Codex no longer has | A cast id left behind by a deleted entry. |
| Time runs backwards with nothing saying so | A scene dated earlier than the one the reader met last. A scene with a [narrative mode](04-chapters-and-scenes.md#how-a-scene-sits-in-time) — flashback, parallel — is not a finding, and a flashback does not drag the clock back for everything after it. |

Each rule has a tick beside it. Turning one off is remembered **with the project**, because a rule that is noise in a time-travel novel is exactly what somebody else needs. Click a finding to open the scene it is about.

Dates Novalist cannot read are skipped rather than guessed at: an in-world calendar date is not a contradiction.

## Where to go next

- [Editor](05-editor.md) — grammar and spelling as you type, which is a different job from these reports.
- [Dashboard](11-dashboard.md) — word goals, pacing, and echo phrases across the book.
- [Dialogue](33-dialogue.md) — every line one character speaks, for catching voice drift.
- [Custom themes & language packs](34-custom-themes-and-languages.md) — adding a writing language's word lists.
