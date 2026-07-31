# Interface Overview

This page is a map of every region of the Novalist window. Keep it open in another tab while you explore the app — it's easier to learn the names of the regions once than to keep looking them up.

Keyboard shortcuts below are written with `Ctrl`; on macOS use `Cmd` instead.

![The Novalist workspace: activity bar, binder, editor, and inspector](images/interface-overview.png)

## The layout at a glance

Novalist uses an activity-bar layout: a slim icon rail switches views, and the binder beside it is only the chapter/scene tree.

```
┌────────────────────────────────────────────────────────────────────────┐
│ Toolbar:  [=] Project name [Book v] [Draft v]  +Chapter +Scene         │
│                             Search  Snapshots  Scene notes  Inspector   │
├────┬──────────────┬───────────────────────────────────┬────────────────┤
│ A  │  Binder      │            Main area              │  Inspector     │
│ c  │  (Chapters / │      (editor or active view)      │  (Context /    │
│ t  │  Smart Lists │                                   │   Footnotes    │
│ i  │  + tree)     │───────────────────────────────────│   tabs)        │
│ v  │              │   Scene notes dock (optional)     │                │
│ e  │              │   (Synopsis  |  Notes)            │                │
├────┴──────────────┴───────────────────────────────────┴────────────────┤
│ Status bar:  scene words │ project totals strip │ goals  git  Core     │
└────────────────────────────────────────────────────────────────────────┘
```

Navigation happens through the **activity bar**, the **Go** menu in the menu bar, and the command palette. The binder, the inspector, and the bottom scene-notes dock can each be hidden.

On Windows and Linux the menu bar is hidden so the toolbar can serve as the window's title bar; press `Alt` to show it. On macOS the menu bar sits in the system bar as usual.

## The toolbar

The slim bar at the top of the window — it doubles as the window's title bar, so it is also where you drag the window and where the minimise, maximise, and close buttons sit. On the left, from left to right:

- **Menu (burger)** — opens the **backstage drawer** (see below).
- **Project name** — double-click it to rename the project.
- **Book selector** — a dropdown listing the project's books. Pick one to switch; the last entry, **+ Add Book**, creates a new book. See [Projects & Books](03-projects-and-books.md).
- **Draft selector** — a dropdown listing the active book's drafts. Pick one to switch; **+ New draft (clone from current)** creates a new draft as a copy of the current one.
- **Delete draft** — removes the active draft (you are asked to confirm; a book always keeps at least one draft).
- **+ Chapter** — creates a new chapter.
- **+ Scene** — creates a new scene in the chapter of the currently open scene.

On the right:

- **Search** — opens [Find & Replace](21-find-replace.md) (`Ctrl+Shift+F`).
- **Snapshots** — opens the [Snapshots](17-snapshots.md) dialog for the open scene.
- **Layout toggles** — three buttons that show/hide the panes: the **binder** (left pane, `Ctrl+B`), the **inspector** (right pane, `Ctrl+Shift+B`), and the **scene-notes dock** (bottom, `Ctrl+Shift+N`).

## The activity bar

The slim icon rail on the far left is the top-level view switcher. From top to bottom it groups: **Write** (Dashboard, Manuscript); **Plan** (Timeline, Plot Grid, Calendar, Relationships); **World** (Codex, Maps, Research, Gallery); and **Publish** (Exposé, Export, Git). **Extensions** and **Settings** sit at the bottom. Extension views that contribute to the main area appear as extra icons. Hover any icon for its name; the active view is highlighted.

The **Editor** has no icon of its own — you reach it by opening a scene from the binder. The same destinations are also listed in the **Go** menu in the menu bar, and `Ctrl+1` through `Ctrl+9` jump to the most-used views — see [Hotkeys](26-hotkeys.md).

## The binder

The left pane is the chapter/scene tree only — view navigation lives in the activity bar.

### Tabs

- **Chapters** — the chapter/scene tree of the active book: act headers, status dots, per-scene word counts, drag-and-drop reordering, context menus, and the Archive section. See [Chapters & Scenes](04-chapters-and-scenes.md).
- **Smart Lists** — saved scene queries (e.g. "all scenes with POV = Alice that aren't in First Draft yet"). See [Smart Lists](16-smart-lists.md).

Click a scene to open it in the editor; the open scene is highlighted. Right-click chapters and scenes for their context menus.

### Ordering and narrowing the tree

Two selectors sit above the tree.

**Order** decides how the scenes inside each chapter are listed:

- **Reading order** — the book. This is where the binder starts, and the only order in which you can drag scenes to reorder them: a drag under a title sort would write a reorder you never meant, so dragging is switched off while another order is active.
- **Title**, **Word count**, **Stage** — ways of finding a scene rather than ways of arranging the book. Untriaged scenes sort last under Stage, because they are the ones with nothing said about them, not the ones at the earliest stage.

**Thread** narrows the tree to the scenes on one plot thread. It appears once the book has plotlines; see [Plot Grid](08-plot-grid.md).

### Pinning

Right-click a scene and choose **Pin to top** to put it in a **Pinned** group above the book, with the chapter it came from beside it. Pins survive restarts and are per project. The group only appears when something is pinned. **Unpin** removes it.

### Workspace layouts

Novalist always opened in the same shape, so planning, drafting and revising meant dragging the same panels back and forth several times a day. **Ctrl+Alt+L** (or "Workspace layouts" in the command palette) saves the shape you are in under a name and brings it back with one click.

A layout stores which view you are on, which binder and inspector tabs are open, whether the binder, inspector and scene-notes dock are visible, both panel widths, and whether focus mode is on. Saving under a name you have already used updates that layout rather than adding a second one.

Layouts are stored per machine, alongside your panel widths — they follow the computer you work on, not the project.

Drag the binder's right edge to resize it. It opens at a width proportional to your display, and once you drag it Novalist keeps that width for future sessions. The inspector and the scene-notes dock work the same way.

## The main area

The big region in the middle shows the active view: the scene **Editor** by default, or whichever view you picked in the activity bar (Dashboard, Timeline, Codex, Export, Settings, and so on). When a second scene is opened in the split editor, the main area shows two editor panes side by side. See [Editor](05-editor.md).

### The scene-notes dock

Below the editor sits an optional **scene-notes dock**, toggled from the toolbar or `Ctrl+Shift+N`. It holds the open scene's **Synopsis** and freeform **Notes** side by side, saved when you click away. Drag its top edge to resize it — the height you set is remembered. The dock only appears in the editor.

## The inspector

The right pane is the context sidebar for the open scene, with two tabs:

- **Context** — the scene's characters, mention frequency, locations, items, lore, and editable scene analysis (POV, emotion, intensity, conflict, tags). See [Inspector](22-context-sidebar.md).
- **Footnotes** — the footnotes and comments anchored in the open scene.

Per-scene snapshots are taken from the toolbar **Snapshots** button rather than the inspector. See [Snapshots](17-snapshots.md).

## The backstage drawer

The toolbar burger opens a left-anchored **backstage drawer**: create or open a project, import an Obsidian plugin, pick from recent projects, or jump to **Settings** / **Extensions**. Click outside or press `Escape` to close it.

## The status bar

The thin strip across the bottom:

- **Left** — the open scene's live word count, reading time, and readability badge.
- **Center** — an always-visible project metrics strip: words, chapters, scenes, characters, locations, reading time, and average words per chapter. Click it for the **project overview** popover, a per-chapter and per-scene breakdown with word bars, an estimated page count and readability.

  The page estimate is exactly that — an estimate. It divides the word count by a **words to a printed page** figure you set per project in Settings, and the popover says which figure it used. A trade paperback runs about 250 words to a page, mass-market nearer 300, large print about 150; the default is 250. Clearing the field puts it back. For an exact count rather than an estimate, export with the Normseiten preset, which is a real typeset grid.
- **Right** — daily and project goal progress, the git branch and changed-file count, and a compact **core-connection dot** (green once the bundled Novalist Core process is up; hover it for the version). If it never connects, see [Troubleshooting](28-troubleshooting.md).

## The command palette

`Ctrl+Shift+P` opens the **command palette**: type to fuzzy-filter the list of commands (including switching views), press `Enter` to run the highlighted one. See [Command Palette](25-command-palette.md).

## Focus mode

`Alt+F` toggles **focus mode**, which gives the window to the main area: both side panes, the toolbar and the status bar are hidden. Press `Alt+F` again to bring them back. Pair it with **Dim other paragraphs while writing** in [Settings](23-settings.md) → Editor for a full composition mode.

## Dialogs and overlays

All dialogs (chapter creation, find & replace, confirmations, the template editor, etc.) appear as in-window overlays with a dimmed background — there are no native modal windows. Dismiss most of them by clicking outside, pressing `Escape`, or using **Cancel**.

## Where to go next

- [Projects & Books](03-projects-and-books.md) — how your data is organized on disk.
- [Editor](05-editor.md) — the core writing surface.
- [Hotkeys](26-hotkeys.md) — every keyboard shortcut.

## Panes

The content area was one view at a time, with the editor allowed to split in two. Wanting the manuscript, the Codex and your notes at once meant picking two and swapping for the third.

Three controls sit in the top bar beside the panel toggles:

- **Split pane right** (`Ctrl+Alt+Right`) and **Split pane down** (`Ctrl+Alt+Down`) divide the pane you are in. The new pane opens on the same view, because splitting to look at two places in one manuscript is at least as common as splitting to look at two different things.
- **Close pane** (`Ctrl+Alt+W`) removes it. The last pane always stays.

Any pane can be split again, so three panes down the left and one tall one on the right is a shape you can build.

The pane you are working in is outlined. That matters because everything that changes a view — the activity bar, the command palette, a link in a panel — lands in **that** pane. Click anywhere in another pane to move there.

### Saved layouts

**Layouts** in the same group remembers an arrangement by name and puts it back later. A drafting layout with the manuscript and your notes, a revision layout with the manuscript, the Codex and the timeline.

Layouts are about your screen rather than your book, so they are remembered per machine and follow you between projects.
