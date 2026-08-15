# Editor

The Editor is where you write. It is a WYSIWYG rich-text editor. Each editor pane keeps a strip of open scene tabs, and the main area can be split so several scenes sit side by side. The writing engine is the same proven one as in earlier Novalist versions — typewriter scrolling, page view, comments, and footnotes all behave identically; only the shell around it is new.

Shortcuts below are written with `Ctrl`; on macOS use `Cmd`.

![A scene open in the editor with the Context inspector and scene-notes dock](images/editor.png)

## Opening a scene

Click any scene in the **binder**. The main area switches to the Editor view and loads the scene; the open scene is highlighted in the binder, it is added to the pane's tab strip, and its statistics appear in the [status bar](#status-bar-statistics).

The Editor has no view-rail icon of its own — you always reach it by opening a scene from the binder.

## Scene tabs

Each editor pane keeps the scenes you have opened as a **tab strip** across the top of the pane. Clicking another scene in the binder adds it as a new tab instead of replacing the current one, so you can keep several scenes open and jump between them.

The strip appears once a pane has more than one scene open, or once a second editor pane is open (a lone editor on one scene keeps the clean, strip-free look). Each tab shows:

- The scene title (falling back to the chapter title for an untitled scene).
- A small **dirty dot** while the scene has unsaved edits, which clears once autosave flushes.
- A **close** button (`×`).

Tab actions:

- **Click** a tab to switch the pane to that scene.
- **Middle-click** a tab, or click its `×`, to close it. Closing the active tab activates its neighbour.
- **Right-click** a tab for a small menu: **Close tab** and **Move to other pane** (hands the scene to the next editor pane, splitting the main area if there is not one yet).

## Auto-save

Novalist saves automatically **two seconds** after the last keystroke. Pending changes are also flushed when you switch to another scene and when the app closes — there is no manual save step.

## The formatting toolbar

The strip above the page shows **what applies to where you are**, rather than every command at once with most of them greyed out.

**With the caret in the text and nothing selected**, the toolbar is about the paragraph:

- **Paragraph style** (drop-down, far left) — what the paragraph under the caret *is*: Body, Heading, Subheading, Block quote or Verse. See [Paragraph styles](#paragraph-styles) below.
- **Bulleted list** and **Numbered list** — turn the paragraph into a list item, or turn a list back into paragraphs.
- **Align left / center / right / justify** — set paragraph alignment.
- **Peek at entity under caret** — appears only when the caret is actually on a linked Codex name, and opens its [hover card](#entity-hover-cards) from the toolbar. Hovering with the pointer still works; this is the way to reach the same card without one, which on a touch screen or by keyboard was previously not possible at all.

**With text selected**, it becomes about that text: **Bold**, **Italic**, **Underline**, **Strikethrough**, **Highlight**, **Link**, and the two labelled buttons **Comment** and **Footnote**.

At the right-hand end, whatever the context:

- **Suggestion mode** (pen icon) — see [Suggesting edits](#suggesting-edits-instead-of-making-them) below.
- **Writing options** (sliders icon) — a menu holding the settings that used to sit on the bar permanently:
  - **Mark hard-to-read sentences** — tints the sentences that fight the reader. See [Readability marking](#readability-marking) below.
  - **Read aloud** — reads the scene back to you from the caret's paragraph. See [Read aloud](#read-aloud) below.
  - **Page view**, **Dim other paragraphs**, **Typewriter scrolling**, and focus mode.
  - **More writing settings**, which opens the matching section of Settings and offers a link back to the scene when you are done.

When **read aloud** or **suggestion mode** is running, a bar under the toolbar says so and carries a single button to leave that mode, so there is never a mode you are in without being told and without an obvious way out.

In a narrow editor pane the primary commands stay put and the rest move into a **More** menu, rather than being cut off at the edge.

The active formatting of the text under the caret is highlighted in the toolbar.

### Selection toolbar

Selecting text pops up a small toolbar over it with the inline formatting that only makes sense on a selection:

- **Bold**, **Italic**, **Underline**.
- **Strikethrough** — for a line you have cut but are not ready to delete. It **is** carried into exports: DOCX, EPUB, Markdown and LaTeX all render it struck, because a struck line was meant to be seen struck.
- **Highlight** — marks a passage to come back to. Pressing it again on a highlighted passage removes the mark. A highlight is a note to yourself, so it is **not** exported: the words come through, the colour does not.
- **Link** — asks for an address and links the selection. Leave the address empty to remove a link.
- **Comment** and **Footnote** — see below.

## Paragraph styles

Beyond inline formatting, a paragraph can carry a **named style**. Pick one from the drop-down at the left of the toolbar; it applies to every paragraph the selection touches, and shows what the paragraph under the caret currently is.

| Style | What it is for | In the editor |
| --- | --- | --- |
| **Body** | Ordinary prose. The absence of a style rather than a style of its own, so a manuscript you never styled carries no markup it did not ask for. | Normal |
| **Heading** | A section title inside a scene. | Larger and bold |
| **Subheading** | A section title one level down. | Slightly larger and semibold |
| **Block quote** | A letter, an inscription, an excerpt from another book. | Indented and italic |
| **Verse** | A poem, a song, an epigraph — anything whose line breaks are the writer's and must survive. | Indented, never justified, line breaks kept |

**Lists** are separate from paragraph styles because they are real structure rather than a look: the two list buttons turn the selected paragraphs into a bulleted or numbered list, and pressing the same button again turns them back into paragraphs.

### How styles reach the exported file

Every format understands every style, so a heading is a heading wherever the manuscript goes:

| | Heading / Subheading | Block quote | Verse | Lists |
| --- | --- | --- | --- | --- |
| **DOCX** | Word's Heading 2 / Heading 3 styles, so Word's navigation pane and table of contents find them | Word's Quote style | Indented, left-aligned | Real Word numbering — an editor can renumber and restyle them |
| **EPUB** | `<h2>` / `<h3>`, so a reading system's navigation works | `<blockquote>` | Its own class, line breaks preserved | `<ul>` / `<ol>` |
| **Markdown** | `#` / `##` | `>` | Indented block | `-` / `1.` |
| **LaTeX / PDF** | `\section*` / `\subsection*` | `quote` environment | `verse` environment | `itemize` / `enumerate` |
| **Normseiten** | Upper-cased and set off with blank lines | As body text — the layout is a fixed submission format | As body text | As body text |

Pasting a heading from a web page or a Word document keeps its structure in the editor. Set it to the style you actually want from the drop-down, so it exports the way you intend rather than however the source page happened to mark it up.

The [Exposé](32-expose.md) view has its own Title / Section / Body buttons, because that document type's structure depends on them differently.

## The editor context menu

Right-click inside the text for:

- **Cut / Copy / Paste / Select All** — pasting strips foreign formatting and keeps only basic bold/italic/underline and alignment.
- **Add comment** — on a selection: attaches a comment to the selected text. Commented passages are marked in the text; click the marker to read or edit the comment.
- **Add footnote** — inserts a footnote at the caret. Footnotes are numbered sequentially within the scene and renumber automatically when one is deleted.
- **Add to Dictionary** — on a word flagged by the spell check: whitelists it.
- **Create entity from selection** — on a selection: makes a new [Codex](06-codex.md) entry named after the selected text (you pick the kind) and turns the selection into a mention of it. The same flow as the `@` picker's Create row, described below.
- **Add selection to entity** — on a selection: copies the passage into one of an existing entity's sections. Pick the entity from a searchable list, name the section (it defaults to **Notes** and is created if it does not exist yet), and confirm. Your prose is left exactly as it was — the passage is copied, not moved.

The last two exist so that worldbuilding you invent mid-sentence can reach the Codex without breaking your writing flow. A description of a city you just wrote can become that location's "Appearance" section in two clicks.

## Cutting a paragraph without losing it

Deleted prose used to be recoverable only by opening a [snapshot](17-snapshots.md) of the whole scene and reading it for the paragraph that used to be there. But a paragraph you cut because it does not belong in *this* chapter is not a mistake to undo — it is writing looking for a different home, and there was nowhere to put it.

Select the prose, right-click, and choose **Cut and keep**. It leaves the scene and lands in **Cut and kept**, at the bottom of the Inspector's Inbox tab, alongside the scene it came from and the date.

Both halves happen in one action on purpose: cutting first and asking you to file it afterwards is exactly how the paragraph gets lost between the two.

In the panel each cut is shown **whole** rather than as a one-line preview, which would hide the reason it was worth keeping. You can search what you kept, add a note about why, copy it, or throw it away for good. Copy rather than a one-click reinsert, because a kept cut usually belongs somewhere other than wherever the caret happens to be.

The bin holds the most recent 500 cuts and lives with the project, so it travels with the book and survives being zipped.

## Splitting a scene

The context menu's **Split scene here** divides the scene at the caret: everything below it becomes a new scene directly after this one, carrying the date, stage, plotlines and POV that still describe it. See [Chapters and scenes](04-chapters-and-scenes.md#splitting-a-scene-in-two).

## Two scenes at once

The main area is a tree of [panes](02-interface-overview.md#splitting-the-main-area-into-panes), and every pane showing the editor holds its own scene. There is no separate "split editor" mode and no limit of two.

Two ways in:

- Right-click a scene in the binder and choose **Open in split** — the main area splits and the scene opens in the new pane.
- Split the pane yourself (`Ctrl+Alt+Right`, `Ctrl+Alt+Down`, or the split buttons in the pane header). The new pane starts empty; click a scene in the binder and it opens there.

Every editor pane is fully editable, auto-saves independently, and keeps its own [tab strip](#scene-tabs) of open scenes. **Move to other pane** on a tab hands a scene to the next editor pane, creating one if there is no second pane yet.

The inspector, the status bar, find-and-replace and the scene-notes dock follow the pane you are working in.

Common uses: referencing an earlier scene while writing a later one, editing two scenes in parallel, or keeping the Codex open beside the scene it describes.

## Entity mentions and autocomplete

Novalist recognises the names of your [Codex](06-codex.md) entities — characters, locations, items, and lore — as you write, matching both the primary name and any aliases.

Type `@` to open the **mention autocomplete**: a picker of matching entities (by name or alias) appears; choose one to insert its name. This is the quickest way to keep names spelled consistently across the manuscript.

### Creating an entity from the name you just typed

The picker never dead-ends on a name that does not exist yet. As soon as you have typed something after the `@`, the last row of the picker is **Create "<name>"** — pick it (click, or select it with the arrow keys and press Enter) and Novalist asks which kind of entry to make: Character, Location, Item, Lore, or any of your custom types. Choose one and:

- the entity is created in the [Codex](06-codex.md) with that name,
- the text you typed becomes a real mention linked to it, and
- the name is immediately recognised everywhere else — hover cards, the Inspector, the [Wiki](30-wiki.md) Appearances timeline, and future `@` picks.

You never leave the page or lose your place. If you cancel the type chooser, the text stays exactly as you typed it, as ordinary prose. Fill in the details later in the Codex — the point is to capture the name at the moment you invent it.

## Entity hover cards

When you hover over the name or alias of a codex entity in your prose (or over an inserted mention), an enriched **hover card** appears with, as available:

- The entity's **image** and **name**, plus a **type** label (Character / Location / Item / Lore).
- A short **detail** line.
- A couple of key **attribute chips** — for a character its role, gender, and age; for a location its type and parent; and so on.
- Up to a few **relationships** (`role: target`).
- The entity's **section** titles.
- An **Open entity** button that opens that entity's article in the [Wiki](30-wiki.md) (from there, **Edit in Codex** jumps to the editable record).

The card is enough to check a character's face, a relationship, or a location without leaving the editor. Move the pointer onto the card to keep it open. Entities are managed in the [Codex](06-codex.md).

## Spell check

**Check spelling as I write** in [Settings](23-settings.md) → Writing assistance underlines misspellings in red as you type. It is on by default and uses the spell checker your operating system already has, so it needs no server and no account, works with the network switched off, and nothing you write leaves the machine.

Right-click a red-underlined word for corrections, or for **Add to dictionary** to teach Novalist a name it keeps flagging. Words you add are stored with your settings rather than in the system dictionary, so they travel with you to another machine instead of having to be taught again. The list is shown under **Your dictionary** in the same settings section, where a word can also be removed.

Novalist checks against the language you are writing in — your **Quote Style** language — not the language the menus are in, so a German novel written on an English install is checked in German. To check against more than one language at once, tick the ones you want under **Dictionaries**; leaving them all unticked follows the writing language.

On macOS this is the system spell checker and is entirely local. On Windows and Linux the dictionary for a language is downloaded once, the first time it is used, and works offline from then on.

## Grammar check

Spell check catches misspelt words; grammar check catches the rest. When **Grammar & Spelling Check** is enabled in [Settings](23-settings.md) → Writing assistance, Novalist sends your text to a LanguageTool-compatible API and underlines issues inline. Click an underlined passage to see suggestions and apply one. Unlike spell check, this one needs a server.

By default the free public LanguageTool endpoint is used; the URL is configurable to point at a self-hosted server (to keep your text local), and Premium credentials, picky mode, and a mother-tongue setting for false-friend detection are available in the same settings section.

## Slash commands

Typing **`/`** at the start of an empty line opens a menu of extension actions that work with nothing selected — chiefly, with the AI Assistant installed, continuing the prose from where you stopped or writing towards a beat you describe. Type to filter, arrow to choose, Enter to run; Escape closes it and leaves what you typed alone. See [Extensions](24-extensions.md#writing-from-the-caret-slash-commands).

A slash anywhere else is just a slash — "and/or" and "24/7" do not open anything.

## Auto-replacements

As you type, certain character sequences are converted automatically based on the **Quote Style** language preset in Settings → Writing assistance:

- `--` becomes an em-dash.
- `...` becomes an ellipsis.
- Straight quotes become the curly quotes of the selected preset (English curly quotes, German low quotes, French guillemets, and others).

Replacements only fire as you type; pasted text is left alone.

### Your own rules

The list is yours to edit, under **Replacement rules** in Settings → Writing assistance. Picking a language fills it in with that language's own rules; everything after that is up to you. Each rule has:

- **Kind** — **Text** matches the characters you type. **Pattern** is a regular expression matched against the text ending at the cursor, and its replacement can put back what the pattern captured using `$1`, `$2` and so on. A rule of `(\d+)x(\d+)` becoming `$1×$2` turns `12x9` into `12×9` as you finish typing it.
- **When I type** — the characters, or the pattern.
- **Insert** — what lands instead.
- **Closing** — fill this in only for a rule that should alternate, the way an opening quote alternates with a closing one. Left empty, the rule produces the same thing every time. A pattern has no alternating form, so this is unavailable for one.

### Trying a rule out

Each rule has a **Try it on** box under its fields. Type a sentence there and the rule is run against it as you go, showing what the sentence becomes — and, for a pattern, what it matched and what each capture group holds. Nothing you type there is stored; it is scratch paper for building the rule.

The preview shows **typing-time** behaviour, which is what makes patterns hard to reason about otherwise: a rule fires the moment the last character of its match is typed, and only there. Clean up the manuscript applies the same rules to prose already written, where they replace every occurrence rather than only the one at the cursor.

**How matching and replacement work** below the table folds open into the full account — when a rule fires, how the order decides which one wins, how the Closing form alternates, how captures work — with a reference for the pattern pieces you are most likely to need: the character classes `\d`, `\w`, `\s` and `.`, your own sets `[abc]`, `[^abc]` and `[a-z]`, the word edge `\b`, the repeats `+`, `*` and `?`, the either-or `|`, the capture `(…)` and its `$1`, and `\.` for matching a symbol that would otherwise mean something.

### What is refused

Rules run in the order they are listed, so a later rule can act on what an earlier one produced. A pattern that is not valid, or that matches nothing at all, is refused when you enter it rather than saved and quietly skipped — the reason appears under the row while you are still looking at it. A pattern that turns out to be too slow to run while you type is set aside for the rest of the session, so it cannot hold up your typing.

Like the rest of Writing assistance, the list can differ per book.

To switch them off entirely, untick **Replace quotes and dashes as I type** in Settings → Writing assistance. Nothing is then substituted — every character you type is the character that lands — and the substitution rules in [Clean up the manuscript](21-find-replace.md#cleaning-up-a-whole-manuscript) are greyed out as well, so no later pass puts them back. The **Quote Style** picker stays where it is, because it is also the language your book is written in.


## Dialogue correction

When **Dialogue Punctuation Correction** is enabled in Settings → Writing assistance, common dialogue punctuation mistakes are fixed as you type, following the conventions of the selected quote-style language — for example a period before a dialogue tag becomes a comma. Disable it if you have your own house style.

## Typewriter scrolling and page styling

In [Settings](23-settings.md) → Editor:

- **Typewriter Scrolling** keeps the active line at a fixed vertical position (top, middle, or bottom) so you never write at the bottom edge of the window.
- **Page View** renders the editor as a book-style page (also toggleable under **Writing options** in the formatting toolbar).
- **Book Page Width** constrains the text column to a printed page width, with selectable page formats, and **Book Font** / **Book Font Size** set the typeface for that mode.
- **Book Paragraph Spacing** adds book-like vertical spacing.
- **Font Family** and **Font Size** control the regular editing view. New projects start on Newsreader at 17px — the app's own text face, bundled so it is always available — but the field takes any family installed on your machine.

All of this is purely visual — it doesn't change what gets exported. For export styling see [Export](20-export.md).

## Keyboard shortcuts and zoom

The editor participates fully in the app's keyboard shortcuts even while the caret is inside the text: a global gesture (for example the command palette, focus mode, or find & replace) fires whether or not the editor has focus, so you never have to click out of the page first. See [Hotkeys](26-hotkeys.md) for the full list.

Hold `Ctrl` and scroll the mouse wheel over the page to **zoom** the editor font up or down (clamped to a sensible range). The zoom adjusts the editor font size, the same value exposed in [Settings](23-settings.md) → Editor.

## Status bar statistics

While a scene is open, the left of the status bar shows live figures for that scene, recomputed as you type:

- **Word count** and **character counts** (with and without spaces).
- Estimated **reading time** in minutes.
- A **readability badge** — a 0–100 score with a colour-coded label (Very easy, Easy, Moderate, Difficult, Very difficult). The score adapts to the writing language selected under Settings → Writing assistance.
- The **scene title**.

The centre of the status bar shows whole-project totals (words, chapters, scenes); click it for a project overview popover. The right side shows daily and project goal progress when goals are set (see [Dashboard](11-dashboard.md)).

## Images in the prose

Right-click in the editor and choose **Insert image**. Pick a file and Novalist asks what it shows, then places it on a line of its own after the paragraph your caret is in.

- The file is **copied into the book's `Images` folder**, so the project stays self-contained and moving it to another machine takes the pictures along. An image you have already used is not copied twice.
- The scene stores the path relative to the book, never an address on your disk — which is what makes the project portable.
- What you type when asked what the image shows becomes its **alt text**, and it travels into every export. Leaving it empty is allowed and means decorative; the question is asked at insert time because asking later means never.
- Images are carried into **EPUB** (packaged and manifested), **DOCX** (embedded, with the alt text as the picture description), **PDF** (drawn, scaled to the measure and never enlarged past its own size), **Markdown** and **LaTeX**.
- A picture whose file has gone is left out of the export rather than written as a broken reference.

Delete an image the way you delete anything else: put the caret after it and press Backspace.

## Readability marking

**Mark hard-to-read sentences**, under **Writing options** in the toolbar, tints each sentence Novalist judges hard to read: a light wash for **difficult**, a stronger one for **very difficult**. Everything easier is left alone on purpose — tinting every sentence produces a heat map you stop seeing, and what you actually want is the handful of sentences that fight the reader.

- Each sentence is graded on its own, with the same readability method the [style report](36-style-report.md) uses for the scene, chosen from your writing language.
- Sentences under four words are never marked. A two-word line is a beat or a piece of dialogue, not a readability signal.
- The marking updates as you type, shortly after you stop.
- Nothing is written to the scene: the tint is painted over the text, so a marked-up chapter is not a modified chapter.

The toggle is remembered, and can be pinned per project like the rest of the editor settings. It is a revision tool — a coloured page while drafting is the opposite of what drafting needs — so it starts off.

## Read aloud

**Read aloud**, under **Writing options** in the toolbar, reads the open scene aloud, starting from the paragraph your caret is in. The sentence being spoken is highlighted and the editor scrolls to keep it in view, so you can follow the reading with your eyes — which is what makes it useful for catching a sentence that does not land, not only for listening.

Stop it by pressing the button again (it becomes a stop square while reading), by pressing `Escape`, or simply by starting to type: typing over a passage being read back is you taking over.

**Settings → Editor** carries the speed and the voice. Left on **Match the writing language**, Novalist asks for a voice in the language the scene is written in, so a German scene is read in German. The voices are the ones your operating system has installed, and nothing leaves the machine.

The highlight is painted without touching the document, so listening to a chapter never marks it as edited.

## Focus mode

`Alt+F` gives the whole window to the page: both side panes, the toolbar and the status bar all go, leaving the scene tabs and your prose. Press `Alt+F` again to bring everything back. The command palette (`Ctrl+Shift+P`) keeps working, so every command stays reachable while focused — nothing is lost, only hidden.

**Dim other paragraphs while writing** (Settings → Editor) fades every paragraph but the one your caret is in. It works everywhere, but it is what turns focus mode into a composition mode rather than a wider editor. The dimming follows the caret as you move, and fades rather than cutting, because a hard change between paragraphs is more distracting than the dimming solves.

## Suggesting edits instead of making them

The **pen** button in the editor toolbar turns on **suggestion mode**. With it on, typing does not change the prose — it proposes a change:

- Words you type go in as an **addition**, underlined and in the accent colour.
- Words you delete are **marked as a cut**, struck through but still readable, rather than removed.
- Typing over a selection does both: the old words are proposed for deletion and the new ones as an addition.
- Deleting something you just suggested takes it straight back out. Proposing to cut your own unaccepted addition is a round trip nobody wants to read.

The editor's left edge is marked while the mode is on, because a mode where nothing you type sticks is a confusing one to be in without noticing.

Suggestion mode is not remembered between sessions. It is how you are working right now, not a preference.

### Answering suggestions

Open the **Notes** tab in the inspector. Every suggestion in the scene is listed with what it proposes, who proposed it, and two buttons: take it, or turn it down. **Take all** and **Turn all down** answer the whole scene at once.

Taking an addition keeps the words and drops the marks. Turning one down removes them. A cut is the mirror: taking it removes the words, turning it down puts them back as ordinary prose.

The [Inbox](22-context-sidebar.md) lists every scene in the book with suggestions waiting, so an edit left in a scene you have not opened for a month is still findable.

### Who suggested what

Set **Your name on suggested edits** in [Settings → Writing assistance](23-settings.md). It is only worth filling in when more than one person is suggesting — which is exactly when an unattributed edit is useless.

### What counts as the book

A suggested addition nobody has turned down **is part of the book**: it is in the word count, in a search, and in an export, the way a word processor treats one. A suggested cut is not. Exports never contain the marks themselves — an exported book is a finished book.

One thing suggestion mode is deliberately careful about: a plain strikethrough you applied yourself is **formatting, not a suggested cut**. Novalist only treats marks it made as suggestions, so text you struck on purpose still prints struck.

## Where to go next

- [Chapters & Scenes](04-chapters-and-scenes.md) — the binder tree around the editor.
- [Interface Overview](02-interface-overview.md#splitting-the-main-area-into-panes) — splitting the main area, pane layouts, and opening a pane in its own window.
- [Exposé](32-expose.md) — the same writing surface with paragraph-style buttons and length counters.
- [Snapshots](17-snapshots.md) — revert a single scene to a previous state.
- [Find & Replace](21-find-replace.md) — search across scene, chapter, book, or project.
- [Settings](23-settings.md) — fonts, theme, writing assistance.
- [Accessibility](39-accessibility.md) — reading comfort, the High Contrast theme, read-aloud.
- [Dialogue](33-dialogue.md) — read one character's lines end to end, and edit them without leaving the list.
