# Research

The Research view is your project's scrapbook. Notes, URLs, PDFs, reference images, downloaded articles, transcripts of interviews — anything that informs the writing but isn't a scene or an entity.

Research is per-project; it isn't tied to a specific book, scene, or character (though you can tag items so they're easy to filter).

## Opening Research

Use **View → Research** or its hotkey. Research lives in its own content area and you can have it open as a tab alongside scenes.

## Item types

A research item is one of:

- **Note** — markdown body. For quick text notes, transcribed paragraphs, research summaries.
- **Link** — a URL. Click to open in the system browser.
- **File** — any binary file. Stored alongside the project.
- **Image** — auto-detected from the file extension. Renders inline in the preview.
- **PDF** — auto-detected from `.pdf`. Renders in the embedded PDF viewer.

Each item has:

- **Title**
- **Type**
- **Content** — for notes, the markdown body; for links, the URL; for files/images/PDFs, a relative path inside the research folder.
- **Tags** — comma-separated. Used for filtering.
- **Created at** / **Updated at** — auto-timestamped.

## Adding items

Toolbar buttons:

- **Add note** — opens a markdown editor for an inline note.
- **Add link** — input dialog for URL and title.
- **Import file** — file picker; Novalist auto-detects the type (image, PDF, or generic file) by extension and copies the file into the project's research folder.

## Browsing and filtering

- Items appear as a **list on the left** of the Research view, sorted by recency.
- Click an item to see it in the **detail panel on the right**.
- A **search box** filters by title.
- A **tag filter** narrows to items with a specific tag.

## The detail panel

- **Notes** — rendered markdown with edit-in-place. Inline images supported via standard markdown syntax pointing at images stored in the project.
- **Links** — title, URL, **Open** button.
- **Images** — full-size preview with zoom.
- **PDFs** — embedded reader with paging and zoom.
- **Files** — metadata, size, type, with **Open externally** and **Reveal in file manager** actions.

## Tags

Add tags by editing the item. Tags are case-sensitive labels separated by commas. Useful schemes:

- By topic: `medieval`, `sailing`, `chemistry`.
- By chapter: `ch1`, `ch4`.
- By status: `to-read`, `consulted`.

The tag filter shows every distinct tag in the project.

## Where files live

Imported files are copied into `<Project>/Research/` so the project is self-contained. Notes are stored as JSON entries in the research manifest.

## Tips

- **Capture once, in detail.** It is much faster to write while reading research notes inside the app than to alt-tab to a browser tab.
- **Tag by chapter when relevant.** A `ch3` tag on every piece of research you needed for chapter 3 makes copy-edits trivial: filter, re-read, fix.
- **Don't over-organize.** A flat list with sensible titles plus a few tags beats a deep folder hierarchy.

## Where to go next

- [Codex](06-codex.md) — for worldbuilding that is structured.
- [Snapshots](17-snapshots.md) — Research items are not snapshotted; for irreplaceable notes, also keep a Git commit.
