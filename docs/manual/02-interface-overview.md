# Interface Overview

This page is a map of every region of the Novalist window. Keep it open in another tab while you explore the app — it's easier to learn the names of the regions once than to keep looking them up.

Keyboard shortcuts below are written with `Ctrl`; on macOS use `Cmd` instead.

## The layout at a glance

Novalist uses a three-pane layout:

```
┌──────────────────────────────────────────────────────────────────┐
│ Toolbar:  [=]  Project name  [Book v] [Draft v]  +Chapter +Scene │
│                                              Search  Inspector   │
├──────────────┬───────────────────────────────────┬───────────────┤
│  Binder      │                                   │  Inspector    │
│  (Chapters / │            Main area              │  (synopsis,   │
│  Smart Lists │      (editor or active view)      │   notes,      │
│  + tree)     │                                   │   snapshots)  │
│  ──────────  │                                   │               │
│  View rail   │                                   │               │
│  (Write,     │                                   │               │
│   Plan, ...) │                                   │               │
├──────────────┴───────────────────────────────────┴───────────────┤
│ Status bar:  scene words  |  project totals  |  Core connected   │
└──────────────────────────────────────────────────────────────────┘
```

All navigation happens through the toolbar, the binder's view rail, and the command palette. Both side panes can be hidden.

## The toolbar

The slim bar at the top of the window. From left to right:

- **Binder toggle** — shows/hides the left pane (`Ctrl+B`).
- **Project name** — double-click it to rename the project.
- **Book selector** — a dropdown listing the project's books. Pick one to switch; the last entry, **+ Add Book**, creates a new book. See [Projects & Books](03-projects-and-books.md).
- **Draft selector** — a dropdown listing the active book's drafts. Pick one to switch; **+ New draft (clone from current)** creates a new draft as a copy of the current one.
- **+ Chapter** — creates a new chapter.
- **+ Scene** — creates a new scene in the chapter of the currently open scene.
- **Search** — opens [Find & Replace](21-find-replace.md) (`Ctrl+Shift+F`).
- **Inspector toggle** — shows/hides the right pane (`Ctrl+Shift+B`).

## The binder

The left pane has three parts, top to bottom:

### Tabs

- **Chapters** — the chapter/scene tree of the active book: act headers, status dots, per-scene word counts, drag-and-drop reordering, context menus, and the Archive section. See [Chapters & Scenes](04-chapters-and-scenes.md).
- **Smart Lists** — saved scene queries (e.g. "all scenes with POV = Alice that aren't in First Draft yet"). See [Smart Lists](16-smart-lists.md).

### The tree

Click a scene to open it in the editor; the open scene is highlighted. Right-click chapters and scenes for their context menus.

### The view rail

Below the tree, grouped navigation buttons switch what the main area shows:

| Group | Views |
| --- | --- |
| **Write** | Editor, Manuscript, Dashboard |
| **Plan** | Timeline, Plot Grid, Calendar, Relationships |
| **World** | Codex, Maps, Research, Gallery |
| **Publish** | Export, Git |
| **Application** | Settings |

When installed extensions contribute views (for example an AI Chat panel), an **Extensions** group appears with one button per contributed view. See [Extensions](24-extensions.md).

The active view is highlighted. `Ctrl+1` through `Ctrl+9` jump directly to the most-used views — see [Hotkeys](26-hotkeys.md).

## The main area

The big region in the middle shows the active view: the scene **Editor** by default, or whichever view you picked in the rail (Dashboard, Timeline, Codex, Export, Settings, and so on). When a second scene is opened in the split editor, the main area shows two editor panes side by side. See [Editor](05-editor.md).

## The inspector

The right pane shows details for the open scene:

- **Title and word count** at the top.
- **Synopsis** — a short summary, saved when you click away.
- **Scene notes** — longer freeform notes, saved when you click away.
- **Scene snapshots** — take a snapshot (with an optional label) and restore earlier ones. See [Snapshots](17-snapshots.md).

## The status bar

The thin strip across the bottom:

- **Left** — the open scene's word count and title.
- **Center** — project totals: words, chapters, scenes.
- **Right** — the core-connection status: **Core connected (version)** once the bundled Novalist Core process is up, **Connecting to core...** while it starts. If it never connects, see [Troubleshooting](28-troubleshooting.md).

## The command palette

`Ctrl+Shift+P` opens the **command palette**: type to fuzzy-filter the list of commands (including switching views), press `Enter` to run the highlighted one. See [Command Palette](25-command-palette.md).

## Focus mode

`Alt+F` toggles **focus mode**, which hides both side panes so only the toolbar, main area, and status bar remain. Press `Alt+F` again to bring the panes back.

## Dialogs and overlays

All dialogs (chapter creation, find & replace, confirmations, the template editor, etc.) appear as in-window overlays with a dimmed background — there are no native modal windows. Dismiss most of them by clicking outside, pressing `Escape`, or using **Cancel**.

## Where to go next

- [Projects & Books](03-projects-and-books.md) — how your data is organized on disk.
- [Editor](05-editor.md) — the core writing surface.
- [Hotkeys](26-hotkeys.md) — every keyboard shortcut.
