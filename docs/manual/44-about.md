# About

**Help → About** opens the About view: one page for the facts about your installation. Before it existed, the app's own version was nowhere at all, the core process's version was hidden in a status-bar tooltip, the changelog lived only on a web page, **Check for updates** was a menu item and nothing else, and the licences of the typefaces and runtimes Novalist ships were never shown to anyone.

About is not part of a mode, so it opens with or without a project. You can also reach it by name from the [command palette](25-command-palette.md), and you can give it a keyboard shortcut in [Settings → Keyboard shortcuts](23-settings.md#hotkeys) like any other command.

## Versions

Two numbers at the top:

- **Novalist** — the version of the application you are running.
- **Core process** — the version of the bundled Novalist Core process that does the project work. It reads **Not reported** until the core process has connected; the green dot at the right of the [status bar](02-interface-overview.md#the-status-bar) says whether it has.

The two are shipped together and normally match. Quote both when you report a problem.

## Links

- **Novalist on GitHub** — the project page, releases, and the source.
- **Report an issue** — opens a new issue against the repository.

Both open in your browser rather than inside Novalist.

## Check for updates

Runs the same check as **Help → Check for Updates**: Novalist asks whether a newer release exists and offers to fetch it. Automatic startup checks are a separate setting — see **Check for updates** in [Settings → Updates & Integrations](23-settings.md#updates--integrations).

The button is **absent in the Mac App Store build**, which is updated by the store itself and cannot update itself.

## What's new

The changelog, rendered inside the app, newest release first. It is the same file published with each release, minus the notes for whoever edits it, so it answers "what changed" and "should I update" without leaving the app. Links inside it open in your browser.

## Third-party licences

Novalist bundles other people's work, and their licences ask to be credited. The list names each component, what it is used for, its licence and its copyright notice:

| Component | Role | Licence |
| --- | --- | --- |
| Fraunces | Display typeface | SIL Open Font License 1.1 |
| Newsreader | Body typeface | SIL Open Font License 1.1 |
| Courier Prime | Monospaced typeface | SIL Open Font License 1.1 |
| Electron | Desktop app runtime | MIT License |
| .NET Runtime | Core process runtime | MIT License |

The full licence text for each of them ships with the app.

## Copy system information

One button that puts a support-ready block on the clipboard, ready to paste into a bug report:

- The Novalist and core-process versions.
- Your platform, and the Electron and Chromium versions underneath.
- The interface language and your system locale.
- The interface scale, the display scale, and the window, content and work-area sizes.

It is written in English rather than in the interface language, because it is pasted into a support thread rather than read on screen.

It is **content-free** by the same rule the diagnostic log follows: no project names, no file paths, no titles, none of your writing. The display half of it is exactly what **Read display information** in [Settings → Diagnostics](23-settings.md#diagnostics) reports, so if you have already been asked for that, this button gives it to you along with the versions.

A short confirmation appears beside the button and clears itself after a few seconds.

## Where to go next

- [Interface Overview](02-interface-overview.md#about) — where About sits in the window, and what the rest of the chrome does.
- [Settings](23-settings.md#diagnostics) — the diagnostic log and the display report to send with a bug.
- [Troubleshooting & FAQ](28-troubleshooting.md) — what to try before reporting one.
