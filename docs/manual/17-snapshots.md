# Snapshots

A **snapshot** is a saved copy of a single scene at a point in time. Snapshots are created manually from the toolbar Snapshots button — and automatically by destructive operations such as Replace All and snapshot restores — so you can revert a scene to any previous state without affecting the rest of the project.

Snapshots are independent per scene. Reverting one scene does not touch any other scene.

## Why snapshots and not just Git?

Snapshots and Git complement each other:

- **Snapshots** are per-scene and never require a commit message. They are the safety net for individual scenes — take one manually before a risky rewrite, or rely on the auto-snapshots taken before destructive operations.
- **Git** captures the whole project at once with an authored commit message and a branch concept. Use Git for project-level versioning, sharing with co-authors, and external backup.

You can (and should) use both.

## How snapshots work

Snapshots are not taken on every save. They are taken **manually** from the toolbar Snapshots button and **automatically** by operations that would otherwise lose content:

- **Replace All** in Find & Replace — every scene that is about to be modified gets an auto-snapshot first.
- **Restoring a snapshot** — the scene's current state is auto-snapshotted before the restore, so a restore is always reversible.

Each snapshot stores the full scene content, the word count, and a timestamp. Snapshots are stored inside the book folder:

```
<Project>/Books/<bookId>/Snapshots/<sceneId>/<timestamp>-<id>.json
```

## Taking a manual snapshot

1. Open the scene in the editor.
2. Click the **Snapshots** button in the toolbar to open the snapshots dialog for the open scene.
3. Click **Take snapshot**.
4. Optionally enter a **label** (e.g. "Before rewrite") — labels make snapshots much easier to find later. Auto-snapshots get a generated label.

## Browsing and restoring

The snapshots dialog lists the open scene's snapshots, newest first. Each row shows the label (or the date, if unlabeled) and the word count at that point.

Click **Restore** on a row to replace the current scene content with that snapshot. The current state is auto-snapshotted first, so you can undo the restore by restoring the newly created snapshot.

## Pruning and disk usage

Snapshots are small JSON files (typically a few KB each) but accumulate on long projects, especially from repeated Replace All operations. Use the pruning buttons in the dialog's **Whole project** scope to clear them out; there is no automatic pruning, because deciding a version is not worth keeping is the writer's call.

If your project is in Git, snapshots are checked in by default — consider `.gitignore`-ing the snapshot folder if you'd rather rely on Git history instead. Both strategies are valid.

## When a scene is deleted

When you delete a scene, its snapshot folder remains on disk, so the content can still be recovered: open the latest snapshot's JSON file and copy its content into a new scene. Once you no longer need it, pruning from the **Whole project** scope removes those orphaned folders.

## The whole project's snapshots

The dialog has two scopes: **This scene** and **Whole project**. The project view lists every snapshot in the book, newest first, with the chapter and scene it belongs to. From there you can **rename** one — a snapshot called "sent to the agent" is findable a year later in a way that a date is not — or delete it.

Two pruning buttons clear out what has piled up:

- **Keep the newest five per scene** — deletes everything past the five most recent snapshots of each scene.
- **Delete older than 90 days**.

Both also remove snapshot folders left behind by scenes you deleted. Nothing can reach those any more, so they accumulate silently; this is the only thing that clears them without closing the project and deleting files by hand.

Pruning cannot be undone. It reports how many snapshots went.

## What a snapshot restores

A snapshot carries the scene, not only its words. Restoring puts back the prose **and** the synopsis, notes, point of view, stage, label, story date, plotline membership and tags as they stood when it was taken.

Snapshots taken before this shipped hold only the prose. Restoring one of those leaves the scene's other fields exactly as they are, rather than blanking a synopsis written since.

## Where to go next

- [Editor](05-editor.md) — open a scene, then take and restore its snapshots from the toolbar Snapshots dialog.
- [Find & Replace](21-find-replace.md) — Replace All auto-snapshots every scene it changes.
- [Backups](35-backups.md) — automatic whole-project archives, for when the problem is bigger than one scene.
- [Git integration](18-git.md) — project-level version control complementing snapshots.
