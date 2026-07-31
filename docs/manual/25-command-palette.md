# Command Palette

The Command Palette is a single text box that can run any registered command. If you don't remember where a view lives or what its hotkey is, you can usually get there faster by typing.

![The command palette](images/command-palette.png)

## Opening the palette

Press `Ctrl+Shift+P` (`Cmd+Shift+P` on macOS). The palette opens as an overlay in the center of the window.

## Using it

- Type any part of a command's name to filter the list.
- Use **Up / Down** to move the highlight, **Enter** to run the highlighted command, **Escape** to close.
- You can also click a command, or hover to highlight it.
- Each command shows its keyboard shortcut on the right, if it has one.
- Clicking outside the palette closes it without running anything.

## What's in the palette

Every registered command:

- **View switching** — jump to the Editor, Manuscript, Dashboard, Timeline, Plot Grid, Calendar, Relationships, Codex, or Research view by name.
- **Panel toggles** — toggle the binder (left pane) and the inspector (right pane).
- **Find & Replace** — open the project-wide find/replace dialog.
- **Focus Mode** — hide both side panes for distraction-free writing.
- **Commands from your extensions** — anything an installed extension registers, listed under its own name. The AI Assistant's critique passes, for instance, appear here once it is installed.

Extension commands are read when the palette opens, so one that installs while Novalist is running shows up the next time you press `Ctrl+Shift+P` — no restart. They have no keyboard shortcut of their own unless the extension also binds one, so the right-hand column is empty for them.

A command that takes arguments is not listed, because the palette has no way to ask you for them. Those are for scripts and for the extension's own buttons.

## Tips

- **Use it instead of memorizing hotkeys.** Typing three letters of a view's name is often faster than recalling its number key.
- **Use it from Focus Mode.** With both panes hidden, the palette is the quickest way to switch views without leaving the keyboard.

## Where to go next

- [Quick Open](31-quick-open.md) — the content-finding counterpart: `Ctrl+P` searches scenes, Codex, notes, and research.
- [Hotkeys](26-hotkeys.md) — the full list of default keybindings, including the palette's own.
- [Interface Overview](02-interface-overview.md) — the binder, main area, and inspector the palette navigates between.
