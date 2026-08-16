# Interface Overview

This page is a map of every region of the Novalist window. Keep it open in another tab while you explore the app — it's easier to learn the names of the regions once than to keep looking them up.

Keyboard shortcuts below are written with `Ctrl`; on macOS use `Cmd` instead.

![The Novalist workspace: the binder, the editor, and the inspector](images/interface-overview.png)

## The layout at a glance

Novalist is organised around **five modes** — Write, Plan, World, Publish and Series — plus a Dashboard you can always return to. A mode is a workspace: picking one decides what the window looks like and which views are one click away.

```
┌───────────────────────────────────────────────────────────────────────────┐
│  File   Edit   Go   View   Window   Help                                  │
├───────────────────────────────────────────────────────────────────────────┤
│  Project name  [Book v] [Draft v]   + Chapter  + Scene        Find   More │
├──────┬────────────┬─────────────┬───────────────────────────┬─────────────┤
│ Mode │ Mode panel │   Binder    │         Main area         │  Inspector  │
│ rail │            │             │                           │             │
│ Dash │ Write   2  │  Chapters / │  Writing bar              │  Context /  │
│ Writ │  Drafting  │  Smart      │ ------------------------- │  Footnotes  │
│ Plan │   Editor   │  Lists      │  (the editor, or whatever │             │
│ Worl │   Manusc.  │  + tree     │   view you picked)        │             │
│ Publ │            │             │ ------------------------- │             │
│ Seri │            │             │  Scene notes (optional)   │             │
├──────┴────────────┴─────────────┴───────────────────────────┴─────────────┤
│  scene words  │  Project status  │  goals   git   Core                    │
└───────────────────────────────────────────────────────────────────────────┘
```

From the outside in: the **menu bar**, the **toolbar**, then a row of panels — the **mode rail**, the **mode panel**, the **binder**, the **main area** and the **inspector** — over the **status bar**. Not all of them are present at once; which ones you get is decided by the mode you are in.

## Where things live

Every command in Novalist has exactly **one** permanent home, and its scope decides which. Nothing sits in two places at once, so once you have learned where a kind of command lives you have learned where all of them live.

| The command acts on | You find it in |
| --- | --- |
| The text you have selected | The small toolbar that appears over the selection |
| The thing under the pointer — a name, a word, a misspelling | The right-click menu |
| The paragraph the caret is in, or the open scene | The **writing bar** above the editor |
| The open project | The **toolbar** at the top of the window |
| The application — where you are, what the window looks like, what is installed | The **menu bar** |
| A preference rather than an action | [Settings](23-settings.md) |

On top of that, **every** command is in the [command palette](25-command-palette.md), and **every** command can be given a keyboard shortcut in Settings → Keyboard shortcuts, whether or not it ships with one. The palette and the shortcut list are the two surfaces that are allowed to hold everything; the containers above each hold one slice.

## The menu bar

The menu bar is always visible on Windows and Linux, and sits in the system bar on macOS as usual. It is the app's complete index: every command that acts on the application as a whole is in it, and nothing else is.

- **File** — New Project, Browse for Project Folder, Import from Obsidian Plugin, Import a manuscript, Quick capture, Print, **Recent Projects** (a submenu of the last ten), and Exit (Close, on macOS).
- **Edit** — Undo, Redo, Cut, Copy, Paste, Select All. These are the system's own, so they behave exactly as they do in every other application.
- **Go** — every view in the app by name, grouped exactly as the modes group them: Dashboard; then Editor and Manuscript; then Timeline, Plot Grid, Planning board, Relationships, Calendar and Dialogue; then Codex, Wiki, Maps, Research, Gallery and Languages; then Exposé, Export, Git and Style report; then Series; then Extensions and Settings — with Quick Open and the Command Palette at the end.
- **View** — Show the mode panel, the binder, the inspector and the scene notes; Focus Mode; the pane commands (split right, split down, close, default layout, open in its own window); **Pane layouts** and **Workspace layouts**; interface size; and the developer items your platform provides.
- **Window** — brings the main window back after you have closed it, minimise, zoom, and the window list.
- **Help** — the **User manual**, **Take the tour**, **[About](#about)**, **Check for Updates** (absent in the Mac App Store build, which updates itself), and **Novalist on GitHub**.

On macOS there is also the usual application menu carrying About, Hide and Quit.

Menu items are greyed out rather than hidden when they cannot do anything — with no project open, most of **Go** is disabled, and you can still see what opening one would give you.

## The mode rail

The narrow labelled rail on the far left. Six entries, each with an icon **and its name**, so nothing has to be hovered to be identified and nothing is ever hidden behind a "...":

- **Dashboard** — home. Not part of any mode: it is where you are before you have decided what to do today, and where a project lands when you open it.
- **Write** — the Editor and the Manuscript view.
- **Plan** — Timeline, Plot Grid, Planning board, Relationships, Calendar, Dialogue.
- **World** — Codex, Wiki, Maps, Research, Gallery, Languages.
- **Publish** — Exposé, Style report, Export, Git.
- **Series** — the Series view, which is the one that sits above a single book.

With no project open the modes are shown disabled rather than absent, so the shape of the window is the same before and after you open one.

Settings, Extensions and About are not on the rail. They are about the installation rather than about a book, so they are reached from the menu bar, from the [command palette](25-command-palette.md), or from a link that deep-links into them.

## The mode panel

Beside the rail, the mode panel lists the views of the mode you are in. It is the same switcher in every mode: having found how to move between Timeline and Calendar, you already know how to move between Codex and Maps.

- The head names the mode and shows **how many views** it holds.
- Views are listed in **labelled groups** — Write has "Drafting"; Plan has "Shape" and "Cast and time"; World has "In this book" and "Reference"; Publish has "Prepare" and "Produce"; Series has "Across books". A mode with a single group shows no label, because a heading over the whole list says nothing the panel's title has not.
- Some rows carry a **count** on the right: the number of scenes beside Manuscript, the number of entries beside Codex.
- Views contributed by extensions sit in their own group, **From extensions**, always last — so installing something never reorders a view you already knew where to find.
- Past **ten** views, the panel grows a **filter** box. It is an accelerator, never the way something becomes reachable: every view is in the list whether or not you are filtering. While you filter, the group labels go, because they describe an order the filtered list no longer has.

**Hiding and showing it.** **View → Show the mode panel** docks or undocks it. In a narrow window it is an overlay instead of a column: picking a mode from the rail raises it, choosing a view closes it again, and clicking outside dismisses it. The rows and their order are the same either way.

The panel is not shown on the Dashboard, in Settings, in Extensions or in About — none of those belongs to a mode.

## What each mode gives you

The chrome around the main area belongs to the mode, not to the individual view. Five layouts instead of one rule per view:

| Mode | Binder | Inspector | Book and draft selectors | Status bar |
| --- | --- | --- | --- | --- |
| **Write** | Yes | Yes | Yes | Yes |
| **Plan** | — | — | Yes | Yes |
| **World** | — | — | Yes | Yes |
| **Publish** | — | — | Yes | Yes |
| **Series** | — | — | — | Yes |
| **Dashboard** | — | — | Yes | Yes |
| Settings, Extensions, About | — | — | — | — |

The binder, the inspector and the scene-notes dock are **Write's alone**. The Export view already has its own controls for choosing what goes in, and a second way to pick the same thing is exactly the duplication this arrangement removes.

Series is the one mode with no book and draft selectors, because it is the one mode that is not about a single book. Everywhere else the toolbar is the same toolbar, so what it holds is one thing to learn rather than five.

Nothing is taken away, only put away: every command is still in the [command palette](25-command-palette.md) and on whatever shortcut you have given it.

### The shell follows the window

The layout also responds to how much room it actually has, measured from the window rather than guessed from the monitor — so a narrow window on a wide screen reflows properly, and moving the window between monitors of different scaling reflows it again without a restart.

- **Wide** — mode panel, binder, main area and inspector side by side, as above.
- **Medium** — the inspector becomes a drawer you open when you want it, and secondary toolbar commands collect under the **More** menu.
- **Compact** — the mode panel and the binder become overlays over the main area rather than columns beside it, so the editor keeps a usable width instead of being squeezed out.

The width you drag a panel to is remembered as a preference, not as a demand: on a narrower window a panel is capped so the editor stays usable, and your stored width comes back when there is room for it again.

For how large the interface is drawn — as opposed to how it is arranged — see **Interface size** in [Settings → Appearance](23-settings.md#appearance).

## The toolbar

The strip under the menu bar is the **open project's** command bar, and it holds only commands that act on the project. On Windows and Linux it is no longer the window's title bar: your platform draws that, so the window is dragged and closed the way every other window on your machine is. On macOS the title bar is still hidden behind this strip, as it always was, and the strip is still where you drag the window.

From the left:

- **Project name** — double-click it to rename the project.
- **Book selector** — a dropdown listing the project's books. Pick one to switch; the last entry, **New Book**, creates one. See [Projects & Books](03-projects-and-books.md).
- **Draft selector** — the active book's drafts, with **New draft** at the end, plus **Compare drafts** and **Delete draft** in a wide window.
- **+ Chapter** and **+ Scene** — create a chapter, or a scene in the chapter of the open scene.
- **Find** — opens [Find & Replace](21-find-replace.md) (`Ctrl+Shift+F`), in a wide window.
- **More** — always present, and the home for the project commands that do not have a button: **Clean up the manuscript**, **Rename project**, and, as the window narrows, whichever of the above have folded into it.

The panel toggles, the pane split and close buttons, the layouts dropdown and the Snapshots button are **no longer on the toolbar**. Showing a panel or splitting the window shapes the application, so it is in the **View** menu; a snapshot is of the scene in front of you, so it is on the [writing bar](05-editor.md#the-writing-bar).

## Before a project is open

There is no separate start screen. With nothing open, the window is the same window — same menu bar, same rail, same toolbar — and the main area holds the welcome content: **New Project**, **Browse for Project Folder**, **Import from Obsidian Plugin**, links to Settings and the manual, your recent projects with their covers, and the scratchpad. Anything that needs a project is visibly disabled rather than missing.

Recent projects are also in **File → Recent Projects**, which is where a reader of any other application would look for them. Both lists show only projects that are still on disk: one you have deleted or moved is dropped rather than offered.

## The binder

The left pane in Write is the chapter/scene tree. View navigation lives in the mode rail and the mode panel, not here.

### Tabs

- **Chapters** — the chapter/scene tree of the active book: act headers, status dots, per-scene word counts, drag-and-drop reordering, context menus, and the Archive section. See [Chapters & Scenes](04-chapters-and-scenes.md).
- **Smart Lists** — saved scene queries (e.g. "all scenes with POV = Alice that aren't in First Draft yet"). See [Smart Lists](16-smart-lists.md).
- **Collections** — hand-curated groups of scenes, and **Bookmarks** — scenes you marked to come back to. Both are in [Smart Lists](16-smart-lists.md) too.

Click a scene to open it in the editor; the open scene is highlighted. Right-click chapters and scenes for their context menus.

### Ordering and narrowing the tree

Two selectors sit above the tree.

**Order** decides how the scenes inside each chapter are listed:

- **Reading order** — the book. This is where the binder starts, and the only order in which you can drag scenes to reorder them: a drag under a title sort would write a reorder you never meant, so dragging is switched off while another order is active.
- **Title**, **Word count**, **Stage** — ways of finding a scene rather than ways of arranging the book. Untriaged scenes sort last under Stage, because they are the ones with nothing said about them, not the ones at the earliest stage.

**Thread** narrows the tree to the scenes on one plot thread. It appears once the book has plotlines; see [Plot Grid](08-plot-grid.md).

### Pinning

Right-click a scene and choose **Pin to top** to put it in a **Pinned** group above the book, with the chapter it came from beside it. Pins survive restarts and are per project. The group only appears when something is pinned. **Unpin** removes it.

Drag the binder's right edge to resize it. It opens at a width proportional to your display, and once you drag it Novalist keeps that width for future sessions. The mode panel, the inspector and the scene-notes dock work the same way.

### Workspace layouts

Novalist always opened in the same shape, so planning, drafting and revising meant dragging the same panels back and forth several times a day. **View → Workspace layouts** (`Ctrl+Alt+L`) saves the shape you are in under a name and brings it back with one click.

A layout stores which view you are on, which binder and inspector tabs are open, whether the binder, inspector and scene-notes dock are visible, both panel widths, and whether focus mode is on. Saving under a name you have already used updates that layout rather than adding a second one.

Layouts are stored per machine, alongside your panel widths — they follow the computer you work on, not the project.

## The main area

The big region in the middle shows the active view: the scene **Editor**, or whichever view you picked from the mode panel or the **Go** menu.

### Splitting the main area into panes

The main area can be split into as many **panes** as you have room for, each showing a different view — the manuscript beside the Codex beside your research, or two scenes side by side. Modes govern the primary workspace; panes stay free, so any view can go in any pane whatever mode you are in.

- **Split** from **View → Split pane right / Split pane down**, `Ctrl+Alt+Right` and `Ctrl+Alt+Down`, or the split buttons in a pane's own header. The new pane opens on the same view, because splitting to look at two places in one manuscript is at least as common as splitting to look at two different things.
- **Choose what a pane shows** from its **header**: the pane's name (top left of the pane) is a menu of every view, grouped the way the modes group them, with the **Editor** included. A pane appears with a header as soon as the window holds more than one; a single-pane window looks as it always did.
- **Resize** by dragging the divider between two panes.
- **Close** a pane from **View → Close pane** or `Ctrl+Alt+W`, or with the `×` in its header. The last pane always stays.
- The **active pane** is outlined. Everything that changes a view — the mode panel, the **Go** menu, the command palette, a link in a panel — lands there, and clicking anywhere inside a pane makes it the active one.

A pane split off the editor starts empty and waits for a scene: click one in the binder and it opens there. Two editor panes are two independent scenes, each with its own [tab strip](05-editor.md#scene-tabs) and its own auto-save.

### Pane layouts

**View → Pane layouts** opens a dialog that saves the arrangement of panes you are in under a name and brings it back later. These are the pane arrangements; the [workspace layouts](#workspace-layouts) above are the panel and view state, and the two are stored separately.

The dialog marks the layout you are currently in. Split a pane, close one or drag a divider and that mark clears by itself — the arrangement is no longer the one that was saved. Type a name and **Save this layout** to store the current one; the bin icon beside a layout forgets it.

**Default** at the top of the list is always there and cannot be deleted: it collapses the window back to a single pane, whatever you have split it into. It is the arrangement Novalist starts in, so it is the way back when a layout is not what you want. The view you are on comes with you — going back to one pane changes how the window is arranged, not where you are in the book.

### Opening a pane in its own window

**View → Open in its own window**, and the same button in every pane header, tears the pane out into a second window: the Codex on another monitor while the manuscript stays where it is.

The new window runs the real view against the same project, so edits made in it land in the book like any other. It opens on the project the pane came from, and on the same scene when the editor is what you tore out. It has no mode rail and no binder — a torn-off pane is one thing on purpose — but it keeps its pane header, so you can point it at a different view or split it further.

### The scene-notes dock

Below the editor sits an optional **scene-notes dock**, toggled from **View → Toggle scene notes** or `Ctrl+Shift+N`. It holds the open scene's **Synopsis** and freeform **Notes** side by side, saved when you click away. Drag its top edge to resize it — the height you set is remembered. The dock only appears in the editor.

## The inspector

The right pane in Write is the context sidebar for the open scene, with these tabs:

- **Context** — the scene's characters, mention frequency, locations, items, lore, and editable scene analysis (POV, emotion, intensity, conflict, tags). See [Inspector](22-context-sidebar.md).
- **Footnotes** — the footnotes and comments anchored in the open scene.
- **Inbox** — suggested edits waiting elsewhere in the book, and the passages you have cut and kept.

Per-scene snapshots are taken from the **writing bar** above the editor rather than from the inspector or the toolbar. See [Snapshots](17-snapshots.md).

## The status bar

The thin strip across the bottom:

- **Left** — the open scene's live word count, reading time, and readability badge.
- **Center** — a single **Project status** button. Click it for the **project overview** popover: chapters, scenes, characters, locations, reading time, average words per chapter, goal progress, and a per-chapter and per-scene breakdown with word bars, an estimated page count and readability.

  The page estimate is exactly that — an estimate. It divides the word count by a **words to a printed page** figure you set per project in Settings, and the popover says which figure it used. A trade paperback runs about 250 words to a page, mass-market nearer 300, large print about 150; the default is 250. Clearing the field puts it back. For an exact count rather than an estimate, export with the Normseiten preset, which is a real typeset grid.
- **Right** — daily and project goal progress, the git branch and changed-file count, and a compact **core-connection dot** (green once the bundled Novalist Core process is up). The core process's version number is on the [About](#about) page. If it never connects, see [Troubleshooting](28-troubleshooting.md).

The status bar stays visible with no project open, because that is exactly when you most want to know whether the core process came up.

## About

**Help → About** opens the About view. It holds the facts about your installation that used to have nowhere to live:

- The **Novalist version** and the **core process version**, side by side.
- Links to the project on GitHub and to **report an issue**.
- **What's new** — the changelog, rendered in the app, so "should I update" can be answered without leaving it.
- **Check for updates** — the same check the Help menu runs. Absent in the Mac App Store build, which delivers its own updates.
- **Third-party licences** — the typefaces and runtimes Novalist ships, with their terms and notices.
- **Copy system information** — one button that puts a support-ready block on the clipboard: versions, platform, interface language, system locale, and the window and display measurements. It contains no project names, no file paths and none of your writing.

About belongs to no mode, so it is reachable with nothing open. See [About](44-about.md) for the whole page.

## In-app help

**Help → User manual**, or the manual entry in the command palette, opens this manual inside the app. Give the command a shortcut of your own in Settings → Keyboard shortcuts if you want one.

It opens **where you are**. On the Timeline it opens the Timeline page; in the Codex, the Codex page; in Settings, the page for the section you have open; in About, this page's About section. A link that points at a particular section arrives at that section rather than at the top of the page, so "read more about typewriter scrolling" lands on typewriter scrolling.

The page list down the left side holds the whole manual, and the search box above it searches the text of every page — so opening help in the right place never stops you going somewhere else.

## The command palette

`Ctrl+Shift+P` opens the **command palette**: type to fuzzy-filter, press `Enter` to run the highlighted command. It now lists **every** command Novalist has, not only the ones carrying a shortcut, and it hides the ones that cannot do anything right now. See [Command Palette](25-command-palette.md).

## Focus mode

`Alt+F` toggles **focus mode**, which gives the window to the main area: the mode rail, the mode panel, both side panes, the toolbar and the status bar are all hidden. Press `Alt+F` again to bring them back. Pair it with **Dim other paragraphs while writing** in [Settings](23-settings.md) → Editor for a full composition mode.

## Dialogs and overlays

All dialogs (chapter creation, find & replace, confirmations, pane layouts, snapshots, the template editor, and so on) appear as in-window overlays with a dimmed background — there are no native modal windows. Dismiss most of them by clicking outside, pressing `Escape`, or using **Cancel**.

## Where to go next

- [Projects & Books](03-projects-and-books.md) — how your data is organized on disk.
- [Editor](05-editor.md) — the core writing surface, the writing bar, and the selection toolbar.
- [About](44-about.md) — versions, the changelog in the app, and third-party licences.
- [Hotkeys](26-hotkeys.md) — every default keyboard shortcut, and how to give one to anything else.
