# Research

The Research view is your project's scrapbook: quick notes, reference URLs, and pointers to files — anything that informs the writing but is not a scene or an entity.

Research is per-project. It is not tied to a specific book, scene, or character, and it travels with the project folder.

## Opening Research

Open it from the **World** group in the binder's view rail (**Research**), from the command palette, or with `Ctrl+9` (macOS uses Cmd).

## Layout

The view is a two-pane list-and-editor:

- **Left** — the action buttons, a search box, and the list of research items, each showing its title and type. Click an item to open it.
- **Right** — the editor for the selected item.

The **search box** filters the list by title, content, or tag (substring match).

## Adding items

Three buttons sit at the top of the list:

- **Add Note** — creates an empty note.
- **Add Link** — creates a link item pre-filled with `https://`.
- **Import File** — opens the system file picker and copies the chosen file into the project. Novalist classifies it by extension: images become **Image** items, PDFs become **Pdf** items, and everything else becomes a **File** item.

## Items

Every research item has:

- **Title**
- **Type** — one of **Note**, **Link**, **File**, **Image**, or **Pdf**. You can change the type in the editor.
- **Content** — the body. For notes this is the text itself; for links, the URL; for files, images, and PDFs, the file path.
- **Tags** — comma-separated labels.

For **Image** items the editor shows a **preview** of the picture. For **File**, **Image**, and **Pdf** items it shows the file's **metadata** — its path, size, and last-modified date.

Depending on the type, the editor's action row offers:

- **Open External** — opens a link in your browser, or a file/image/PDF in its default application. Available for Link and file-backed items.
- **Reveal** — shows the file in your system file manager. Available for file-backed items.
- **Delete** — removes the item; a confirmation dialog asks first.

Everything saves automatically as you edit — changes are written when you leave a field.

## Tags

Tags are comma-separated labels on each item. Useful schemes:

- By topic: `medieval`, `sailing`, `chemistry`.
- By chapter: `ch1`, `ch4`.
- By status: `to-read`, `consulted`.

## Tips

- **Capture once, in detail.** It is much faster to write while reading research notes inside the app than to alt-tab to a browser.
- **Tag by chapter when relevant.** A `ch3` tag on every piece of research you needed for chapter 3 makes copy-edits trivial: skim, re-read, fix.
- **Don't over-organize.** A flat list with sensible titles plus a few tags beats a deep hierarchy.

## Where to go next

- [Codex](06-codex.md) — for worldbuilding that is structured.
- [Git integration](18-git.md) — commit regularly so irreplaceable notes are versioned with the project.
