# Hotkeys reference

This is the full list of **default** keyboard shortcuts shipped with Novalist. On macOS, read `Ctrl` as `Cmd` — the two are treated as the same modifier.

Gestures are written in Novalist's key-gesture grammar: `D1` through `D9` are the number keys `1` through `9` on the main keyboard row, so `Ctrl+D1` means `Ctrl+1`.

A shortcut is a **property of a command, not the reason one exists**. Every command Novalist has is listed in **Settings → Keyboard shortcuts**, whether or not it ships with a gesture, and any of them can be given one: click a command's gesture button, press the combination to capture it (Novalist warns you if it clashes with another command), and reset one or all of them back to the defaults at any time. See [Settings](23-settings.md#hotkeys). Most commands ship unbound — the table below is what a fresh install binds.

## Navigation

The number keys switch the main area between views, in this order:

| Command | Gesture |
| --- | --- |
| Editor | `Ctrl+D1` |
| Dashboard | `Ctrl+D2` |
| Timeline | `Ctrl+D3` |
| Codex | `Ctrl+D4` |
| Manuscript | `Ctrl+D5` |
| Calendar | `Ctrl+D6` |
| Relationships | `Ctrl+D7` |
| Plot Grid | `Ctrl+D8` |
| Research | `Ctrl+D9` |

One view outside that run has a gesture of its own, because the nine digits are already spoken for and inserting a tenth would renumber all of them:

| Command | Gesture |
| --- | --- |
| Narration | `Ctrl+Alt+R` |

Every other view — Wiki, Maps, Languages, Gallery, Dialogue, Planning board, Series, Exposé, Style report, Export, Git, Extensions, Settings — is a command with no default gesture. Reach it from its mode in the [mode panel](02-interface-overview.md#the-mode-panel), from the **Go** menu, or from the palette; bind it yourself if you go there often.

## Panels and panes

| Command | Gesture |
| --- | --- |
| Toggle binder (left pane) | `Ctrl+Alt+B` |
| Toggle inspector (right pane) | `Ctrl+Alt+I` |
| Toggle scene notes (bottom dock) | `Ctrl+Shift+N` |
| Show the mode panel | *(unbound)* |
| Focus Mode | `Alt+F` |
| Split pane right | `Ctrl+Alt+Right` |
| Split pane down | `Ctrl+Alt+Down` |
| Close pane | `Ctrl+Alt+W` |
| Default pane layout | *(unbound)* |
| Open in its own window | *(unbound)* |
| Pane layouts | *(unbound)* |
| Workspace layouts | `Ctrl+Alt+L` |
| Increase interface scale | `Ctrl+Plus` |
| Decrease interface scale | `Ctrl+-` |
| Reset interface scale | `Ctrl+0` |

**Interface scale** is the size of Novalist's own chrome, not of your manuscript — see [Settings → Appearance](23-settings.md#appearance).

## Project

| Command | Gesture |
| --- | --- |
| Find and Replace | `Ctrl+Shift+F` |
| Quick capture (jot a note) | `Ctrl+Shift+K` |
| Clean up the manuscript | *(unbound — it rewrites the prose in every scene it touches, so it ships without a gesture)* |
| New chapter | *(unbound)* |
| New scene | *(unbound)* |
| New Book, New draft, Compare drafts, Delete draft, Rename project | *(unbound)* |

## Editor

| Command | Gesture |
| --- | --- |
| Comment | `Ctrl+Shift+M` |
| Peek at entity under caret | `Ctrl+Shift+E` |
| Bold | `Ctrl+B` |
| Italic | `Ctrl+I` |
| Underline | `Ctrl+U` |
| Strikethrough, Highlight, Link, Footnote | *(unbound)* |
| Paragraph styles, lists, alignment | *(unbound)* |
| Scene snapshots, Suggest edits, Read aloud, and the writing options | *(unbound)* |

**Comment** and **Peek at entity under caret** both need something to act on — a selection, and a linked Codex name under the caret — and do nothing without it.

## General

| Command | Gesture |
| --- | --- |
| Command Palette | `Ctrl+Shift+P` |
| Quick Open (search everything) | `Ctrl+P` |
| Print | `Ctrl+Alt+P` |
| Take the tour | `Ctrl+Alt+T` |
| User manual | `F1` |
| About Novalist | *(unbound — Help → About)* |
| New Project, Browse for Project Folder, Import from Obsidian Plugin, Import a manuscript | *(unbound — the File menu)* |

## Notes

- **Reopen the main window** is in the Window menu, named after the app, and carries no gesture. On macOS, closing the last window leaves Novalist running; use the menu item, or click the dock icon, to bring the project back.
- Shortcuts shown beside a menu item are **displayed, not registered by the menu**: the app itself listens for every gesture, including inside the editor, so a shortcut you rebind takes effect everywhere at once.
- While the cursor is in a text field or the editor, only gestures that include `Ctrl` (or `Cmd`) fire, so plain typing and `Alt` shortcuts never interrupt your writing. `Alt+F` works whenever focus is outside a text field.
- Standard text-editing shortcuts — copy, cut, paste, select all, undo, redo (`Ctrl+C/X/V/A/Z/Y`, `Ctrl+Shift+Z`) — are handled natively by the writing surface, and are also in the **Edit** menu.
- On Apple keyboards `Cmd+B`, `Cmd+I` and `Cmd+U` stay with the writing surface as bold, italic and underline, and `Ctrl+B`, `Ctrl+I` and `Ctrl+U` do the same everywhere else, so the gesture means the same thing on every platform.
- Every command is also available by name in the [Command Palette](25-command-palette.md), which shows the gesture you have bound to it, if any.

## Dialog conventions

These behaviors are consistent across the in-window dialogs:

- **`Enter` confirms** the primary action — OK, Create, Rename, and so on.
- **`Escape` cancels** every dialog and overlay, including the command palette and find/replace.
- Dialogs auto-focus their first input field when they open.

## Where to go next

- [Command Palette](25-command-palette.md) — every command by name regardless of binding.
- [Interface Overview](02-interface-overview.md) — the modes, panels and menus these shortcuts drive.
- [Settings](23-settings.md#hotkeys) — the rebinding editor, which lists every command in the app.
