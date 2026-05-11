# Projects & Books

A **project** in Novalist is one folder on your disk. Inside that folder are one or more **books**, and inside each book are chapters, scenes, characters, locations, items, lore, images, and snapshots.

This page explains what each layer is, what is shared and what is per-book, and how everything is stored on disk.

## The model

```
Project
├── one or more Books
│   ├── Chapters (each with one or more Scenes)
│   ├── Entities (Characters, Locations, Items, Lore, Custom types)
│   ├── Templates (per entity type)
│   ├── Plotlines
│   ├── Acts
│   ├── In-world calendar
│   ├── Cover image
│   └── Images (the per-book image pool)
├── World Bible
│   └── Project-wide Entities (shared across books)
├── Custom entity-type definitions
├── Smart Lists
└── Story calendar config (shared default)
```

A **project** is the top container. It owns the recent-projects entry, the project name shown in the title bar, the optional Git repo, and the world bible.

A **book** is where the actual story lives. You can have a single book (most users) or many books (a series, related novellas, an anthology). Each book has its own chapters, scenes, entities, plotlines, acts, calendar, and templates.

The **World Bible** is a shared entity pool across books. Characters, locations, items, and lore stored there are visible from every book, so a series of novels can share the same cast without duplicating data. The book picker shows World Bible entries alongside book-scoped entries when relevant.

## Creating a project

From the [Welcome screen](01-getting-started.md#the-welcome-screen):

1. Click **Create new project**.
2. Fill in **Project name**, **First book name**, **Location**, and pick a **Template**.
3. Click **Create**.

The folder is created at `<Location>/<Project name>/` and opened immediately.

If you already have a project folder on disk (for example after copying from another machine, restoring from backup, or cloning from Git), use **Open project** instead and point the file picker at the project folder.

## Opening recent projects

Recent projects appear:

- On the Welcome screen as cover-image cards.
- In the **Start menu → Recent Projects** flyout (opened from the hamburger button at the far left of the app bar).

Right-click a recent project card to remove it from the list.

## The folder layout

This is the on-disk shape of a project. **Do not edit JSON files by hand while the app is running** — Novalist holds them in memory and will overwrite your changes on the next save. You can safely edit scene `.html` files in any editor when Novalist is closed.

```
<Project name>/
├── .novalist/
│   ├── project.json            # ProjectMetadata: book list, world-bible folder,
│   │                           # custom entity types, smart lists, settings
│   ├── settings.json           # ProjectSettings: per-project overrides
│   └── ...
├── Books/
│   └── <bookId>/
│       ├── book.json           # BookData: name, folders, templates, plotlines, acts, calendar
│       ├── chapters.json       # ordered chapter list
│       ├── scenes.json         # scene manifest (per chapter)
│       ├── Chapters/
│       │   └── <ChapterFolder>/
│       │       └── <Scene>.html
│       ├── Characters/
│       │   └── <character>.json
│       ├── Locations/
│       │   └── <location>.json
│       ├── Items/
│       │   └── <item>.json
│       ├── Lore/
│       │   └── <lore>.json
│       ├── Images/
│       │   └── ...
│       └── Snapshots/
│           └── <sceneId>/
│               └── <timestamp>.json
└── WorldBible/
    ├── Characters/
    ├── Locations/
    ├── Items/
    └── Lore/
```

The folder names inside a book (`Chapters`, `Characters`, etc.) are configurable per book — see `BookData` for details — but the defaults shown above are what you get from a fresh project.

## Multi-book support

Every project starts with one book (the **First book name** you chose at creation time). To work with more than one book:

1. Click the **book picker** in the app bar (the chevron next to the active book name).
2. The book picker overlay opens, showing one card per book with its cover image.
3. Click **Add book**, give it a name, and a new book is created with its own empty folder layout.
4. Right-click a book card for **Rename** or **Delete**.

The **active book** is the one all views (editor, codex, dashboard, plot grid, calendar, etc.) operate on. Switching books takes you back to the dashboard.

A typical use of multiple books:

- Volume 1, Volume 2, Volume 3 of a series — shared cast in the World Bible.
- The main book and a companion (short stories, prequels, lore primer).
- A draft and a heavily-revised remix you want to keep separate but in the same workspace.

## Renaming and deleting projects

- To **rename the project itself**, click the project totals button in the middle of the status bar to open the Project Overview, then click **Rename project**.
- To **delete a project**, close Novalist and delete the folder from your file manager. Novalist does not delete project folders for you.

## Renaming and deleting books

From the book picker overlay, right-click a book card:

- **Rename** — opens an input dialog.
- **Delete** — removes the book and its folder. This is destructive; consider taking a Git commit or filesystem copy first.

## Cover images

Each book has its own optional **cover image**. Set it from the book editor or from the Dashboard. Cover images appear:

- On the book card in the book picker.
- As the hero image on the Dashboard.
- On the project card in the Welcome screen (the cover of the first book is used).

See [Image Gallery](19-image-gallery.md) for how images are stored.

## Backups, sync, and version control

Because everything is a regular folder of regular files, your options for safekeeping are wide open:

- **Filesystem backups** — copy the folder, restore the folder.
- **Cloud sync** — Dropbox, OneDrive, iCloud Drive, etc. all work. Avoid editing the same scene on two machines at the same time.
- **Git** — Novalist has first-class Git support inside the app. See [Git](18-git.md). The recommended `.gitignore` for a Novalist project excludes only `.novalist/runtime/` and similar caches.

Novalist also takes per-scene snapshots automatically on save. See [Snapshots](17-snapshots.md) for the per-scene history mechanism — it is complementary to Git, not a replacement.

## Importing from other tools

The Welcome screen has an **Import from Obsidian Plugin** entry. This converts a project produced by the legacy Obsidian-based Novalist workflow into a native Novalist project. See [Troubleshooting](28-troubleshooting.md#import) for details.

## Where to go next

- [Chapters & Scenes](04-chapters-and-scenes.md) — start filling your book with content.
- [Codex](06-codex.md) — build the cast, world, and lore.
- [Settings](23-settings.md) — change the project's UI language, theme, accent, default templates.
