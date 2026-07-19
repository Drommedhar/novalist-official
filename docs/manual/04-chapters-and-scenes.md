# Chapters & Scenes

Chapters and scenes are how Novalist breaks a book into writable pieces. A book (more precisely, its active draft) has an ordered list of chapters; each chapter has an ordered list of scenes; each scene holds the actual prose.

You spend most of your time looking at one scene in the editor and the full tree in the **binder** on the left.

## The binder tree (Chapters tab)

The **Chapters** tab of the binder shows the active draft's structure as a tree:

- **Act headers** — when chapters are assigned to acts, a header row with the act name appears above the first chapter of each act.
- **Chapter rows** — a collapse/expand chevron, a colored **status dot**, and the title. Click the dot to cycle the chapter's status.
- **Scene rows** — the scene title and its word count. Click a scene to open it in the editor; the open scene is highlighted.
- **ARCHIVE** — a section at the bottom listing archived scenes (see below).

The tree supports:

- **Drag to reorder** — drag a chapter onto another chapter to change chapter order. Drag a scene within its chapter to reorder it, or drop it onto another chapter (or one of that chapter's scenes) to move it there.
- **Right-click context menus** — on chapters: **Rename Act**, **Rename Chapter**, **Delete**. On scenes: **Archive**, **Toggle split editor**, **Rename**, **Delete**.

## Chapters

A chapter has:

- **Title** — shown everywhere.
- **Order** — its position in the book; controlled by drag-and-drop.
- **Status** — one of `Outline`, `First Draft`, `Revised`, `Edited`, `Final`. Shown as the status dot and driving the Dashboard status breakdown and the Manuscript view filter.
- **Act** — optional textual label (e.g. "Act 1"). Groups chapters under act headers in the binder and in the Timeline and Plot Grid.
- **Date / date range** — in-world dates used by the [Timeline](12-timeline.md) and [Calendar](13-calendar.md).
- **Folder name** — derived from the title at creation. Determines the on-disk folder for the chapter's scene files.

### Creating a chapter

Click **+ Chapter** in the toolbar. A dialog asks for the chapter name; confirm with `Enter`.

### Renaming a chapter

Right-click the chapter in the binder → **Rename Chapter**. The folder on disk is renamed in step with the title.

### Setting chapter status

Click the chapter's **status dot**. Each click cycles to the next status: Outline, First Draft, Revised, Edited, Final, and back to Outline. The dot's color reflects the current status.

### Reordering chapters

Drag the chapter onto the chapter you want it to take the place of. The order is persisted immediately.

### Deleting a chapter

Right-click → **Delete**. You are asked to confirm; deletion removes the chapter, its scenes, and the on-disk folder. Snapshots of the deleted scenes survive in the book's `Snapshots/` folder until you delete them manually.

## Scenes

A scene has:

- **Title** — shown in the binder and the status bar.
- **Order** and **chapter** — its place in the manuscript; controlled by drag-and-drop.
- **File name** — the `.novalist` file on disk, derived from the title at creation.
- **Word count** — auto-computed, shown next to the scene in the binder.
- **Synopsis** and **notes** — editable in the [inspector](02-interface-overview.md#the-inspector). The synopsis is also editable from the Manuscript view's outliner and appears on its corkboard cards.
- **Date / date range** — in-world dates used by the Calendar and Timeline.
- **Comments** and **footnotes** — anchored to the text. See [Editor](05-editor.md).
- **Analysis overrides** — optional manual overrides for detected POV, emotion, intensity, conflict, and tags, used by Smart Lists and the Manuscript outliner.

### Creating a scene

Click **+ Scene** in the toolbar and enter a name. The scene is added to the chapter of the currently open scene (or the last chapter when nothing is open).

### Renaming a scene

Right-click the scene in the binder → **Rename**. The file on disk is renamed to match.

### Reordering and moving scenes

Drag within the same chapter to change order. Drop the scene onto another chapter to move it there; the file moves to the target chapter's folder and the snapshot history follows.

### Opening a scene in the split editor

Right-click → **Toggle split editor** opens the scene in a second editor pane beside the one you are writing in — for example to reference an earlier scene. See [Editor](05-editor.md#split-editor).

### Deleting a scene

Right-click → **Delete**. Asks to confirm. Snapshots of the deleted scene survive in the book's `Snapshots/` folder. If you might want the scene back, prefer **Archive**.

## Archiving scenes

Archiving removes a scene from the manuscript without deleting its text — useful for cut scenes you are not ready to throw away.

- Right-click a scene → **Archive**. The scene leaves its chapter; it no longer counts toward totals or exports.
- Click **ARCHIVE** at the bottom of the binder tree to show the list of archived scenes.
- Click **Restore** next to an archived scene to bring it back into the manuscript (it is restored into the first chapter; drag it from there to where it belongs).

## Status workflow

A typical novelist workflow with the five built-in chapter statuses:

1. **Outline** — bullet points or rough structure; not yet written.
2. **First Draft** — first pass through, complete or near-complete.
3. **Revised** — restructuring, voice fixes, scene-level edits done.
4. **Edited** — line edits, prose polish, copy-edits applied.
5. **Final** — ready to export.

The [Dashboard](11-dashboard.md) shows a breakdown of chapters at each status. The [Manuscript](10-manuscript.md) view filter lets you read only chapters at a given status.

## Acts

Acts are simple named buckets (e.g. "Act 1: Setup", "Act 2: Confrontation") that group chapters.

To assign a chapter to an act, right-click it → **Rename Act** and type the act name (use the same spelling for every chapter of that act). An act header appears in the binder above the first chapter of each act, the [Timeline](12-timeline.md) shows acts as its broadest grouping, and the [Plot Grid](08-plot-grid.md) groups columns by act.

The Timeline's **Add structure...** dropdown can lay out a known story structure (Three-Act, Save the Cat, Hero's Journey, 7-Point) as timeline events to plot against — see [Timeline](12-timeline.md).

## Snapshots

Take a snapshot of the open scene from the inspector (with an optional label such as "Before rewrite") and restore any earlier snapshot from the same list. Automatic snapshots are also taken before destructive operations such as Replace All. See [Snapshots](17-snapshots.md).

## Where to go next

- [Editor](05-editor.md) — formatting, split editor, comments, footnotes.
- [Plot Grid](08-plot-grid.md) — attach scenes to plotlines.
- [Calendar & in-world dates](13-calendar.md) — give scenes structured story dates.
- [Smart Lists](16-smart-lists.md) — saved scene queries.
