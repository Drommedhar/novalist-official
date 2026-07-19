# Snapshots

A **snapshot** is a saved copy of a single scene at a point in time. Snapshots are created manually from the Inspector — and automatically by destructive operations such as Replace All and snapshot restores — so you can revert a scene to any previous state without affecting the rest of the project.

Snapshots are independent per scene. Reverting one scene does not touch any other scene.

## Why snapshots and not just Git?

Snapshots and Git complement each other:

- **Snapshots** are per-scene and never require a commit message. They are the safety net for individual scenes — take one manually before a risky rewrite, or rely on the auto-snapshots taken before destructive operations.
- **Git** captures the whole project at once with an authored commit message and a branch concept. Use Git for project-level versioning, sharing with co-authors, and external backup.

You can (and should) use both.

## How snapshots work

Snapshots are not taken on every save. They are taken **manually** from the Inspector and **automatically** by operations that would otherwise lose content:

- **Replace All** in Find & Replace — every scene that is about to be modified gets an auto-snapshot first.
- **Restoring a snapshot** — the scene's current state is auto-snapshotted before the restore, so a restore is always reversible.

Each snapshot stores the full scene content, the word count, and a timestamp. Snapshots are stored inside the book folder:

```
<Project>/Books/<bookId>/Snapshots/<sceneId>/<timestamp>-<id>.json
```

## Taking a manual snapshot

1. Open the scene in the editor.
2. Open the **Inspector** (the right-hand pane) with the toolbar toggle at the far right, or `Ctrl+Shift+B` (`Cmd` on macOS).
3. Under **Scene snapshots**, click **Take snapshot**.
4. Optionally enter a **label** (e.g. "Before rewrite") — labels make snapshots much easier to find later. Auto-snapshots get a generated label.

## Browsing and restoring

The **Scene snapshots** section of the Inspector lists the open scene's snapshots, newest first. Each row shows the label (or the date, if unlabeled) and the word count at that point.

Click **Restore** on a row to replace the current scene content with that snapshot. The current state is auto-snapshotted first, so you can undo the restore by restoring the newly created snapshot.

## Pruning and disk usage

Snapshots are small JSON files (typically a few KB each) but accumulate on long projects, especially from repeated Replace All operations. There is no built-in auto-prune; to clean up, delete old snapshot files from the `Snapshots` folder on disk while the project is closed.

If your project is in Git, snapshots are checked in by default — consider `.gitignore`-ing the snapshot folder if you'd rather rely on Git history instead. Both strategies are valid.

## When a scene is deleted

When you delete a scene, its snapshot folder remains on disk. To recover the content, open the latest snapshot's JSON file and copy its content into a new scene.

## Where to go next

- [Inspector](22-context-sidebar.md) — where snapshots are taken and restored.
- [Find & Replace](21-find-replace.md) — Replace All auto-snapshots every scene it changes.
- [Git integration](18-git.md) — project-level version control complementing snapshots.
