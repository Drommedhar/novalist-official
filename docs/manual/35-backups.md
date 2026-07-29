# Backups

Novalist archives your whole project folder to a ZIP file on a schedule, and can restore one from inside the app. This is separate from [Snapshots](17-snapshots.md), which cover a single scene, and from [Git](18-git.md), which you drive by hand.

Backups are on by default. You do not need to set anything up.

## What gets archived

Everything in the project folder: `.novalist/`, your books and drafts, the World Bible, images, research, maps, and the snapshot folder.

One exception: the `.git` folder is skipped. It is already version control, so archiving it would double the size of every backup without giving you any recovery option you did not already have.

## Where backups are written

By default, into your application data folder, in a directory named after the project:

- **Windows:** `%APPDATA%\Novalist\Backups\<Project>\`
- **macOS:** `~/Library/Application Support/Novalist/Backups/<Project>/`
- **Linux:** `~/.config/Novalist/Backups/<Project>/`

Deliberately **outside** the project folder. A backup kept inside the project is destroyed by exactly the accident it exists to survive — a deleted folder, a bad sync, a drive failure — and it would also be picked up by Git and by file-sync tools.

You can point backups somewhere else in **Settings → Backups → Backup folder**. Choosing a synced folder or an external drive means your work survives losing the machine itself, not just losing the project.

## When a backup is taken

| Trigger | When |
| --- | --- |
| On open | Each time you open the project |
| Automatic | Every N minutes while the project stays open |
| On close | When you quit with the project open |
| Manual | When you press **Back up now** |
| Before restore | Automatically, immediately before restoring another backup |

## Settings

All of these live in **Settings → Backups**.

- **Back up the project automatically** — the master switch. Turning it off stops every automatic trigger; **Back up now** still works.
- **Minutes between backups** — default 30. Set it to `0` to back up only on open and close. Values below 5 are treated as 5, and the maximum is 1440 (one day).
- **Archives to keep** — default 5. Once more than this many exist, the oldest is deleted first. The minimum is 1, the maximum 100.
- **Backup folder** — leave empty for the default location above.

## Restoring

In **Settings → Backups**, each archive is listed with its date, what triggered it, and its size. Press **Restore** on the one you want and confirm.

Restoring overwrites the project folder with the contents of the archive, then reopens the project so what you see matches what is on disk.

**Restoring is undoable.** Novalist archives the current state first, tagged *Before restore*, so if you pick the wrong one you can restore your way back out. That pre-restore archive counts against the retention limit like any other.

Two things restoring does not do:

- It does not delete files that were added after the backup was taken. The archive is unpacked over the folder, so a scene created since the backup will still be there afterwards. If you need an exact match, move the project folder aside and restore into an empty one.
- It does not touch the `.git` folder, since that was never archived. Your Git history survives a restore intact.

## Housekeeping

**Delete old archives** applies the retention limit immediately rather than waiting for the next backup. Use it after lowering the limit.

**Open backup folder** reveals the archives in your file manager. They are ordinary ZIP files — you can copy them elsewhere, open them without Novalist, or delete them by hand.

## Where to go next

- [Snapshots](17-snapshots.md) — per-scene history, for reverting one scene without touching the rest.
- [Git integration](18-git.md) — commit, push and pull from inside the app.
- [Settings](23-settings.md) — where the backup options live, and how global versus per-project scope works.
- [Troubleshooting & FAQ](28-troubleshooting.md) — where files live and how to recover them by hand.
