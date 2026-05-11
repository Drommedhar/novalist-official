# Context sidebar, Scene Notes, Footnotes, Comments

This page covers the auxiliary panels that surround the editor:

- The **Context sidebar** on the right — live scene analysis.
- The **Footnotes** tab — list of footnotes in the current scene.
- The **Scene Notes** panel at the bottom — synopsis, notes, comments.

Each can be toggled individually. They appear only when a scene is open.

## The Context sidebar

The right-hand panel. Toggle with the corresponding button on the app bar, **View → Toggle Context Sidebar**, or the hotkey.

Tabs along the top of the sidebar:

- **Context** — the live scene-analysis panel (described below).
- **Footnotes** — list of footnotes in the scene.
- Any tabs contributed by extensions.

### The Context tab — live scene analysis

When you open or edit a scene, the Context tab analyzes the prose and shows:

#### Detected entities

- **Characters** — all characters whose name (or surname) appears in the scene, with thumbnails and frequency counts. Click a character row to open them in a new tab.
- **Locations** — locations referenced.
- **Items** — items referenced.
- **Lore** — lore entries referenced.

Detection is name-match-based. False positives can be hidden via the **Ignore** action on a row.

#### POV analysis

- Detected **POV character** based on first-person markers and dialogue tags. If unsure, lists candidates.
- **POV style** — first-person / second-person / third-person.
- You can override the detected POV via the scene's analysis-overrides; the override sticks even after re-detection.

#### Emotional tone

A profile picker plus inferred values:

- **Neutral / Tense / Joyful / Melancholic / Chaotic / Somber** — the dominant tone, inferred from keyword sentiment.
- **Intensity** — 1–10 scale.
- **Conflict** — short tag.
- **Tags** — free-text tags used by Smart Lists.

You can override any of these from the analysis overrides editor.

#### Counts

- **Dialogue** lines, **sentence** count, **word** count, **paragraph** count.
- Distribution of **sentence length**.
- Dialogue percentage of total words.

#### Live updates

The analysis re-runs when the scene content changes. It is debounced so it doesn't update on every keystroke; expect it to refresh a second or so after you stop typing.

### Hiding the Context sidebar

Toggle from **View → Toggle Context Sidebar** or the app-bar button. It hides cleanly without losing any data.

## The Footnotes tab

The **Footnotes** tab of the Context sidebar lists every footnote in the current scene, in order. Each entry shows:

- **Number** — superscript number anchored in the scene.
- **Body** — editable text. Edit and click outside to save.
- A **delete** button.
- A **jump** button that scrolls the editor to the footnote's anchor.

### Adding a footnote

Place the caret in the editor at the desired position. Use:

- **Edit → Add Footnote**, or
- the hotkey (see [Hotkeys](26-hotkeys.md)), or
- the editor's context menu.

A superscript number is inserted at the caret position; the new footnote opens for editing in the panel.

### Renumbering

Deleting a footnote causes the remaining footnotes to renumber automatically. Each footnote has a stable internal `id` separate from its rendered `number`, so cross-references in your prose continue to point at the right footnote even if the number changes.

### Footnotes in exports

Footnotes are preserved across all exports that support them (DOCX, EPUB, PDF, Markdown). LaTeX and Final Draft export them in their respective conventions.

## The Scene Notes panel

The bottom panel beneath the editor. Toggle from the app bar, **View → Toggle Scene Notes**, or the hotkey. It contains three sub-sections:

### Synopsis

A short summary of the scene. Two or three sentences is the usual scale. Synopsis appears:

- On scene cards in the Manuscript Corkboard.
- In the Manuscript Outliner table.
- On scene tooltips in the Explorer.

Edit inline; saves automatically.

### Notes

A longer freeform note field. Use for:

- Research links specific to the scene.
- Outline / draft notes during writing.
- Reminders ("fix the timing of Alice arriving" / "double-check the magic-system rule applies here").

Notes are not exported. They're for you.

### Comments

The list of inline comments anchored to text ranges in the scene (see below). Each entry shows:

- **Anchor snippet** — the text the comment was attached to.
- **Body** — your comment.
- **Created at** — timestamp.
- **Resolved** — toggleable. Resolved comments are styled de-emphasized.
- **Jump** — scrolls the editor to the anchor.
- **Delete** — removes the comment span from the scene and the entry from the list.

#### Adding a comment

In the editor, select some text and use **Edit → Add Comment** or the hotkey. A small input dialog asks for the comment body. After save, the selection is wrapped in a comment span (colored underline) and the comment appears in the list.

#### Resolving vs deleting

A **resolved** comment remains in the scene (and re-appears if a beta reader looks at the file), but is visually de-emphasized. A **deleted** comment is gone entirely. Use Resolve for tracked feedback you've addressed; use Delete for outdated noise.

#### Comments in exports

By default DOCX and Markdown exports drop comments. Some extension presets include them as DOCX comments or Markdown footnotes for delivery to editors.

## Showing both notes panels at once

The Context sidebar (right) and the Scene Notes panel (bottom) are independent. You can have both open while writing. The status bar continues to work normally.

On smaller windows, consider keeping only one open at a time to keep the editor wide enough to read comfortably.

## Where to go next

- [Editor](05-editor.md) — where comments and footnotes are inserted.
- [Manuscript view](10-manuscript.md) — synopsis and comments appear here too.
- [Smart Lists](16-smart-lists.md) — tag and POV filters come from the Context analysis.
