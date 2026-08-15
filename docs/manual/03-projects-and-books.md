# Projects & Books

A **project** in Novalist is one folder on your disk. Inside that folder are one or more **books**, each book has one or more **drafts**, and inside each draft are chapters and scenes. Books also hold characters, locations, items, lore, images, and snapshots.

This page explains what each layer is, what is shared and what is per-book, and how everything is stored on disk.

## The model

```
Project
├── one or more Books
│   ├── Drafts (each with Chapters, each chapter with Scenes)
│   ├── Entities (Characters, Locations, Items, Lore, Custom types)
│   ├── Templates (per entity type)
│   ├── Plotlines
│   ├── Acts
│   ├── In-world calendar
│   └── Images (the per-book image pool)
├── World Bible
│   └── Project-wide Entities (shared across books)
├── Custom entity-type definitions
├── Smart Lists
└── Story calendar config (shared default)
```

A **project** is the top container. It owns the recent-projects entry, the project name shown in the toolbar, the optional Git repo, and the World Bible.

A **book** is where the actual story lives. You can have a single book (most users) or many books (a series, related novellas, an anthology). Each book has its own drafts, entities, plotlines, acts, calendar, and templates.

A **draft** is one version of a book's manuscript. Most books have a single draft; create more when you want to keep a rewrite separate from the original without losing it.

The **World Bible** is a shared entity pool across books. Characters, locations, items, and lore stored there are visible from every book, so a series of novels can share the same cast without duplicating data. World Bible entries are marked with a **WB** badge in the [Codex](06-codex.md).

## Opening a project

From the [start screen](01-getting-started.md#the-start-screen):

- Click a project in **Recent Projects** — each recent project shows its portrait **book cover** (set on the [Dashboard](11-dashboard.md#banner-and-book-cover)), or a placeholder when none is set, or
- Click **Browse for Project Folder...** and point the folder picker at the project folder (any folder containing a `.novalist/` subdirectory).

This also covers projects copied from another machine, restored from backup, or cloned from Git — there is no separate "import" step for native projects. For projects from the legacy Obsidian plugin, use **Import from Obsidian Plugin...** instead.

## Starting from a premise

The new-project dialog has a **Start from a premise (Snowflake method)** tick box. With it on, once the project exists Novalist asks for the book in one sentence, then in one paragraph, then act by act - and offers to create a run of placeholder chapters under each act so you have a shape to write into.

Everything it asks is optional and everything it writes is editable afterwards. The answers become the book's [Premise](11-dashboard.md#premise) on the Dashboard, not prose in a scene, so they never end up in an export.

Leave the box off and the project is created exactly as before.

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
│   ├── Expose.novalist         # the book's exposé; HTML inside
│   ├── Drafts/
│   │   └── <DraftFolder>/      # one folder per draft
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

Scene files use the `.novalist` extension and contain HTML inside — you can open them in any text editor. The folder names inside a book (`Characters`, `Locations`, etc.) are configurable per book, but the defaults shown above are what you get from a fresh project.

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
| Edit a scene's title, date, notes, or synopsis | Per-scene metadata lives in `scenes.json`. There is no per-scene marker file. Rename scenes from the binder; edit synopsis and notes in the inspector. |
| Edit a chapter's title, act, status, or date | Chapter metadata lives in both `.nvchapter.json` and `draft.json`. The reconciler treats the cached `draft.json` value as authoritative for these fields — editing the marker in a text editor does **not** propagate. Use the chapter's right-click menu in the binder. |
| Reorder chapters by changing the `NN -` folder prefix | The number prefix is cosmetic — Novalist never renumbers behind your back, and equally never reads the prefix as a reorder signal. Drag chapters in the binder to reorder. |
| Reorder scenes by renaming `scene-NN.novalist` files | Same reason — the `NN` is cosmetic. Drag scenes in the binder to reorder. |
| Add or rename an act | Acts live in `acts.json` and on chapter records. Use **Right-click chapter → Rename Act** in the binder. |
| Edit project / book / draft names or folder layout fields | These live in `project.json` (root `.novalist/project.json`). Renaming the project folder itself is supported by re-opening the moved project; renaming or restructuring inside the project tree is not auto-migrated. |
| Add or rename codex entities by editing JSON in `Characters/`, `Locations/`, etc. | Codex entities are not part of the manuscript reconciler. The app reads them on open but doesn't reconcile schema-level edits. |

If you want a chapter's act to follow a chapter move, change the act inside Novalist after moving the folder — the act value carried along with the moved chapter is whatever was last set in `draft.json`, not what its new neighbours have. The binder groups chapters by act in first-appearance order, so a chapter whose act doesn't match its neighbours will appear under its own act's header in the tree rather than between those neighbours; reassign the act in Novalist if you want it back at its visual position.

Two ways reconciliation runs:

- **On load** — when you open a project, Novalist scans the active draft and applies any external changes made while it was closed.
- **On save** — when you save a scene whose file changed on disk since Novalist read it, the save is refused rather than overwriting the other version, and you are shown both. See [When a scene changed somewhere else](#when-a-scene-changed-somewhere-else) below.

## When a scene changed somewhere else

Novalist keeps your project in a plain folder, which means people put it in Dropbox, iCloud Drive, OneDrive or Syncthing and write on more than one machine. Two machines editing the same scene is not exotic — it is what happens when a sync arrives while you had the scene open, or when you close the laptop before it finishes uploading.

When you save a scene whose file changed since Novalist read it, **the save is refused**. Nothing is written and the other version stays intact. A dialog shows the two versions side by side:

- The left column is what you wrote; the right is what is in the file.
- Lines both versions agree on are shown greyed out for context and cannot be picked.
- Every line they differ on is a pair of buttons; click the side you want. Your own text is preselected, because you were the one typing.
- **Take all of mine** and **Take all from disk** set every differing line at once, for when one version is simply the right one.
- **Decide later** closes the dialog and changes nothing. Your text stays in the editor, still unsaved, and the file is untouched.

Novalist deliberately does **not** merge prose automatically. A sentence spliced from two drafts reads like neither, and a writer would rather choose than discover the splice three chapters later.

Saving your resolution takes a [snapshot](17-snapshots.md) of both versions first, so a wrong click here is recoverable — either original can be restored from the scene's snapshot list.

Migration to this model happens automatically the first time you open an older project: scene files are stamped, markers and the index are written. It adds about 30 bytes per scene and one small file per chapter folder — no content is rewritten.

## Working with books

Every project has at least one book. To work with more than one:

1. Open the **book selector** in the toolbar (the dropdown next to the project name).
2. Pick a book to switch to it, or pick **+ Add Book** and give the new book a name. It is created with its own empty folder layout.

The **active book** is the one all views (editor, codex, dashboard, plot grid, calendar, etc.) operate on.

A typical use of multiple books:

- Volume 1, Volume 2, Volume 3 of a series — shared cast in the World Bible.
- The main book and a companion (short stories, prequels, lore primer).
- A draft and a heavily-revised remix you want to keep separate but in the same workspace.

## Working with drafts

Each book has its own drafts, shown in the **draft selector** in the toolbar (next to the book selector):

- Pick a draft to switch to it. The binder, editor, and all manuscript views follow the active draft.
- Pick **+ New draft (clone from current)** to create a new draft as a full copy of the current one — useful before a structural rewrite: the old draft stays intact and you keep working in the clone.
- Use the **delete-draft** button next to the draft selector to remove the active draft (you are asked to confirm). A book always keeps at least one draft.

Entities, images, plotlines, and templates are per-book, not per-draft — all drafts of a book share them.

### Comparing two drafts

Cloning a draft before a rewrite is easy. Seeing what the rewrite actually changed is the other half, and that is what the **compare-drafts** button next to the draft selector is for. It needs at least two drafts.

Pick a draft on each side. Novalist opens on the pair you most likely want: the draft you are in, against the one it was cloned from.

The left column lists every scene in both drafts, marked as one of:

| Marking | Means |
| --- | --- |
| Unchanged | In both drafts, word for word. Formatting differences do not count as changes. |
| Changed | In both drafts, with different prose. The two word counts are shown. |
| Only in the later draft | Written after the drafts parted. |
| Only in the earlier draft | Cut on the way to the later draft. |

Scenes are matched by identity, not by title, so a scene you renamed during the rewrite is still recognised as the same scene rather than showing up as one added and one deleted.

Pick a scene to see a line-by-line diff of it, laid out exactly like the [snapshot](17-snapshots.md) comparison.

### Taking a scene back across

When the right-hand side is the draft you are actually in, a changed scene offers **Take this scene from …**. That replaces the scene's prose in your draft with the version from the other one — the per-scene cherry-pick you would otherwise do by copy and paste.

- A [snapshot](17-snapshots.md) of the scene is taken first, labelled with the draft it came from, so taking the wrong version is undoable from the scene's own history.
- If the scene does not exist in your draft but its chapter does, it is created there.
- If the chapter is gone too, nothing happens. Novalist will not recreate structure you deleted while you were asking about one scene.
- Only prose moves. Synopsis, notes, status and metadata stay as they are in your draft.

Comparing is read-only in both directions until you press that button.

## Renaming and deleting projects

- To **rename the project**, double-click the project name in the toolbar and enter the new name.
- To **delete a project**, close Novalist and delete the folder from your file manager. Novalist does not delete project folders for you.

## Backups, sync, and version control

Because everything is a regular folder of regular files, your options for safekeeping are wide open:

- **Filesystem backups** — copy the folder, restore the folder.
- **Cloud sync** — Dropbox, OneDrive, iCloud Drive, etc. all work. Avoid editing the same scene on two machines at the same time.
- **Git** — Novalist has first-class Git support inside the app. See [Git](18-git.md).

Novalist also supports per-scene snapshots — a manual version history per scene plus auto-snapshots taken before destructive operations such as Replace All. See [Snapshots](17-snapshots.md) — it is complementary to Git, not a replacement.

## Importing from other tools

The start screen has an **Import from Obsidian Plugin...** entry. This converts a project produced by the legacy Obsidian-based Novalist workflow into a native Novalist project. See [Troubleshooting](28-troubleshooting.md#importing-from-obsidian) for details.

## Where to go next

- [Chapters & Scenes](04-chapters-and-scenes.md) — start filling your book with content.
- [Codex](06-codex.md) — build the cast, world, and lore.
- [Settings](23-settings.md) — change the UI language, theme, accent, templates.
