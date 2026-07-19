# Editor

The Editor is where you write. It is a WYSIWYG rich-text editor that operates one scene at a time, with the option of a second scene side by side. The writing engine is the same proven one as in earlier Novalist versions — typewriter scrolling, page view, comments, and footnotes all behave identically; only the shell around it is new.

Shortcuts below are written with `Ctrl`; on macOS use `Cmd`.

## Opening a scene

Click any scene in the **binder**. The main area switches to the Editor view and loads the scene; the open scene is highlighted in the binder and its word count and title appear in the status bar.

You can also switch to the Editor view any time via **Write → Editor** in the binder's view rail.

## Auto-save

Novalist saves automatically **two seconds** after the last keystroke. Pending changes are also flushed when you switch to another scene and when the app closes — there is no manual save step.

## The formatting toolbar

The strip above the page:

- **Bold**, **Italic**, **Underline** — toggle inline formatting on the selection.
- **Align left / center / right / justify** — set paragraph alignment.
- **Page view toggle** (book icon, far right) — switches the editor between a plain writing surface and a printed-book-style page with paper background, margins, and shadow. This is the same setting as **Page View** in [Settings](23-settings.md) → Editor.

The active formatting of the text under the caret is highlighted in the toolbar.

## The editor context menu

Right-click inside the text for:

- **Cut / Copy / Paste / Select All** — pasting strips foreign formatting and keeps only basic bold/italic/underline and alignment.
- **Add comment** — on a selection: attaches a comment to the selected text. Commented passages are marked in the text; click the marker to read or edit the comment.
- **Add footnote** — inserts a footnote at the caret. Footnotes are numbered sequentially within the scene and renumber automatically when one is deleted.
- **Add to Dictionary** — on a word flagged by the spell check: whitelists it.

## Split editor

To see two scenes at once, right-click a scene in the binder and choose **Toggle split editor**. The main area splits into two editor panes: your current scene on the left, the chosen scene on the right. Both panes are fully editable and auto-save independently. Choosing the command on another scene swaps which scene the second pane shows.

Common uses: referencing an earlier scene while writing a later one, or editing two scenes in parallel.

## Entity hover cards

When you hover over the name (or alias) of a codex entity in your prose, a small **hover card** appears with the entity's image, name, and a short detail line — enough to check a character's face or a location without leaving the editor. Entities are managed in the [Codex](06-codex.md).

## Grammar and spelling check

When **Grammar & Spelling Check** is enabled in [Settings](23-settings.md) → Writing assistance, Novalist sends your text to a LanguageTool-compatible API and underlines issues inline. Click an underlined passage to see suggestions and apply one.

By default the free public LanguageTool endpoint is used; the URL is configurable to point at a self-hosted server (to keep your text local), and Premium credentials, picky mode, and a mother-tongue setting for false-friend detection are available in the same settings section. Use **Add to Dictionary** in the context menu for names the spell check keeps flagging.

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
- **Font Family** and **Font Size** control the regular editing view.

All of this is purely visual — it doesn't change what gets exported. For export styling see [Export](20-export.md).

## Focus mode

`Alt+F` hides both side panes so only the toolbar, the page, and the status bar remain. Press `Alt+F` again to bring the panes back. The command palette (`Ctrl+Shift+P`) keeps working, so every command stays reachable while focused.

## Where to go next

- [Chapters & Scenes](04-chapters-and-scenes.md) — the binder tree around the editor.
- [Snapshots](17-snapshots.md) — revert a single scene to a previous state.
- [Find & Replace](21-find-replace.md) — search across scene, chapter, book, or project.
- [Settings](23-settings.md) — fonts, theme, writing assistance.
