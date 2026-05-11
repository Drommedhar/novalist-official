# Editor

The Editor is where you write. It is a WYSIWYG rich-text editor that operates one scene at a time, with the option of a split second pane.

This page covers everything in or attached to the editor: formatting, paragraph styles, the auto-save loop, focus mode, the split editor, multiple scene tabs, auto-replacements, dialogue correction, grammar check, comments, footnotes, and the focus-peek popover.

For everything around the editor (synopsis/notes, scene analysis, footnote list) see [Context sidebar](22-context-sidebar.md). For chapter and scene management see [Chapters & Scenes](04-chapters-and-scenes.md).

## Opening a scene

Click any scene in the left **Explorer**. It opens in a new tab at the top of the content area, or focuses the existing tab if already open. The active tab is highlighted.

Each tab has:

- A short **badge** with the chapter index (for example `1.3` for chapter 1, scene 3).
- The scene title.
- A **dirty dot** indicating unsaved changes.
- A **close (×)** button.

Right-click a tab for **Close** and **Move to other pane** (sends the tab to the secondary editor, opening a split if needed). Drag a tab to reorder it.

## Auto-save and the dirty indicator

Novalist saves automatically two seconds after the last keystroke. The dirty dot disappears when the save completes. You can also save immediately with `Ctrl+S`.

If the app is closed while a tab is still dirty, the in-flight save is flushed first.

## Word count, reading time, and readability

The status bar (bottom-left of the window) shows three live metrics for the active scene:

- **Word count** — Hover for a tooltip showing character count (with and without spaces).
- **Reading time** — Based on ~225 words per minute.
- **Readability badge** — Flesch reading-ease score 0–100. Color-coded:
  - Green — very easy (80–100)
  - Yellow-green — easy (70–80)
  - Yellow — fairly easy (60–70)
  - Orange — fairly difficult (50–60)
  - Red-orange — difficult (30–50)
  - Red — very difficult (0–30)

  Hover for the textual level label. The same scoring appears per chapter in the Project Overview popup (status bar center).

## Formatting

The toolbar above the editor (or the **Format** menu of the editor extension, or the keyboard shortcuts) gives you:

### Inline formatting

| Action | Shortcut |
| --- | --- |
| Bold | `Ctrl+B` |
| Italic | `Ctrl+I` |
| Underline | `Ctrl+U` |
| Strikethrough | `Ctrl+Shift+X` |

### Alignment

| Action | Shortcut |
| --- | --- |
| Align left | `Ctrl+L` |
| Align center | `Ctrl+E` |
| Align right | `Ctrl+R` |
| Justify | `Ctrl+J` |

### Paragraph styles

Apply structural paragraph styles by placing the cursor in the paragraph (or selecting multiple) and choosing a style:

- **Body** — the default. Plain paragraph, indented or spaced depending on book settings.
- **Heading** — top-level heading.
- **Subheading** — second-level heading.
- **Blockquote** — indented quotation block.
- **Poetry** — preserves line breaks and uses a slightly different layout.
- **Clear style** — reverts the paragraph to Body.

Each style has a default hotkey (see [Hotkeys](26-hotkeys.md)).

### Indentation and lists

- **Indent / Outdent** — `Tab` / `Shift+Tab` at the start of a paragraph.
- **Bullet list / Numbered list** — via the toolbar.

## Auto-replacements

As you type, certain character sequences are converted automatically based on the active **auto-replacement language preset** (set in Settings → Writing Assistance):

- `--` becomes an em-dash (—).
- `...` becomes an ellipsis (…).
- Straight quotes `'` become curly quotes appropriate to the preset: English curly quotes, German low-quote style, French guillemets with non-breaking spaces, Spanish/Italian/Portuguese/Russian guillemets, Polish/Czech/Slovak quotes.

You can edit the replacement table from Settings or disable it entirely. Replacements only fire as you type; pasting raw text is left alone.

## Dialogue correction

When **Dialogue Punctuation Correction** is enabled in Settings → Writing Assistance, common dialogue punctuation mistakes are fixed for you as you type. Examples:

- `"That's mine." he said.` → `"That's mine," he said.` (period before tag becomes comma)
- `"What?". she asked` → `"What?" she asked` (no period after a question mark)

The correction is opinionated and follows the conventions of the active language. Disable it if you have your own house style.

## Grammar and spelling check

When **Grammar & Spelling Check** is enabled (default on), Novalist calls a LanguageTool-compatible API and underlines issues inline. By default it uses the free public LanguageTool endpoint; the URL is configurable to point at a self-hosted server. The selected UI language drives the check language.

Click an underlined word for:

- **Suggestions** — pick one to replace.
- **Add to ignore list** — for the current scene.
- **Ignore rule** — silence this rule entirely.

You can also disable the check from Settings.

## Focus mode

`F11` toggles **Focus Mode**. The menu bar, app bar, sidebars, scene notes, and tab strip all hide. Only the editor remains. Press `F11` again to bring everything back.

Use this for distraction-free drafting. All hotkeys still work, including the command palette (`Ctrl+Shift+P`) which gives you access to any action you need.

## Focus peek (entity preview)

When you hover over an entity reference in the prose (for example a character's name), a **focus peek** card pops up showing:

- The entity's name.
- Its primary image, if any.
- Its short description / synopsis.

Click the card to open the full entity editor in a new tab. The peek is provided by the `FocusPeekExtension` and is enabled by default. Disable from Settings if you find it intrusive.

## Split editor

Toggle the split with **View → Toggle Split Editor**, or use the hotkey. A second editor pane opens to the right with its own tab strip. Each tab can be moved between panes via right-click → **Move to other pane**, or dragged.

Common uses:

- Reference an earlier scene while writing a later one.
- Edit two scenes in parallel.
- Keep an entity editor visible while writing.

The split is a runtime view; closing the second pane (by clicking the toggle again) does not lose any tabs — they fold back into the main pane.

## Comments

Highlight some text and press the comment hotkey (or **Edit → Add Comment**). A bubble dialog asks for the comment text. After pressing Save the selection is wrapped in a comment span (with a colored underline) and the comment appears in the **Comments** section of the Scene Notes panel.

Each comment has:

- **Anchor text** — the snippet you originally selected.
- **Body** — your comment.
- **Created at** — timestamp.
- **Resolved** — toggleable. Resolved comments still exist but display de-emphasized.

Click a comment in the Scene Notes panel to jump back to its anchor in the editor.

See [Comments](22-context-sidebar.md#comments) for the full lifecycle.

## Footnotes

Place the caret where you want the footnote marker and press the footnote hotkey (or **Edit → Add Footnote**). A superscript number is inserted; the footnote opens for editing in the **Footnotes** tab of the Context sidebar.

Footnotes are numbered sequentially within the scene. Deleting a footnote renumbers the rest automatically. Each footnote has an `id` (stable across renumbering) and a `number` (renderer-facing).

See [Footnotes](22-context-sidebar.md#footnotes) for the panel.

## The book-style preview

The editor can be styled to look like a printed book page. Toggle from Settings → Editor:

- **Enable book paragraph spacing** — applies first-line indents and tighter paragraph spacing.
- **Enable book width** — constrains the text column to a printed-page width.
- **Book page format** — choose a target trim size (US Trade 6×9 by default; A5 and other presets available).
- **Book font family / size** — the typeface used in book-preview mode.

The book preview is purely visual — it doesn't change what gets exported. For the actual export styling see [Export](20-export.md).

## Multiple open scenes

You can have many scenes open at once across the two panes. Tabs persist across sessions: when you reopen the project, the same scenes are reopened in the same panes in the same order, with the same active tab.

Close a tab with × on the tab, `Ctrl+W`, or middle-click. Closing a tab does not delete the scene — it just removes it from the open list.

## Selection actions and inline actions

Right-clicking inside the editor opens a context menu with:

- Standard cut/copy/paste/select-all.
- **Add comment** (if a selection is active).
- **Add footnote** (at the caret).
- **Style** submenu (apply a paragraph style).
- Any **inline actions** contributed by extensions (e.g. AI rewrite, expand, describe, translate). Inline actions can act on the current selection and replace it with the result.

## Where to go next

- [Context sidebar](22-context-sidebar.md) — scene analysis, comments, footnotes, scene notes panel.
- [Snapshots](17-snapshots.md) — revert a single scene to a previous save.
- [Find & Replace](21-find-replace.md) — across-book search.
- [Settings](23-settings.md) — fonts, theme, accent, writing assistance.
