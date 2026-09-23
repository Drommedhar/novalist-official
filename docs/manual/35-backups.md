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
| Milestone | When you name a version and press **Keep this version** |

## Milestones

Rotating backups answer "what did this look like an hour ago". They cannot answer "what did the first draft look like", because by then it has been rotated out.

A **milestone** is an archive you name and Novalist never deletes. Type a name in **Settings → Backups → Milestone** and press **Keep this version**. Use it for the moments you will want to come back to: `First draft`, `Sent to agent`, `Before the rewrite`.

- Milestones are **exempt from retention** entirely. They are neither counted against the *Archives to keep* limit nor rotated out by it, so keeping ten of them does not push out a single ordinary backup.
- They are taken **even when automatic backups are switched off**. Asking to keep a version is deliberate, and a rotating schedule you disabled should not override it.
- They restore exactly like any other archive.
- Because retention will never clear one, the only way to remove a milestone is the delete button on its row.
- The name is stored in the archive's file name, so it is still readable if you copy the ZIP somewhere else. Punctuation is dropped and spaces become dashes; capitals are kept. Names longer than 60 characters are shortened.

Milestones are listed above their date with a flag, so they stand apart from the rotating archives around them.

## Settings

All of these live in **Settings → Backups**.

- **Back up the project automatically** — the master switch. Turning it off stops every automatic trigger; **Back up now** still works.
- **Minutes between backups** — default 30. Set it to `0` to back up only on open and close. Values below 5 are treated as 5, and the maximum is 1440 (one day).
- **Archives to keep** — default 5. Once more than this many exist, the oldest is deleted first. The minimum is 1, the maximum 100.
- **Backup folder** — leave empty for the default location above.

## Restoring

In **Settings → Backups**, each archive is listed with its date, what triggered it, and its size. Press **Restore** on the one you want and confirm.

Restoring replaces the project contents with the selected version, then reopens the project. Chapters, scenes, images and other files added after the backup are removed. The ZIP is extracted and checked before the project is changed.

**Restoring is undoable.** Novalist archives the current state first, tagged *Before restore*, even when automatic backups are disabled. If you pick the wrong version, you can restore that safety backup. It counts against the retention limit; retention is applied after the selected version has been restored.

Restore preserves `.git`, which is not included in backups. Your Git history survives intact. Keep your backup folder outside the project folder.

### Restore as a new project

Choose **Restore backup as new project** on the welcome screen without opening a project first. Select a Novalist backup ZIP (including one in a synced folder), enter a new project name, and choose its parent folder. **Restore and open** creates a separate project and opens it. An existing folder is never overwritten.

The same action is available in **Settings → Backups**, both above the list and beside each saved version. Using a version's action preselects that ZIP. The existing project remains unchanged, so you can switch between the original and the restored copy to compare them.

## Housekeeping

**Delete old archives** applies the retention limit immediately rather than waiting for the next backup. Use it after lowering the limit. Milestones are left alone.

The delete button on a row removes that one archive, milestone or not. There is no undo for it, so it asks first.

**Open backup folder** reveals the archives in your file manager. They are ordinary ZIP files — you can copy them elsewhere, open them without Novalist, or delete them by hand.

## Where to go next

- [Snapshots](17-snapshots.md) — per-scene history, for reverting one scene without touching the rest.
- [Git integration](18-git.md) — commit, push and pull from inside the app.
- [Settings](23-settings.md) — where the backup options live, and how global versus per-project scope works.
- [Troubleshooting & FAQ](28-troubleshooting.md) — where files live and how to recover them by hand.
