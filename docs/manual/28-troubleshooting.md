# Troubleshooting & FAQ

This page collects the questions that come up most often, the places where things can go wrong, and the recovery procedures for the worst cases.

## Where do my files live?

Two locations matter:

- **Your project folder** — wherever you created it. All of your manuscript, entities, snapshots, research, and images live here.
- **The app data folder** — settings, recent-project list, hotkey overrides, installed extensions.
  - Windows: `%APPDATA%/Novalist/`
  - macOS: `~/Library/Application Support/Novalist/`
  - Linux: `~/.config/Novalist/`

Backing up just the project folder is enough to safeguard your writing. Backing up the app data folder is only useful if you want to preserve your hotkey overrides and installed-extension state across machines.

## I deleted a scene by accident. Can I get it back?

Yes. Look in `<Project>/Books/<bookId>/Snapshots/<sceneId>/`. The folder remains even after the scene is deleted. Each `<timestamp>.json` is a saved snapshot containing the scene's content.

Two paths to recover:

1. **Create a new scene** with any name in the right chapter, then **restore** a snapshot through the Snapshots dialog.
2. **Manually copy** the content out of the most recent `<timestamp>.json` (the `content` field) into a fresh scene's HTML file.

If neither works, your project's Git history may have a copy — `git log -- "Books/<bookId>/Chapters/<chapter>/<scene>.html"` will show every commit that touched the file.

## My project won't open

Symptoms: Welcome screen offers to open, you pick the folder, nothing happens or you see an error toast.

Things to check, in order:

1. **The folder you picked must contain a `.novalist/` subdirectory.** If you accidentally pointed Novalist at the parent or a child folder, it won't find the project. Pick the project root.
2. **The `.novalist/project.json` file must be valid JSON.** If you (or another tool) corrupted it, Novalist will refuse to open. The simplest fix is to restore from a Git commit (`git checkout HEAD -- .novalist/project.json`) or from a backup.
3. **The folder is read-only.** Check filesystem permissions; Novalist needs write access.
4. **File locked by another app.** Most commonly a cloud-sync tool (Dropbox, OneDrive) holding the file mid-sync. Wait for sync to settle and try again.

## A scene is corrupted / shows raw HTML

Open the scene's `.html` file directly in a text editor (with Novalist closed). The file should be a single well-formed HTML fragment.

If the file has been doubly-encoded, contains the JSON wrapper accidentally pasted in, or is otherwise broken:

1. Close Novalist.
2. Restore the most recent snapshot from `<Project>/Books/<bookId>/Snapshots/<sceneId>/<timestamp>.json` — the `content` field is the HTML.
3. Save and reopen Novalist.

## My extension won't load

In the **Extensions → Installed** tab, look for a red error indicator on the extension.

Common causes:

- **Version mismatch.** The extension's `minHostVersion` is higher than your running Novalist, or its `maxHostVersion` is too low. Update Novalist or the extension.
- **Missing dependency DLL.** Some extensions bundle native dependencies. Re-install from the store.
- **Disabled in settings.** Check the **Enabled** toggle.

If the app crashes on startup because of an extension, close Novalist and delete or rename the extension folder under `<app-data>/Extensions/<extensionId>/`. The next startup will skip the missing extension.

## A hotkey isn't working

- **Conflict.** Two actions bound to the same gesture: only the first registered fires. Open **Settings → Keyboard Shortcuts** and search for the gesture to see all bindings.
- **CanExecute returned false.** Some actions are gated (Git actions need a Git repo; editor actions need a scene open). The disabled state is silent — the action just won't fire.
- **Focus wrong.** Editor formatting hotkeys only fire while focus is in the editor. Click into the editor first.

## The status-bar word goal isn't updating

- **Daily goal at 0.** Set a non-zero goal in **Settings → Writing Goals → Daily word goal**.
- **Writing not in the active book.** Word goal counts the active book's words. Switching books resets the percentage to that book's totals.
- **Manual edits via the file system.** Novalist tracks word counts in the in-memory project; if you edit `.html` files outside the app it doesn't notice until you reopen the project.

## Grammar check isn't working

- **Disabled.** Toggle on at **Settings → Writing Assistance → Grammar & Spelling Check**.
- **Network blocked.** Default endpoint is the public LanguageTool API. Some networks block it.
- **Self-hosted endpoint wrong.** Check the URL in **Settings → Writing Assistance → Grammar check API URL**.
- **Language unsupported by LanguageTool.** LanguageTool covers most major languages but not all.

## Snapshots are taking up too much space

The snapshot folder grows over time. Two strategies:

- **Manual cleanup.** Open the Snapshots dialog for each scene, delete older snapshots one by one. Keep labeled snapshots and a few recent unlabeled ones.
- **Gitignore them.** Add `Books/*/Snapshots/` to `.gitignore` if you'd rather rely on Git history. You still get the per-scene history while writing; you just don't commit it.

## Git operations failing

- **`git` not on PATH.** Install Git, ensure `git --version` works from a terminal in the project folder.
- **No upstream remote.** Push and Pull are disabled until you `git remote add origin <url>` from a terminal.
- **Authentication.** Push fails with auth errors? Configure Git's credential helper from the CLI; Novalist piggy-backs on whatever auth `git` uses.
- **Merge conflicts.** Novalist's Git view doesn't resolve conflicts. Drop to a CLI or external Git client, resolve, then return.

## Importing from Obsidian

The Welcome screen has an **Import from Obsidian** action. Point it at a folder produced by the legacy "Obsidian Novalist Plugin"; it scaffolds a Novalist project from the markdown and metadata files. Original folder is not modified.

Limitations of the importer:

- Custom-property types may need adjusting in the new project.
- Plotlines and timeline entries may need re-creation if the source plugin didn't expose them.
- Snapshot history from Obsidian's daily-notes plugin is not imported.

## Cloud sync caveats

Novalist projects work with any file-sync service (Dropbox, OneDrive, iCloud Drive, Google Drive's desktop client, Syncthing). A few rules:

- **Don't have the project open on two machines at once.** Cloud-sync conflict files can result.
- **Wait for sync to finish before opening on another machine.** Otherwise you might open a half-synced state.
- **Beware of partial file paths.** Some sync clients keep "online-only" placeholders for unopened files; Novalist needs the actual content.

## Performance issues

Novalist is light, but very large projects (50+ chapters, hundreds of scenes, big image gallery) can slow down some views.

- **Manuscript mode** renders every scene at once. On large books, switch to Corkboard or Outliner mode while planning, only use Manuscript for read-throughs.
- **Image gallery** decodes thumbnails lazily. The first scroll through is slower; subsequent ones reuse the cache.
- **Smart Lists** filter live. If a smart list is unused, delete it.
- **Grammar check** calls a remote API per scene; disable if it lags.

## Reporting bugs

Open an issue on the project's repo. Include:

- Your OS and version.
- The Novalist version (visible at the bottom of the Start menu).
- A description of what you did and what happened.
- Steps to reproduce, if you have them.
- Whether the issue persists after disabling all extensions.

## Where to go next

- [Snapshots](17-snapshots.md) — per-scene recovery.
- [Git integration](18-git.md) — project-level recovery.
- [Settings](23-settings.md) — toggling features that are misbehaving.
