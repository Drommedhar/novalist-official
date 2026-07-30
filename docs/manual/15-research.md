# Research

The Research view is your project's scrapbook: quick notes, reference URLs, and pointers to files — anything that informs the writing but is not a scene or an entity.

Research is per-project. It is not tied to a specific book, scene, or character, and it travels with the project folder.

## Opening Research

Open it from the **World** group in the activity bar (**Research**), from the command palette, or with `Ctrl+9` (macOS uses Cmd).

## Layout

The view is a two-pane list-and-editor:

- **Left** — the action buttons, a search box, and the list of research items, each showing its title and type. Click an item to open it.
- **Right** — the editor for the selected item.

The **search box** filters the list by title, content, or tag (substring match).

## Quick capture and the Inbox

An idea usually arrives mid-sentence, and deciding where it belongs is exactly the thing you do not have time for. **Quick capture** removes that decision:

- Press `Ctrl+Shift+K` (`Cmd+Shift+K` on macOS) from anywhere in the app.
- Type the thought. `Ctrl+Enter` saves it, `Esc` cancels, and plain `Enter` just makes a new line — captures are often more than one.
- The note lands in Research as a **Note** carrying the reserved `inbox` tag. Its title is the first line; the body is everything you typed.

Unfiled captures collect in the **Inbox**. When at least one exists, an **Inbox** button appears above the research list showing how many are waiting; click it to show only those, and click again to show everything. Inbox items are also marked with a small **Inbox** badge in the list.

### The scratchpad, with no project open

Quick capture writes into the open project's Inbox, which is no help when the thought arrives before the right project is open — and that is exactly when thoughts arrive.

With no project open, quick capture goes to the **scratchpad** instead. It lives beside your settings rather than inside any project, survives every project being closed, and is shown on the welcome screen so you can add to it and read it without opening anything.

Once a project is open, the scratchpad appears at the bottom of the Research view whenever no item is selected. **File into project** moves a note into that project's Inbox, where it can be filed like any other capture.

### Filing an inbox note

Select an inbox note and a filing row appears above its title with three choices:

- **Create Codex entry** — makes a new [Codex](06-codex.md) entry named after the note's title (you choose the kind) and puts the note's body into its **Notes** section.
- **Add to existing entry** — appends the body to a section of an entry you pick, exactly like the editor's "Add selection to entity".
- **Keep as research note** — the note was already where it belongs; this just clears the inbox flag.

All three are **non-destructive**: filing copies the text and clears the `inbox` tag, leaving the research note itself intact. Delete it yourself if you no longer want the duplicate.

## Adding items

Three buttons sit at the top of the list:

- **Add Note** — creates an empty note.
- **Add Link** — creates a link item pre-filled with `https://`.
- **Import File** — opens the system file picker and copies the chosen file into the project. Novalist classifies it by extension: images become **Image** items, PDFs become **Pdf** items, and everything else becomes a **File** item.

You can also **drag and drop** onto the research list:

- Dropping **files** imports them exactly as **Import File** does — several at once is fine.
- Dropping **text** creates a note from it; if the text is a URL it becomes a **Link** instead.

For a **Link** item, the editor offers **Fetch title**: Novalist reads the page and renames the item to its title, so your list says "Rigging and Knots" instead of a bare address. It only reaches the network when you press the button, and if the lookup fails (you are offline, or the page has no title) the item is simply left as it was.

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

## Status, rating and links between items

Every item carries three things beyond its content, all optional and all set from the detail pane:

- **Status** — no status (the default), **open question**, **looking into it**, or **answered**. A shelf without this is a pile: nothing separates a question still open from one settled three months ago, and the note reading "check whether the bridge existed in 1755" looks the same after it has been checked as before.
- **Rating** — one to five stars. Forty sources have three that matter, and until now nothing said which. Clicking the star already set clears the rating, so a wrong click is one click to undo.
- **Related research** — links to other items, listed under **Related research** with a click to open each one.

Links are written **both ways**. A one-way link is discoverable only from the item that carries it, and the end worth finding is usually the other one: the question a source answers is what you are reading when you need the source. Deleting an item removes the links pointing at it, so nothing is left referring to something that is gone.

## Linking research to the Codex

Research is most useful at the moment you are writing about the thing it concerns — not when you happen to open the Research view. Each item therefore has a **Linked entries** field: pick any [Codex](06-codex.md) entry from the dropdown to link it, or click the `×` on a chip to unlink.

A linked item appears in a **Research** section on that entity's [Wiki](30-wiki.md) article, and clicking it there brings you straight back here with the item selected. So the article for a city can carry the three pages of notes you gathered about medieval ports.

## Tags

Tags are comma-separated labels on each item. Useful schemes:

- By topic: `medieval`, `sailing`, `chemistry`.
- By chapter: `ch1`, `ch4`.
- By status: `to-read`, `consulted`.

## Tips

- **Capture first, file later.** `Ctrl+Shift+K` costs four seconds and no decisions. Empty the Inbox when you next take a break, not mid-sentence.
- **Capture once, in detail.** It is much faster to write while reading research notes inside the app than to alt-tab to a browser.
- **Tag by chapter when relevant.** A `ch3` tag on every piece of research you needed for chapter 3 makes copy-edits trivial: skim, re-read, fix.
- **Don't over-organize.** A flat list with sensible titles plus a few tags beats a deep hierarchy.

## Media, in the app

Research items are typed, and the type decides what the view does with them:

| Type | What you get |
| --- | --- |
| **Note** | A Markdown note you write in place. |
| **Link** | A URL, with the page's title fetched when you add it. |
| **Image** | The picture, shown. |
| **Pdf** | The document, rendered and scrollable in the panel. |
| **Audio** | A player: an interview, a field recording, the music a scene is written to. |
| **Video** | A player: a clip, a reference performance, a location walk-through. |
| **File** | Anything else — metadata, plus **Open External** and **Reveal**. |

**Import a file** picks the type from the extension. `.mp3`, `.m4a`, `.wav`, `.ogg`, `.flac` and `.aac` become Audio; `.mp4`, `.webm`, `.m4v`, `.mov` and `.ogv` become Video; a format the app cannot play stays a File, so you never get playback controls that do nothing. You can change the type by hand afterwards.

Playing and reading happen inside the Research view on purpose. Opening a reference in another application is how a train of thought gets lost, which is what **Open External** is there for when you genuinely want it.

## Where to go next

- [Codex](06-codex.md) — for worldbuilding that is structured.
- [Git integration](18-git.md) — commit regularly so irreplaceable notes are versioned with the project.
