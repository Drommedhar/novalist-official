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

This is the on-disk shape of a project. **Do not edit the `.json` cache files by hand while the app is running** — Novalist holds them in memory and will overwrite your changes on the next save. Scene and chapter *structure*, however, is safe to rearrange with a file manager — see [Editing your project outside Novalist](#editing-your-project-outside-novalist) below.

```
<Project name>/
├── .novalist/
│   ├── project.json            # ProjectMetadata: book list, world-bible folder,
│   │                           # custom entity types, smart lists, settings
│   ├── settings.json           # ProjectSettings: per-project overrides
│   └── ...
├── <BookFolder>/               # one folder per book, named after the book
│   ├── .book/                  # per-book metadata (book.json, acts, etc.)
│   ├── Drafts/
│   │   └── <DraftFolder>/      # active draft
│   │       └── <ChapterFolder>/
│   │           └── <Scene>.novalist   # one file per scene; plain text inside
│   ├── Characters/
│   │   └── <character>.json
│   ├── Locations/
│   │   └── <location>.json
│   ├── Items/
│   │   └── <item>.json
│   ├── Lore/
│   │   └── <lore>.json
│   ├── Images/
│   │   └── ...
│   └── Snapshots/
│       └── <sceneId>/
│           └── <timestamp>.json
└── WorldBible/
    ├── Characters/
    ├── Locations/
    ├── Items/
    └── Lore/
```

Scene files use the `.novalist` extension and contain HTML inside — you can open them in any text editor. The folder names inside a book (`Characters`, `Locations`, etc.) are configurable per book — see `BookData` for details — but the defaults shown above are what you get from a fresh project.

## Editing your project outside Novalist

The filesystem is the source of truth for your manuscript structure. You can add, move, rename, and delete scenes and chapters with any file manager and Novalist will reconcile the changes — no JSON editing required.

How identity is kept so nothing gets lost when you rearrange files:

- **Scene files** carry a one-line HTML comment at the very top, e.g. `<!--nv v=1 id=… -->`. This is the scene's durable id. It lets Novalist recognise a scene after you move it to another chapter folder or rename the file. The comment is stripped before editing, word count, and export — you never see it in the editor, and it does not affect your text.
- **Chapter folders** contain a hidden `.nvchapter.json` marker that pins the chapter's durable identity (its `guid`). Novalist also stamps the chapter's current metadata (title, act, order, status, date) into the marker on every save so the file is readable, but identity is the only field the reconciler actually re-reads — see "What Novalist does not detect" below. You can rename the chapter folder freely (the `NN -` number prefix is only a display hint and is never renumbered behind your back).
- **`.nvindex.json`** in each draft folder is a rebuildable fingerprint cache used to detect moves. It is safe to delete; Novalist rebuilds it.
- **`acts.json`** holds act metadata, split out of `draft.json`.

What Novalist detects and reconciles:

| You do this (in a file manager) | Novalist on next load / live |
|---|---|
| Add a `.novalist` file to a chapter folder | New scene, stamped with a fresh id, appended |
| Move a scene file to another chapter folder | Recognised as the same scene, moved (by id, or by content if it had no id yet) |
| Rename a scene file | Same scene, new file name |
| Rename a chapter folder | Same chapter — identity preserved by the marker |
| Add a new chapter folder | New chapter (appended at the end of the book; reorder it inside Novalist) |
| Delete a scene file or chapter folder | Removed from the manuscript |
| Edit the body of a `.novalist` file | Picked up the next time the scene is opened (or live, if it isn't open in the editor) |

What Novalist does **not** detect — for these you must use the app:

| You do this | Why it doesn't reconcile |
|---|---|
| Edit a scene's title, date, date range, notes, synopsis, label colour, favourite, or POV/conflict/emotion overrides | Per-scene metadata lives in `scenes.json`. There is no per-scene marker file. Edit it from the [Scene dialog](04-chapters-and-scenes.md) or the Context sidebar, or edit `scenes.json` directly (treat as advanced — Novalist does not validate the schema). |
| Edit a chapter's title, act, status, date, date range, or favourite | Chapter metadata lives in both `.nvchapter.json` and `draft.json`. The reconciler currently treats the cached `draft.json` value as authoritative for these fields — editing the marker in a text editor does **not** propagate. Use the chapter's right-click menu in the Explorer. |
| Reorder chapters by changing the `NN -` folder prefix | The number prefix is cosmetic — Novalist never renumbers behind your back, and equally never reads the prefix as a reorder signal. Drag chapters in the Explorer to reorder. |
| Reorder scenes by renaming `scene-NN.novalist` files | Same reason — the `NN` is cosmetic. Drag scenes in the Explorer to reorder. |
| Add a new act, rename an act, change an act's date range | Acts live in `acts.json` and on chapter records. Use **Right-click chapter → Set act…** or the Plot Grid. |
| Edit project / book / draft names or folder layout fields | These live in `project.json` (root `.novalist/project.json`). Renaming the project folder itself is supported by re-opening the moved project; renaming or restructuring inside the project tree is not auto-migrated. |
| Add or rename codex entities by editing JSON in `Characters/`, `Locations/`, etc. | Codex entities are not part of the manuscript reconciler. The app reads them on open but doesn't reconcile schema-level edits. |

If you want a chapter's act to follow a chapter move, change the act inside Novalist after moving the folder — the act value carried along with the moved chapter is whatever was last set in `draft.json`, not what its new neighbours have. The Explorer groups chapters by act in first-appearance order, so a chapter whose act doesn't match its neighbours will appear under its own act's header in the tree rather than between those neighbours; reassign the act in Novalist if you want it back at its visual position.

Two ways reconciliation runs:

- **On load** — when you open a project, Novalist scans the active draft and applies any external changes made while it was closed.
- **Live** — while the app is open, Novalist watches the active draft folder and reconciles changes shortly after they happen, so moving a file in Explorer updates the manuscript without a restart. If a file you have open in the editor changes on disk, Novalist asks before discarding unsaved edits. Live watching can be turned off in [Settings](23-settings.md) (for example on flaky network drives); load-time reconciliation still runs.

Migration to this model happens automatically the first time you open an older project: scene files are stamped, markers and the index are written. It adds about 30 bytes per scene and one small file per chapter folder — no content is rewritten.

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

Novalist also supports per-scene snapshots — a manual version history per scene plus auto-snapshots taken before destructive operations such as Find & Replace. See [Snapshots](17-snapshots.md) for the per-scene history mechanism — it is complementary to Git, not a replacement.

## Importing from other tools

The Welcome screen has an **Import from Obsidian Plugin** entry. This converts a project produced by the legacy Obsidian-based Novalist workflow into a native Novalist project. See [Troubleshooting](28-troubleshooting.md#import) for details.

## Where to go next

- [Chapters & Scenes](04-chapters-and-scenes.md) — start filling your book with content.
- [Codex](06-codex.md) — build the cast, world, and lore.
- [Settings](23-settings.md) — change the project's UI language, theme, accent, default templates.
