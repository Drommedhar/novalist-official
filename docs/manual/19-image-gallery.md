# Image Gallery

The Image Gallery is a unified view of every image in your project. Reference photos, character portraits, location concept art, map files, mood boards — all in one place, regardless of which entity (if any) they're attached to.

## Opening the Gallery

Click the **image** icon in the activity bar, or use **View → Gallery** / its hotkey.

## What it shows

The gallery scans the project's image folders:

- The active book's `Images/` folder.
- The World Bible's images.
- Any other folder configured as image-source in the project settings.

Each image appears as a card with:

- **Thumbnail** — generated lazily off the UI thread.
- **Filename**.
- **Relative path** in the project.

A toolbar lets you toggle between **Grid** (cards) and **List** (table with filename and path).

## Filtering

A **search box** filters by filename (substring match). Useful when you remember "the dragon image" but not which entity you attached it to.

## Per-image actions

Right-click an image card (or click the action menu) for:

- **Open externally** — opens in the system default image viewer.
- **Reveal in file manager** — shows the file on disk (Windows Explorer, Finder, GNOME Files).
- **Copy path** — copies the file path to the clipboard.
- **Copy as markdown** — copies a markdown image-link string ready to paste into a research note.

## Adding images

The gallery itself is read-only — it surfaces images that already exist in the project. You add images by:

- Attaching them to an entity from the entity editor (most common).
- Importing them into Research from the Research view.
- Dropping them into the book's `Images/` folder via the file manager (they show up after a refresh).

When you add an image via the entity editor or Research, you choose the **source** in the Add Image Source dialog:

- **From file** — system file picker.
- **From clipboard** — paste copy buffer.
- **From the project gallery** — pick an image already in the project (via the Project Image Picker).
- **From URL** — paste a URL, Novalist downloads.

## Lazy loading

Thumbnails are decoded in the background. Large galleries will show placeholders briefly and then fill in. The full-resolution image is only loaded when you click into a preview.

## Cover images

A book's cover image is a regular project image. Pick one from the gallery via the Project Image Picker — see the book editor or Dashboard.

## Where to go next

- [Codex](06-codex.md) — entity image galleries.
- [Research](15-research.md) — image research items.
