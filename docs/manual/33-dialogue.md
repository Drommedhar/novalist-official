# Dialogue

The Dialogue view gathers every line one character speaks across the whole book and lays it out in story order. Read straight down and you are reading that person talking, and nothing else — no narration, no other voices, no scene breaks in between. That is what makes it useful: a shift in how somebody speaks is nearly invisible when their lines are eighty pages apart, and obvious when they are stacked in one column.

You can also fix things from here. A line can be rewritten in place and the change lands in the scene file, and a line the app attributed to the wrong person can be reassigned.

Open **Dialogue** from the **Plan** mode, under **Cast and time** in the mode panel, after the Relationships graph and the Calendar.

## The Dialogue view at a glance

The view has two panes:

- The **speakers** list on the left shows every character who has at least one line, most talkative first, with their line count. A filter box narrows it as you type. Below the cast sits **Unassigned** — the lines that could not be traced to anybody.
- The **lines** pane on the right shows the selected character's dialogue, grouped by story time.

Opening the view selects the character with the most lines, so it is never empty on arrival.

## How lines are grouped

Lines are grouped by the scene's **story date** — the in-world date on the scene, falling back to its chapter's (the same date the [Inspector](22-context-sidebar.md) and the Wiki's Appearances timeline show). Each group header is one point in story time; inside it, scenes appear as sub-headers with their chapter and scene name, and the lines follow in the order they are spoken.

Scenes are walked in manuscript order, and a new group starts whenever the story date changes. A scene with **no date** continues the group before it rather than being pushed to the end of the list — so scene order is the fallback, and an undated scene never jumps out of sequence. Undated scenes that come before any date at all form a leading group labelled **No story date**.

Each scene sub-header has an **open** button that jumps to that scene in the editor.

## How the speaker is worked out

Novalist reads the prose itself. There is no AI involved and nothing is sent anywhere — the whole pass runs offline over your scene files.

Every quoted passage is found first. Novalist recognises the quote styles it ships support for — `"…"`, `“…”`, `„…“`, `«…»`, `»…«`, `‹…›`, `‚…'` — so a German or French manuscript is read as correctly as an English one. Then each line collects evidence from the prose around it, and the strongest signal decides:

1. **A speaker you set yourself.** Always wins, and always survives a re-scan.
2. **An entity mention in the dialogue tag.** If you `@`-mentioned a character next to the line (the same links the editor's hover cards use), that is a statement you made deliberately, so it is trusted over anything guessed from the words.
3. **A speech verb beside a character's name.** "said Mira", "flüsterte Aldric", "阿德说道". The speech verbs come from your project's writing language, so this works in every language that ships an analysis lexicon (English, German, Simplified Chinese). Names match on the character's given name, full name, and every alias in the Codex.
4. **A second line in the same paragraph.** When a paragraph holds two quotes and the second has no tag of its own, it is the same person still talking. This carries only as much weight as the line it continues.
5. **A pronoun the narration can only mean one way.** For a tag like "brummte er" or "she said", Novalist looks at the narration around the line — the paragraphs above, and if nobody is named there, the paragraphs below. If exactly one character of that gender is named, the pronoun can only be them. If two are, nothing is assumed. This reads the character's **Gender** field in the Codex, so filling it in makes attribution noticeably better.
6. **A name beside the line with no speech verb.** Probably the speaker, but they might just as easily be the person being addressed.
7. **Back-and-forth alternation.** In a two-hander, an untagged line goes back to whoever spoke before last.

Names *inside* the quote marks are deliberately ignored — "Guten Morgen, Liam" is Liam being spoken to, not Liam speaking.

If none of that produces an answer, the line is left alone rather than guessed at, and lands under **Unassigned**.

Every line carries a label saying which of those it came from, so you always know how much to trust it:

| Label | Means |
| --- | --- |
| **Set by you** | You assigned this line by hand. |
| **Named** | A speech verb and a name, an entity mention you placed, or a continuation of such a line. |
| **Inferred** | A pronoun in the tag that could only have meant one character. |
| **Nearby** | A name beside the line, without a speech verb. |
| **Guessed** | Taken from alternation alone — check this one. |
| **Unknown** | No speaker could be found. |

If your project's writing language ships no analysis lexicon, attribution still works from names and mentions; it just never reaches **Named** on verb evidence alone, and pronoun tags are never resolved.

## Suggestions you can click

Wherever the prose does not name the speaker outright, the line shows a short row of **candidates** with a percentage each: *Might be: Liam 78% · Amy 22%*. Click one to assign the line to that character.

The percentage is that candidate's share of the evidence for the line, so the numbers across a row add up to 100%. It says how the evidence is split, not how likely the answer is in absolute terms — a single candidate at 100% only means nothing else was in the running, which for a line with one character nearby is a weak statement, not a confident one.

Candidates come from the same signals as the verdict, plus who the narration names near the line and who has already spoken in the scene. The character whose list you are reading is left out of their own row, since the chips are there to move a line somewhere else. Lines labelled **Named** or **Set by you** show no chips at all — there is nothing to second-guess.

## Correcting a speaker

Click a candidate chip to take its suggestion, or use the dropdown at the right of a line to pick anybody. The dropdown offers your whole cast, not only the characters who already have lines — so a character whose every line was misattributed is still pickable. Choosing **Unassigned** clears a wrong guess without naming a replacement.

Your choice is stored against the scene and marked **Set by you**. It survives re-scans and edits to the surrounding text, and it follows the line if you later rewrite its words. To hand a line back to automatic attribution, reassign it and then clear it again.

Reassigning a line moves it out of one character's list and into another's, so the view reloads and both counts update.

## Editing a line

Click a line to edit it. You are editing **only the words inside the quote marks** — the dialogue tag ("she said") and the narration around it stay exactly as they are. The greyed text beside each line shows that surrounding prose, so you can tell two similar lines apart without opening the scene.

Press `Enter` to save, `Shift+Enter` for a line break, or `Esc` to cancel. The change is written straight into the scene file, and the scene's word count is updated.

A few safeguards:

- **A snapshot is taken before every edit**, exactly as replace-all does, so you can revert an individual scene from the [Snapshots](17-snapshots.md) dialog.
- **Lines containing formatting cannot be edited here.** If a line has emphasis, an entity mention, or a footnote anchor inside the quote marks, rewriting it as plain text would destroy that markup — so those lines are shown read-only, and the open button takes you to the scene instead.
- **An edit is refused if the scene changed underneath it.** If you edited the same scene in the editor since the list was built, the save is rejected rather than applied to the wrong words, and a banner offers to reload.

## Where this data lives

Nothing is duplicated. The lines are read from your scene files each time you open the view, so they always reflect what is actually written. The only thing stored is the speakers you assigned by hand, which live alongside the rest of the scene's metadata in `.novalist/scenes.json`.

## Where to go next

- [Editor](05-editor.md) — dialogue punctuation correction, entity mentions, and the hover cards this view's attribution relies on.
- [Codex (Characters, Locations, Items, Lore)](06-codex.md) — character names, aliases, and the Gender field, which are what your prose is matched against.
- [Chapters & Scenes](04-chapters-and-scenes.md) — scene and chapter story dates, which decide how lines are grouped.
- [Snapshots](17-snapshots.md) — recovering a scene after an edit made here.
- [Inspector](22-context-sidebar.md) — the per-scene dialogue percentage, computed from the same quote detection.
