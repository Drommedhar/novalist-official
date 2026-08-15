# Troubleshooting & FAQ

This page collects the questions that come up most often, the places where things can go wrong, and the recovery procedures for the worst cases.

## How the app is put together

The window you see is an Electron shell; all project logic — loading, saving, search, export, git — runs in a bundled C# core process ("Novalist Core") that the app starts automatically. You never launch it yourself. The right side of the status bar shows the connection:

- **Core connected (version)** — everything is running normally.
- **Connecting to core...** — the core is still starting, or has just restarted.

If the core process ever crashes, the app restarts it automatically and reconnects. If the status bar stays on "Connecting to core..." for more than a few seconds, restart Novalist; if that doesn't help, see the diagnostic log below.

The core works on your project **one request at a time**. That is deliberate: it keeps two screens from reading the same half-written answer. It also means a screen opened while something else is still loading can take a moment to fill in. Nothing is lost; it is a queue, not a failure. Version control, backups and exporting run alongside rather than in that queue — they work on files rather than on the project the app is holding — so a slow archive, or a git command waiting on a server, cannot hold the rest of the app up. Checking whether the app is alive, closing it, and the dialogs an extension puts up all skip the queue too.

## Where do my files live?

Two locations matter:

- **Your project folder** — wherever you created it. All of your manuscript, entities, snapshots, research, and images live here.
- **The app data folder** — settings, recent-project list, installed extensions, diagnostic logs.
  - Windows: `%APPDATA%/Novalist/`
  - macOS: `~/Library/Application Support/Novalist/`
  - Linux: `~/.config/Novalist/`

Backing up just the project folder is enough to safeguard your writing.

## The diagnostic log

**Settings → Diagnostics → Diagnostic logging** is an opt-in file log written to the `logs/` folder under the app data folder. It exists so you can attach a log to a bug report for issues we cannot reproduce.

The log **never contains story content** — no titles, no prose, no entity data, no file paths. It records only structural information: state names, counts, timings, and error stack traces. You can send it without exposing your writing.

## I deleted a scene by accident. Can I get it back?

Yes. Look in `<Project>/<BookFolder>/Snapshots/<sceneId>/`. The folder remains even after the scene is deleted. Each `<timestamp>.json` is a saved snapshot containing the scene's content.

Two paths to recover:

1. **Create a new scene** with any name in the right chapter, then **restore** a snapshot from the **Scene snapshots** dialog on the writing bar.
2. **Manually copy** the content out of the most recent `<timestamp>.json` (the `content` field) into a fresh scene's HTML file.

If neither works, your project's Git history may have a copy — `git log -- "Books/<bookId>/Chapters/<chapter>/<scene>.html"` will show every commit that touched the file.

Also check the **Archive** section at the bottom of the binder's Chapters tab — archived scenes are not deleted and can be restored with one click.

## My project won't open

Symptoms: you choose **Browse for Project Folder**, pick the folder, and nothing happens or you see an error.

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

Extensions are .NET DLLs loaded by the core process at startup. Common causes when one doesn't appear:

- **Version mismatch.** The extension's `minHostVersion` is higher than your running Novalist, or its `maxHostVersion` is too low. Update Novalist or the extension.
- **Broken folder.** The extension folder must contain the DLL named in its `extension.json`. Re-install from the release package.

If the app misbehaves at startup because of an extension, close Novalist and delete or rename the extension folder under `<app-data>/Extensions/<extensionId>/`. The next startup will skip the missing extension. See [Extensions](24-extensions.md).

## A hotkey isn't working

- **Focus is in a text field.** While typing, only gestures that include `Ctrl` (or `Cmd` on macOS) fire — `Alt+F` for Focus Mode needs focus outside a text field.
- **The gesture doesn't exist.** The list of shortcuts a fresh install ships with is short; see [Hotkeys](26-hotkeys.md). Anything not listed there is still reachable through the [Command Palette](25-command-palette.md), the menu bar or the mode panel — and you can give it a gesture of your own in Settings → Keyboard shortcuts.

## Grammar check isn't working

- **Disabled.** Toggle on at **Settings → Writing assistance → Grammar check**.
- **Network blocked.** The grammar check calls the LanguageTool API. Some networks block it.
- **Language unsupported by LanguageTool.** LanguageTool covers most major languages but not all.

## Snapshots are taking up too much space

The snapshot folder grows over time. Two strategies:

- **Manual cleanup.** Open a scene, review its snapshots in the **Scene snapshots** dialog on the writing bar, and delete older ones. Keep a few recent snapshots per scene.
- **Gitignore them.** Add `Books/*/Snapshots/` to `.gitignore` if you'd rather rely on Git history. You still get the per-scene history while writing; you just don't commit it.

## Git operations failing

- **`git` not on PATH.** Install Git, ensure `git --version` works from a terminal in the project folder.
- **No upstream remote.** Push and Pull won't work until you `git remote add origin <url>` from a terminal.
- **Authentication.** Push fails with auth errors? Configure Git's credential helper from the CLI; Novalist piggy-backs on whatever auth `git` uses.
- **Merge conflicts.** Novalist's Git view doesn't resolve conflicts. Drop to a CLI or external Git client, resolve, then return.

## Importing from Obsidian

**File → Import from Obsidian Plugin** is the way in. Point it at a vault used with the legacy "Obsidian Novalist Plugin"; detected plugin projects are offered for import, you choose the output folder and names, and a Novalist project is scaffolded from the markdown and metadata files. The original vault is not modified, and the importer writes an `import-log.txt` into the new project so you can review what was converted.

Limitations of the importer:

- Custom-property types may need adjusting in the new project.
- Plotlines and timeline entries may need re-creation if the source plugin didn't expose them.

## Cloud sync caveats

Novalist projects work with any file-sync service (Dropbox, OneDrive, iCloud Drive, Google Drive's desktop client, Syncthing). A few rules:

- **Don't have the project open on two machines at once.** Cloud-sync conflict files can result.
- **Wait for sync to finish before opening on another machine.** Otherwise you might open a half-synced state.
- **Beware of placeholder files.** Some sync clients keep "online-only" placeholders for unopened files; Novalist needs the actual content.

## Performance issues

Novalist is light, but very large projects (50+ chapters, hundreds of scenes, big image gallery) can slow down some views.

- **Corkboard and Outliner** build a chapter's cards and rows when you scroll near it, not all at once, so opening a fifty-chapter book is about as fast as opening a three-chapter one. The space below is already reserved, so the scrollbar is the right length and scrolling lands where you expect.
- **Manuscript mode** still renders every scene at once, because it is one continuous text. On very large books, use Corkboard or Outliner while planning and Manuscript for read-throughs.
- **Image gallery** decodes thumbnails lazily. The first scroll through is slower; subsequent ones reuse the cache.
- **Grammar check** calls a remote API per scene; disable it in Settings if it lags.

## Updates

Novalist checks for updates on startup (in installed builds) and from **Help → Check for Updates…** at any time. When a newer release exists it is **downloaded automatically** on all platforms, with a progress bar in the update dialog; once ready you can **Restart & install** immediately, or dismiss and it installs on next quit.

- On **macOS**, an installed update can only be applied to a code-signed build. An unsigned/ad-hoc build will download the update but fail to install it — use the **View release** link in the dialog to update manually.
- In a development build there is no update feed, so **Check for Updates…** reports that updates are only available in installed builds.

## The manual and startup

- Use **Help → User manual** in the menu bar to open this manual inside the app: a searchable, readable overlay with every page. It opens at the page for whatever you were looking at, and the page list down the side is still there to browse or search the rest. It ships without a keyboard shortcut; give it one in Settings → Keyboard shortcuts if you want one.
- A brief **splash** appears while the core process starts and the update check runs, then the main window opens.

## Reporting bugs

Open an issue on the project's repo. Include:

- Your OS and version.
- The Novalist version, and the core version shown in the status bar's connection indicator (hover the dot at the far right — its tooltip reads "Core connected (...)").
- A description of what you did and what happened.
- Steps to reproduce, if you have them.
- The diagnostic log, if you can reproduce the issue with diagnostic logging enabled.
- Whether the issue persists after removing all extensions.

## Where to go next

- [Snapshots](17-snapshots.md) — per-scene recovery.
- [Git integration](18-git.md) — project-level recovery.
- [Settings](23-settings.md) — toggling features that are misbehaving.
