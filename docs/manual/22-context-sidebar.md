# Inspector

The **Inspector** is the right-hand context sidebar of the Novalist window. For the scene open in the editor it has two tabs:

- **Context** — scene context and analysis (entities, mention matrix, editable POV/emotion/intensity/conflict/tags).
- **Footnotes** — the footnotes and comments anchored in the open scene.
- **Inbox** — every open note in the book, with replies and to-dos.

The scene's **synopsis** and freeform **notes** live in the [scene-notes dock](02-interface-overview.md) beneath the editor (`Ctrl+Shift+N`), and the scene's **snapshot history** is opened from the toolbar Snapshots button. (This pane was called the context sidebar in earlier versions.)

## Toggling the Inspector

- The Inspector toggle at the far right of the toolbar, or
- `Ctrl+Shift+B` (`Cmd+Shift+B` on macOS).

**Focus mode** (`Alt+F`) hides the Inspector together with the binder, leaving only the editor.

When no scene is open, the Inspector shows a placeholder; open a scene from the binder and its details appear.

## Scene header

The header shows the open scene's title, a **Chapter · Scene N of M** subtitle placing it within its chapter, the scene's **story date** with the weekday it falls on (when the scene has a date), and the current **word count**.

## Scene context and analysis

Directly below the header, Novalist analyses the open scene and shows what it finds. Every part is a collapsible section, and your collapse choices are remembered between scenes.

### Find new entries in this scene

If you have an extension installed that provides an entity extractor (the [AI Assistant](24-extensions.md) does), a **Find new entries in this scene** button sits at the top of the Context tab. It reads the open scene and suggests Codex entries for the people, places, and things it mentions that are not in your Codex yet — the names you invented mid-sentence and never got round to filing.

The suggestions arrive as a **review list**: each row shows the proposed name, a one-line note on what the scene says about it, and a dropdown to change the kind (Character, Location, Item, Lore). Nothing is pre-ticked and nothing is written until you select entries and confirm; anything already in your Codex is filtered out before you ever see it. Accepted entries are created immediately and appear in the panel below.

Without such an extension the button does not appear, and the rest of the Inspector is unchanged.

### Entities in the scene

The **characters**, **locations**, **items**, and **lore** detected in the scene, each shown as a card with its thumbnail and name. **Hover** a card to raise the same rich **focus-peek** card the editor shows when you hover an entity's name in the prose — image, attribute pills, relationships (which you can click to peek through to related entities), appearance, custom properties, description, map pins and sections, with pin/open/close buttons in its header. The peek resolves any [chapter or scene overrides](06-codex.md) for the open scene, so it matches who the entity is at this point in the story. **Click** a card to open that entity's article in the [Wiki](30-wiki.md) (from there, **Edit in Codex** reaches the editable record). Character cards also carry small **Gender** and **Age** pills. A character that has a [chapter or scene override](06-codex.md) for the open scene is shown with its overridden name, role, gender and age, so the card matches who the character is at this point in the story. When age is stored as a birth date, the pill shows the character's age **at the open scene**, computed from the birth date against the scene's story date (falling back to the chapter's date, then today).

### Research about this scene

The research items this scene is about, each with the reason it is being offered — the character, place or thing it is filed under, or the tag it shares with the scene. Click one to open it in the [Research](15-research.md) view.

Matching is exact and needs no AI: an item appears here when it is linked to a Codex entry the scene involves, or carries one of the scene's own tags. Nothing is guessed from the prose, so a suggestion never needs double-checking. Items in the Research inbox are excluded — everything quick-captured carries that tag, and matching on it would offer you the whole unfiled pile.

At most six are shown, best first, in the same order every time you open the scene. The section is hidden when nothing matches.

Until now research reached you in two places and neither was where you were writing: the Research view, which means leaving the scene, and an entity's Wiki article, which means already knowing what to look up.

### Mention matrix

A cross-chapter grid: one row per tracked character showing, chapter by chapter, where they appear. The current chapter is marked, and a character who has been off-page for a while gets a **"last seen N chapters ago"** note — an easy way to spot a cast member who has quietly dropped out of the story.

### Scene analysis

Each value is auto-computed but fully editable, and every field carries a **reset-to-auto** button that drops your override and restores the detected value.

**A note on language:** emotion, intensity, conflict, and the auto tags are detected from keyword lists, and Novalist ships one list per language — currently **English, German, and Simplified Chinese**, matching the bundled interface languages. Your writing language (Settings → Writing assistance) selects the list; a regional variant falls back to its base language, so `de-AT` uses the German list.

If you write in a language that has no list yet, Novalist does **not** guess — it leaves those fields blank and says so at the top of the section, rather than scoring your prose against another language's words. You can still set every field yourself, and the language-independent parts (POV from character names, word count, dialogue percentage, sentence length) work in every language.

Adding a language is a data change, not a code change: drop an `analysis.<tag>.json` into the `Analysis/` folder beside your settings, with the same emotion keys and translated word lists, and press **Rescan** in Settings, Language packs. See [Custom themes & language packs](34-custom-themes-and-languages.md) and [Localization](27-localization.md).

- **POV** — the point-of-view character, chosen from a dropdown.
- **Emotion** — the scene's dominant emotion, chosen from a dropdown.
- **Intensity** — a bipolar bar from -10 to +10 (the fill runs one way for negative values, the other for positive) with a number box to set it exactly.
- **Conflict** — a short free-text description.
- **Tags** — a comma-separated list; the current tags also appear as chips below the field.

Beneath the analysis, a stats line reports the scene's **word count**, **dialogue percentage**, and **average sentence length**.

## Links and backlinks

Under the scene analysis, **Points at** lists everything this scene references and **Pointed at by** lists every scene that references it.

Research items could already reference each other, both ways. A scene could reference nothing — a scene that answers an earlier scene, or leans on one research note, could only say so as prose in its own notes, which nothing could follow and which the other end never knew about.

- Pick a **kind** — Scene, Research, or Codex entry — then pick what to point at, and press the plus.
- Each link takes an optional **reason** in your own words, edited in place: "pays off the promise made here". A bare link is still worth having, so the reason is never required — demanding one is how a link does not get made.
- Click a link to go there. A scene link opens that scene; a research or Codex link switches to that view.
- Pointing at the same thing twice does not make a second row — it rewrites the reason on the row already there.
- A scene cannot point at itself.
- If the thing at the other end is deleted, **the row stays** and reads *No longer there*. A link that disappears silently is a link you never find out you lost.

**Pointed at by** is the half that makes a link worth making: it is how a scene finds out which scenes answer it, and it is filled in automatically by the links pointing here.

## Inbox tab

The **Inbox** tab lists every open note in the whole book, not just the open scene — because a note you cannot find again is a note you did not leave.

Each entry shows which chapter and scene it is in (click to open that scene), the text it was anchored to, the note itself, and who left it. From the row you can:

- **Mark done** — resolve it. Resolved notes drop out of the list; tick **Include resolved** to see them again, or reopen one.
- **Make this a to-do** — flag it as a job rather than a remark. Tick **To-dos only** to see just those.
- **Reply** — type an answer and press Enter. Replies stack under the note, each with its author, which is how a note becomes a conversation.

Authors come from the project's **Author** setting, stamped when a note is created.

## To do

Under the Inbox tab, alongside the open notes and the prose you cut, is a **To do** list.

A [todo comment](#footnotes-and-comments-footnotes-tab) is anchored to a passage and belongs to the scene it sits in. "Check the dates in act two", "read the whole thing aloud", "decide whether Tomas survives" belong to no passage and to no scene, so they used to be kept on paper or in a scene called Notes.

- Type what needs doing and press Enter. The second box puts it in a **named list** — leave it empty and it lands in **Loose ends**.
- The list box offers the names already in use, so a checklist is not split in two by a capital letter.
- **A ticked item stays visible**, greyed and struck through. A checklist that empties as it is worked reads as though nothing was done, which is the opposite of what a revision pass is for. Each list shows how many of its items are done.
- **Untick the list** on a named list clears every tick at once, so a revision checklist can be run again on the next pass instead of being retyped. That is what makes it a checklist rather than a list of notes.
- Unticking one item clears its completion date too. A date saying something was finished, on a row that is not, is worse than no date.

## Footnotes and comments (Footnotes tab)

The **Footnotes** tab lists the footnotes and inline comments in the open scene:

- **Footnotes** — each with its number and its text, editable inline; remove one with its close button.
- **Comments** — each shows the anchored text it attaches to, an editable comment body, a close button to delete it, and a **resolved** toggle to mark it done (resolved comments are dimmed).

You create footnotes and comments inside the editor (see [Editor](05-editor.md)); this tab is where you read, edit, resolve, and clear them.

## Synopsis and notes (bottom dock)

The scene's **synopsis** (a short summary) and freeform **notes** are no longer in the Inspector — they live in the **scene-notes dock** beneath the editor, toggled from the toolbar or `Ctrl+Shift+N`. Both save when you click away. The synopsis also appears on Manuscript corkboard cards and in the outliner table; notes are never exported.

## Scene snapshots (toolbar dialog)

Per-scene snapshots are taken and managed from the toolbar **Snapshots** button, which opens a dialog with **Take snapshot**, **Restore**, **Delete**, and **Compare**. See [Snapshots](17-snapshots.md) for the full picture, including the auto-snapshots taken before Replace All and before restores.

## Where to go next

- [Editor](05-editor.md) — the writing surface the Inspector describes.
- [Snapshots](17-snapshots.md) — per-scene version history.
- [Codex](06-codex.md) — where the entity cards open.
- [Manuscript view](10-manuscript.md) — where synopses show up on cards and in the outliner.
- [Dialogue](33-dialogue.md) — the same quote detection behind the dialogue percentage, per character across the book.
