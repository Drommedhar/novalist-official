# Hotkeys reference

This is the full list of keyboard shortcuts shipped with Novalist. On macOS, read `Ctrl` as `Cmd` — the two are treated as the same modifier.

Gestures are written in Novalist's key-gesture grammar: `D1` through `D9` are the number keys `1` through `9` on the main keyboard row, so `Ctrl+D1` means `Ctrl+1`.

## Navigation

The number keys switch the main area between views, in this order:

| Action | Gesture |
| --- | --- |
| Go to Editor | `Ctrl+D1` |
| Go to Dashboard | `Ctrl+D2` |
| Go to Timeline | `Ctrl+D3` |
| Go to Codex | `Ctrl+D4` |
| Go to Manuscript | `Ctrl+D5` |
| Go to Calendar | `Ctrl+D6` |
| Go to Relationships | `Ctrl+D7` |
| Go to Plot Grid | `Ctrl+D8` |
| Go to Research | `Ctrl+D9` |

## Panels and tools

| Action | Gesture |
| --- | --- |
| Toggle binder (left pane) | `Ctrl+B` |
| Toggle inspector (right pane) | `Ctrl+Shift+B` |
| Find & Replace | `Ctrl+Shift+F` |
| Toggle Focus Mode | `Alt+F` |
| Command Palette | `Ctrl+Shift+P` |

## Notes

- While the cursor is in a text field or the editor, only gestures that include `Ctrl` (or `Cmd`) fire, so plain typing and `Alt` shortcuts never interrupt your writing. `Alt+F` works whenever focus is outside a text field.
- Standard text-editing shortcuts — copy, cut, paste, select all, undo, redo (`Ctrl+C/X/V/A/Z/Y`, `Ctrl+Shift+Z`) — are handled natively by the writing surface.
- Every action listed here is also available by name in the [Command Palette](25-command-palette.md).

## Dialog conventions

These behaviors are consistent across the in-window dialogs:

- **`Enter` confirms** the primary action — OK, Create, Rename, and so on.
- **`Escape` cancels** every dialog and overlay, including the command palette and find/replace.
- Dialogs auto-focus their first input field when they open.

## Where to go next

- [Command Palette](25-command-palette.md) — every action by name regardless of binding.
- [Interface Overview](02-interface-overview.md) — the panes and views these shortcuts drive.
