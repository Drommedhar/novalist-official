# Editor

The Editor is where you write. It is a WYSIWYG rich-text editor. Each editor pane keeps a strip of open scene tabs, and you can show a second pane side by side. The writing engine is the same proven one as in earlier Novalist versions — typewriter scrolling, page view, comments, and footnotes all behave identically; only the shell around it is new.

Shortcuts below are written with `Ctrl`; on macOS use `Cmd`.

![A scene open in the editor with the Context inspector and scene-notes dock](images/editor.png)

## Opening a scene

Click any scene in the **binder**. The main area switches to the Editor view and loads the scene; the open scene is highlighted in the binder, it is added to the pane's tab strip, and its statistics appear in the [status bar](#status-bar-statistics).

The Editor has no view-rail icon of its own — you always reach it by opening a scene from the binder.

## Scene tabs

Each editor pane keeps the scenes you have opened as a **tab strip** across the top of the pane. Clicking another scene in the binder adds it as a new tab instead of replacing the current one, so you can keep several scenes open and jump between them.

The strip appears once a pane has more than one scene open (a single open scene keeps the clean, strip-free look). Each tab shows:

- The scene title (falling back to the chapter title for an untitled scene).
- A small **dirty dot** while the scene has unsaved edits, which clears once autosave flushes.
- A **close** button (`×`).

Tab actions:

- **Click** a tab to switch the pane to that scene.
- **Middle-click** a tab, or click its `×`, to close it. Closing the active tab activates its neighbour.
- **Right-click** a tab for a small menu: **Close tab** and **Move to other split** (sends the scene to the other editor pane, opening the split if needed).

## Auto-save

Novalist saves automatically **two seconds** after the last keystroke. Pending changes are also flushed when you switch to another scene and when the app closes — there is no manual save step.

## The formatting toolbar

The strip above the page:

- **Paragraph style** (drop-down, far left) — what the paragraph under the caret *is*: Body, Heading, Subheading, Block quote or Verse. See [Paragraph styles](#paragraph-styles) below.
- **Bold**, **Italic**, **Underline** — toggle inline formatting on the selection.
- **Bulleted list** and **Numbered list** — turn the selected paragraphs into a list, or turn a list back into paragraphs.
- **Align left / center / right / justify** — set paragraph alignment.
- **Mark hard-to-read sentences** (gauge icon, right) — tints the sentences that fight the reader. See [Readability marking](#readability-marking) below.
- **Read aloud** (speaker icon, right) — reads the scene back to you from the caret's paragraph. See [Read aloud](#read-aloud) below.
- **Page view toggle** (book icon, far right) — switches the editor between a plain writing surface and a printed-book-style page with paper background, margins, and shadow. This is the same setting as **Page View** in [Settings](23-settings.md) → Editor.

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

## Splitting a scene

The context menu's **Split scene here** divides the scene at the caret: everything below it becomes a new scene directly after this one, carrying the date, stage, plotlines and POV that still describe it. See [Chapters and scenes](04-chapters-and-scenes.md#splitting-a-scene-in-two).

## Split editor

To see two scenes at once, right-click a scene in the binder and choose **Toggle split editor**. The main area splits into two editor panes: your current scene on the left, the chosen scene on the right. Both panes are fully editable, auto-save independently, and each keeps its own [tab strip](#scene-tabs) of open scenes. Use **Move to other split** on a tab to hand a scene from one pane to the other.

Common uses: referencing an earlier scene while writing a later one, or editing two scenes in parallel.

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

## Dialogue correction

When **Dialogue Punctuation Correction** is enabled in Settings → Writing assistance, common dialogue punctuation mistakes are fixed as you type, following the conventions of the selected quote-style language — for example a period before a dialogue tag becomes a comma. Disable it if you have your own house style.

## Typewriter scrolling and page styling

In [Settings](23-settings.md) → Editor:

- **Typewriter Scrolling** keeps the active line at a fixed vertical position (top, middle, or bottom) so you never write at the bottom edge of the window.
- **Page View** renders the editor as a book-style page (also toggleable from the formatting toolbar).
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

## Readability marking

The gauge button on the toolbar tints each sentence Novalist judges hard to read: a light wash for **difficult**, a stronger one for **very difficult**. Everything easier is left alone on purpose — tinting every sentence produces a heat map you stop seeing, and what you actually want is the handful of sentences that fight the reader.

- Each sentence is graded on its own, with the same readability method the [style report](36-style-report.md) uses for the scene, chosen from your writing language.
- Sentences under four words are never marked. A two-word line is a beat or a piece of dialogue, not a readability signal.
- The marking updates as you type, shortly after you stop.
- Nothing is written to the scene: the tint is painted over the text, so a marked-up chapter is not a modified chapter.

The toggle is remembered, and can be pinned per project like the rest of the editor settings. It is a revision tool — a coloured page while drafting is the opposite of what drafting needs — so it starts off.

## Read aloud

The speaker button on the toolbar reads the open scene aloud, starting from the paragraph your caret is in. The sentence being spoken is highlighted and the editor scrolls to keep it in view, so you can follow the reading with your eyes — which is what makes it useful for catching a sentence that does not land, not only for listening.

Stop it by pressing the button again (it becomes a stop square while reading), by pressing `Escape`, or simply by starting to type: typing over a passage being read back is you taking over.

**Settings → Editor** carries the speed and the voice. Left on **Match the writing language**, Novalist asks for a voice in the language the scene is written in, so a German scene is read in German. The voices are the ones your operating system has installed, and nothing leaves the machine.

The highlight is painted without touching the document, so listening to a chapter never marks it as edited.

## Focus mode

`Alt+F` gives the whole window to the page: both side panes, the toolbar and the status bar all go, leaving the scene tabs and your prose. Press `Alt+F` again to bring everything back. The command palette (`Ctrl+Shift+P`) keeps working, so every command stays reachable while focused — nothing is lost, only hidden.

**Dim other paragraphs while writing** (Settings → Editor) fades every paragraph but the one your caret is in. It works everywhere, but it is what turns focus mode into a composition mode rather than a wider editor. The dimming follows the caret as you move, and fades rather than cutting, because a hard change between paragraphs is more distracting than the dimming solves.

## Where to go next

- [Chapters & Scenes](04-chapters-and-scenes.md) — the binder tree around the editor.
- [Exposé](32-expose.md) — the same writing surface with paragraph-style buttons and length counters.
- [Snapshots](17-snapshots.md) — revert a single scene to a previous state.
- [Find & Replace](21-find-replace.md) — search across scene, chapter, book, or project.
- [Settings](23-settings.md) — fonts, theme, writing assistance.
- [Accessibility](39-accessibility.md) — reading comfort, the High Contrast theme, read-aloud.
- [Dialogue](33-dialogue.md) — read one character's lines end to end, and edit them without leaving the list.
