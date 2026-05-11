# Chapters & Scenes

Chapters and scenes are how Novalist breaks a book into writable pieces. A book has an ordered list of chapters; each chapter has an ordered list of scenes; each scene holds the actual prose.

You spend most of your time looking at one scene in the editor and the full tree in the left sidebar.

## The Explorer (left sidebar, Chapters tab)

The Chapters tab of the left sidebar shows the book's structure as a tree. Each chapter is expandable; scenes appear underneath. Click a scene to open it in the editor. The currently open scene is highlighted.

Above the tree is a row of controls:

- **New chapter** — same as the **+Chapter** button in the app bar.
- **New scene** — same as the **+Scene** button in the app bar.
- **Search** — filters the tree by title.
- **Collapse all / Expand all**.

The tree supports:

- **Drag to reorder** — drag a scene to another position to change its order, or to another chapter to move it. Drag a chapter to change its order in the book.
- **Multi-select** — `Ctrl+Click` or `Shift+Click` to select multiple chapters or scenes. Operations like delete and status change apply to the selection.
- **Right-click context menu** — rename, delete, duplicate, set status, set label color, mark favorite, take snapshot, open in split pane, etc.

## Chapters

A chapter has:

- **Title** — shown everywhere.
- **Order** — its position in the book; controlled by drag-and-drop.
- **Status** — one of `Outline`, `First Draft`, `Revised`, `Edited`, `Final`. Drives the Dashboard status breakdown and is filterable from the Manuscript view.
- **Act** — optional textual label (e.g. "Act 1", "Act II"). Used by the timeline and by the Plot Grid for grouping. Acts can have their own date ranges defined in the book's act list.
- **Date** — a free-text in-world date string (e.g. "Spring, Year 312"). Optional.
- **Date range** — a structured `StoryDateRange` with start/end and optional start/end times. When present this takes precedence over the free-text date. See [Calendar & in-world dates](13-calendar.md).
- **Favorite** — pin to the top of filtered views.
- **Folder name** — derived from the title at creation, can be overridden. Determines the on-disk folder for the chapter's scene files.

### Creating a chapter

Click **+Chapter** in the app bar or press the hotkey (`Ctrl+Shift+N` by default).

The dialog asks for:

- **Chapter name** (up to 200 characters).
- An optional **date** via a date picker.

Press **Create** or `Enter`.

### Renaming a chapter

Right-click the chapter in the Explorer → **Rename**. The folder on disk is renamed in step with the title.

### Setting chapter status

Right-click → **Status →** pick one of the five. The status color appears as a small swatch next to the chapter in the Explorer and contributes to the Dashboard breakdown.

### Reordering chapters

Drag the chapter to a new position in the tree. The order is persisted immediately.

### Deleting a chapter

Right-click → **Delete**. You are asked to confirm. Deletion removes the chapter, its scenes, and the on-disk folder. Snapshots of the deleted scenes survive in `Books/<bookId>/Snapshots/` until you delete them manually.

## Scenes

A scene has:

- **Title** — shown in the Explorer and the editor tab.
- **Order** — its position within the chapter.
- **Chapter** — which chapter it belongs to.
- **File name** — derived from the title at creation; the actual `.html` file on disk.
- **Word count** — auto-computed from the scene content.
- **Date** — free-text in-world date.
- **Date range** — structured `StoryDateRange` (start, end, optional times, note). Used by the Calendar and Timeline.
- **Synopsis** — short summary. Editable from the Scene Notes bottom panel and from the Manuscript outliner view.
- **Notes** — longer freeform notes. Editable from the Scene Notes bottom panel.
- **Label color** — pick from a palette to color-code the scene in the Explorer, Manuscript corkboard, and other views.
- **Favorite** — pin to favorite-filtered views.
- **Plotline IDs** — list of plotlines this scene contributes to. Editable from the [Plot Grid](08-plot-grid.md) or scene context menu.
- **Comments** — inline comments anchored to text ranges. See [Comments](22-context-sidebar.md#comments).
- **Footnotes** — numbered footnotes attached to text positions. See [Footnotes](22-context-sidebar.md#footnotes).
- **Analysis overrides** — optional manual overrides for the scene's detected POV, emotion, intensity, conflict, and tags. The Context sidebar derives these by default; you can override any of them.

### Creating a scene

Click **+Scene** in the app bar or use the hotkey. The dialog asks for:

- **Scene name** (up to 200 characters).
- **Chapter** — drop-down of all chapters in the active book.
- Optional **date**.

The new scene opens in the editor.

### Renaming a scene

Right-click in the Explorer → **Rename**. The file on disk is renamed to match.

### Reordering and moving scenes

Drag inside the same chapter to change order. Drag to another chapter to move. The file moves to the target chapter's folder; the snapshot history follows.

### Marking favorite

Right-click → **Toggle favorite**, or click the star icon if shown. Favorites appear at the top of the explorer when the favorites filter is on, and contribute to the smart-list query options.

### Setting a label color

Right-click → **Label color** → pick a swatch (or **Clear**). The chosen color appears as a small bar on the scene row and as the card background in the Manuscript corkboard. Useful for color-coding by POV, plotline emphasis, or tone — entirely up to you.

### Duplicating a scene

Right-click → **Duplicate**. Creates a copy of the scene's content as a new scene immediately below, with `(copy)` appended to the title.

### Deleting a scene

Right-click → **Delete**. Asks to confirm. Snapshots of the deleted scene survive in `Books/<bookId>/Snapshots/<sceneId>/`.

## Status workflow

A typical novelist workflow with the five built-in chapter statuses:

1. **Outline** — bullet points or rough structure in the scene; not yet written.
2. **First Draft** — first pass through, complete or near-complete.
3. **Revised** — restructuring, voice fixes, scene-level edits done.
4. **Edited** — line edits, prose polish, copy-edits applied.
5. **Final** — ready to export.

The Dashboard shows a count of chapters at each status. The Manuscript view filter lets you read only chapters at a given status.

## Acts

A book has an optional list of **acts**. Acts are simple named buckets (e.g. "Act 1: Setup", "Act 2: Confrontation", "Act 3: Resolution"). Each chapter optionally references an act by name via its `Act` property.

Acts can have their own optional `StoryDateRange`. The Timeline displays acts as the broadest grouping; the Plot Grid groups columns by act when the act label is set.

Story-structure templates (such as the three-act or hero's-journey templates) pre-create acts and assign chapters to them. See [Templates](07-templates.md).

## Snapshots

Every time you save a scene a **snapshot** is captured automatically. Snapshots are independent per scene and let you revert one scene's content without affecting the rest of the project. See [Snapshots](17-snapshots.md) for the snapshot browser, compare view, and pruning.

You can also take a manual snapshot at any time from **Edit → Take Snapshot** or `Ctrl+Alt+S` (default binding).

## Where to go next

- [Editor](05-editor.md) — formatting, focus mode, comments, footnotes, split editor.
- [Plot Grid](08-plot-grid.md) — attach scenes to plotlines.
- [Calendar & in-world dates](13-calendar.md) — give scenes structured story dates.
- [Smart Lists](16-smart-lists.md) — saved scene queries.
