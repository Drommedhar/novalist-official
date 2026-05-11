# Snapshots

A **snapshot** is a saved copy of a single scene at a point in time. Snapshots are taken automatically on save and can also be taken manually. They let you revert a scene to any previous state without affecting the rest of the project.

Snapshots are independent per scene. Reverting one scene does not touch any other scene.

## Why snapshots and not just Git?

Snapshots and Git complement each other:

- **Snapshots** are automatic, per-scene, and never require a commit message. They are the safety net for everyday writing — undo your way back through your last twenty saves of one scene without rebuilding the rest of the project.
- **Git** captures the whole project at once with an authored commit message and a branch concept. Use Git for project-level versioning, sharing with co-authors, and external backup.

You can (and should) use both.

## How automatic snapshots work

Every time a scene saves — auto-save or manual `Ctrl+S` — a snapshot of its content is captured. The snapshot stores the full HTML content, the word count, and a timestamp. Snapshots are stored under:

```
<Project>/Books/<bookId>/<SnapshotFolder>/<sceneId>/<timestamp>.json
```

where `<SnapshotFolder>` defaults to `Snapshots`.

## Taking a manual snapshot

Use the menu **Edit → Take Snapshot**, or its hotkey, or right-click a scene in the Explorer → **Take Snapshot**.

A manual snapshot can be given a **label** (e.g. "before rewrite", "v2 polish"), which makes it easier to find in the list later. Auto-snapshots have no label.

## Browsing snapshots

Open the snapshots browser:

- **Edit → Snapshots**.
- The **Snapshots** button in the app bar.
- Right-click a scene in the Explorer → **Snapshots**.

The Snapshots dialog shows:

- The current scene's name at the top.
- A list of snapshots, newest first. Each row shows:
  - **Timestamp**.
  - **Label** (if any).
  - **Word count** at that point.
- A **preview pane** showing the snapshot's content.

Actions:

- **Restore** — replaces the current scene content with this snapshot's content. A new snapshot is taken first so you can undo the restore.
- **Delete** — removes the snapshot from disk.
- **Compare** — opens the Snapshot Compare dialog (see below).

## Comparing snapshots

Click **Compare** to open a side-by-side diff between two snapshots, or between a snapshot and the current scene. The compare dialog shows:

- Two columns of text.
- Differences highlighted: green for added content, red for removed.
- Word-count delta.

This is the right tool for "what did I change in the last hour?" and "what was the original version like?" questions.

## Pruning

Snapshots accumulate quickly. You can delete individual snapshots from the dialog. There is no built-in auto-prune as of this writing — manual cleanup is on you. A reasonable policy:

- Keep all snapshots from the last 7 days.
- Keep labeled snapshots indefinitely.
- Delete unlabeled snapshots older than 30 days when the scene reaches Final status.

## Disk usage

Snapshots are JSON files containing the scene's HTML. They are small individually (typically a few KB) but can accumulate to many MB on long projects. If your project is in Git, snapshots are checked in by default — consider `.gitignoring` the snapshot folder if you'd rather rely on Git history instead. Both strategies are valid.

## When a scene is deleted

When you delete a scene, its snapshot folder remains. To recover, restore the snapshot folder and re-create the scene with the right filename, then point it at the recovered content — or copy the latest snapshot's content into a new scene.

## Where to go next

- [Editor](05-editor.md) — auto-save takes snapshots.
- [Git integration](18-git.md) — project-level version control complementing snapshots.
- [Troubleshooting](28-troubleshooting.md) — recovery scenarios.
