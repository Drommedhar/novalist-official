# Interface Overview

This page is a map of every region of the Novalist window. Keep it open in another tab while you explore the app — it's easier to learn the names of the regions once than to keep looking them up.

## The layout at a glance

From left to right, top to bottom:

```
┌─────────────────────────────────────────────────────────────────────┐
│ Menu bar (Edit, View) — Windows/Linux only                          │
├─────────────────────────────────────────────────────────────────────┤
│ App bar:  menu  [Book v]   +Chapter  +Scene   |   |   |   Snapshots │
├──┬───────────────┬───────────────────────────────────────────────┬──┤
│  │               │                                               │  │
│ A│   Sidebar     │              Content area                     │ C│
│ c│ (Chapters /   │   (editor, dashboard, codex, etc.)            │ o│
│ t│  Entities /   │                                               │ n│
│ i│  SmartLists)  │                                               │ t│
│ v│               ├───────────────────────────────────────────────┤ e│
│ i│               │              Scene notes (bottom panel)       │ x│
│ t│               │                                               │ t│
│ y│               │                                               │  │
├──┴───────────────┴───────────────────────────────────────────────┴──┤
│ Status bar:  1,234 words · 6 min read · F-K 65  |  project totals  │
└─────────────────────────────────────────────────────────────────────┘
```

The activity bar is **column A**. The context sidebar is **column C**. Both can be hidden.

## The menu bar (Windows / Linux)

The classic menu bar at the very top contains only two menus:

- **Edit**
  - **Command Palette** — `Ctrl+Shift+P`
  - **Find & Replace** — `Ctrl+H`
  - **Take Snapshot** — capture the current scene's state.
  - **Snapshots** — open the snapshot browser.
- **View**
  - **Toggle Explorer** — show/hide the left sidebar.
  - **Toggle Context Sidebar** — show/hide the right sidebar.
  - **Toggle Scene Notes** — show/hide the bottom panel.
  - **Focus Mode** — `F11`, hides all chrome.
  - **Plot Grid** — open the plot grid.
  - **Research** — open the research view.
  - **Toggle Split Editor** — open a second editor pane side-by-side.

On macOS, these items live under the app menu.

## The app bar

The slim toolbar below the menu bar. From left to right:

- **Hamburger button** (three horizontal lines, far left) — opens the **Start menu** (see below).
- **Book picker** — shows the active book name with a chevron. Click to open the **book picker overlay** where you can switch between books, add a book, rename, or delete one.
- **+Chapter** — creates a new chapter via the [Chapter dialog](04-chapters-and-scenes.md).
- **+Scene** — creates a new scene via the [Scene dialog](04-chapters-and-scenes.md).
- **Explorer toggle** — shows/hides the left sidebar (the chapter/entity panel).
- **Context sidebar toggle** — shows/hides the right panel.
- **Scene notes toggle** — shows/hides the bottom panel.
- **Snapshots** — opens the snapshot manager for the current scene.

The app bar is hidden when **Focus Mode** is on.

## The activity bar

The narrow icon strip on the far left. The top section holds **project views**, the bottom section holds **activity views** and **app-wide actions**:

| Icon | View | Description |
| --- | --- | --- |
| Bar chart | **Dashboard** | Word counts, goals, status breakdown, echo phrases. See [Dashboard](11-dashboard.md). |
| Calendar | **Timeline** | Chronological event view. See [Timeline](12-timeline.md). |
| Book open | **Codex Hub** | Unified character / location / item / lore browser. See [Codex](06-codex.md). |
| Scroll | **Manuscript** | Read the whole book as a continuous document. See [Manuscript](10-manuscript.md). |
| Calendar (multi-day) | **Calendar** | In-world calendar with scene placement. See [Calendar](13-calendar.md). |
| User | **Relationships** | Auto-clustered character relationship graph. See [Relationships](14-relationships.md). |
| Upload | **Export** | Export to EPUB, DOCX, PDF, Markdown, more. See [Export](20-export.md). |
| Image | **Gallery** | All project images. See [Image Gallery](19-image-gallery.md). |
| Git branch | **Git** | Version control panel (only visible if the project is a Git repo). See [Git](18-git.md). |
| Plug | **Extensions** | Manage installed extensions; browse the store. See [Extensions](24-extensions.md). |
| Gear | **Settings** | App settings and per-project settings. See [Settings](23-settings.md). |

Active view is highlighted. Extensions can contribute additional activity bar items; they appear after the built-in ones.

## The left sidebar

Tabs at the top let you switch between three panels:

- **Chapters** — tree view of every chapter and scene in the active book. Drag to reorder; right-click for context menu (rename, delete, duplicate, set status, set label color, etc.). See [Chapters & Scenes](04-chapters-and-scenes.md).
- **Entities** — flat list of every entity (characters, locations, items, lore, custom types) grouped by type. Click to open the entity editor. See [Codex](06-codex.md).
- **Smart Lists** — saved scene queries (e.g. "all scenes with POV = Alice that aren't in First Draft yet"). See [Smart Lists](16-smart-lists.md).

Extensions can add their own sidebar tabs.

## The content area

The big region in the middle. What shows here depends on the active view:

- A **scene** is loaded in the WYSIWYG **Editor**.
- The **Dashboard**, **Timeline**, **Manuscript**, **Codex**, **Calendar**, **Relationships**, **Plot Grid**, **Research**, **Export**, **Gallery**, and **Git** views replace the editor when their activity bar icons are clicked.
- Extensions can contribute their own full-area content views.

Above the content area is a **tab strip** showing every open scene and entity editor. Tabs can be reordered by drag, closed with the × button, moved to the secondary editor pane via right-click, and dragged to the other pane. The split-editor toggle (View → Toggle Split Editor) opens a second pane next to the first.

## The context sidebar

The optional right-hand panel. Tabs at the top:

- **Context** — live scene analysis (POV, emotional tone, dialogue/sentence/word counts, detected entities). See [Context sidebar](22-context-sidebar.md).
- **Footnotes** — list of footnotes in the current scene.
- Extension-contributed tabs.

## The scene notes panel

The optional bottom panel. Holds the current scene's **synopsis**, **notes**, and **comments**. See [Scene Notes](22-context-sidebar.md#scene-notes).

## The status bar

The thin strip across the bottom of the window. Three regions:

- **Left** — when a scene is open: word count, reading time, readability badge. Hover the word count for a tooltip showing characters with and without spaces. When no scene is open you see a hint instead.
- **Center** — clickable button showing **project totals**: total words, chapter count, scene count, character count, location count, average words per chapter, reading time. Clicking opens the **Project Overview** pop-up that lists every chapter with its word count and readability and lets you drill into individual scenes. The pop-up also has a **Rename project** button.
- **Right** — extension-contributed status items, Git status (branch name and number of changed files), daily-goal progress bar, project-goal progress bar.

## The Start menu

Click the hamburger button on the app bar. A slim left-edge overlay opens with:

- **Open Project**
- **Recent Projects** (sub-menu of the last few projects you opened)
- **Settings**
- **Extensions**
- **Close Project**

The app version number is shown at the bottom.

## The Command Palette

`Ctrl+Shift+P` (or **Edit → Command Palette**) opens a searchable list of every action in the app, with the hotkey shown next to each one. Type to filter; press `Enter` to execute. See [Command Palette](25-command-palette.md).

## Focus Mode

`F11` toggles **Focus Mode** which hides the menu bar, app bar, sidebars, and tab strip. Only the editor remains. Press `F11` again to come back.

## Dialogs and overlays

Modal dialogs (chapter creation, scene creation, find/replace, snapshots, settings, etc.) appear in-window with a dimmed background. Click outside to dismiss most of them, or use **Cancel** / **Escape**.

A short-lived **toast** appears at the bottom-center for non-blocking messages (e.g. "Snapshot taken", "Export complete"). Click a toast to dismiss it.

## Where to go next

- [Projects & Books](03-projects-and-books.md) — how your data is organized on disk.
- [Editor](05-editor.md) — the core writing surface.
- [Hotkeys](26-hotkeys.md) — every keyboard shortcut.
