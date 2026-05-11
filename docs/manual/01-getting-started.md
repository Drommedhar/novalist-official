# Getting Started

This page walks you from a freshly installed Novalist to writing your first scene. It takes about ten minutes.

If you already have Novalist open and just want to find your way around, skip to [Interface Overview](02-interface-overview.md).

## Installing Novalist

Novalist is a desktop application built on .NET 8 and Avalonia. It runs on Windows, macOS, and Linux. Download the latest release for your platform from the project's release page and run the installer (or, on macOS and Linux, extract the archive and launch the `Novalist` binary).

No account is required. Novalist works fully offline. The only times it touches the network are:

- Checking for application updates (toggleable in [Settings](23-settings.md)).
- Browsing the extension gallery (toggleable in Settings).
- Calling the LanguageTool grammar service when grammar check is enabled (toggleable, and the endpoint can be replaced with a self-hosted server).

## The Welcome screen

When you start Novalist with no project open, the **Welcome** screen appears. It has three columns of actions:

- **Create a new project** — opens an inline form. Fill in:
  - **Project name** — used as the folder name on disk and the title shown in the app.
  - **First book name** — every project contains at least one book. You can rename it later or add more books from the book picker.
  - **Location** — the parent folder where Novalist will create the project folder. Use **Browse** to pick one.
  - **Template** — choose **Blank** for an empty project, or one of the bundled story-structure templates (three-act, Save the Cat, hero's journey, etc.). Templates pre-seed chapters and acts; see [Templates](07-templates.md).
  - Hit **Create** and Novalist will scaffold the folder and open the project.
- **Open existing project** — opens a folder picker. Point it at any folder that contains a `.novalist/` subdirectory.
- **Recent projects** — a list of projects you have opened before, sorted by most recent. Each card shows the project's cover image (if you have set one for the first book) and the project name. Click a card to open. Right-click a card for **Remove from list**.

There is also an **Import from Obsidian** entry that converts a project produced by the legacy "Obsidian Novalist Plugin" into a native Novalist project. See [Troubleshooting](28-troubleshooting.md) if you have legacy projects.

## Your first project

Pick **Create new project**, give it a name like `My First Novel`, set a location somewhere you'll find it again, and accept the default **Blank** template. After clicking **Create**, the Welcome screen disappears and the main workspace opens.

You are looking at the **Dashboard** view by default. The dashboard is empty for a brand-new project — that's expected. To start writing you first need a chapter.

## Creating a chapter and a scene

On the left side of the window is the **activity bar** (the narrow strip of icons). Click the scroll icon (tooltip: **Manuscript**) or use the top app bar:

1. Click the **Chapter** button in the top app bar (book icon, labeled "Chapter"). A small dialog appears asking for a chapter name. Type `Chapter 1` and press **Create**.
2. The new chapter shows up in the left **Explorer** sidebar with no scenes underneath it. Click the **Scene** button next to the chapter button. A dialog asks for a scene name and which chapter it belongs to. Pick your chapter and name the scene `Opening`.
3. The scene appears under the chapter in the Explorer and opens in the **Editor** automatically.

You can also create chapters and scenes by right-clicking inside the Explorer.

## Writing

Type into the editor. As you write you'll see:

- A live **word count** in the bottom-left of the status bar.
- A **reading-time** estimate next to it.
- A **readability badge** showing the Flesch reading-ease score, color-coded by difficulty.
- In the bottom-right, two progress bars: your **daily goal** (set in Settings) and your **project goal**.

The editor saves your scene automatically two seconds after you stop typing. There is also `Ctrl+S` to save immediately. Saved scenes are written to disk as HTML files inside the chapter folder, and a **snapshot** is taken on every save so you can roll back later.

## Where things live

By default the project folder looks like this:

```
My First Novel/
├── .novalist/                  # JSON manifests (do not edit by hand)
│   ├── project.json
│   ├── settings.json
│   └── ...
└── Books/
    └── <bookId>/
        ├── Chapters/
        │   └── Chapter1/
        │       └── Opening.html
        ├── Characters/
        ├── Locations/
        ├── Items/
        ├── Lore/
        ├── Images/
        └── Snapshots/
```

The structure makes the project easy to read with any text editor, easy to back up, and easy to version-control with Git. See [Projects & Books](03-projects-and-books.md) for the full layout.

## Next steps

Now that you have a working project, here are the most useful things to learn next:

- [Interface Overview](02-interface-overview.md) — every part of the window and what it does.
- [Editor](05-editor.md) — formatting, focus mode, split view, the comment and footnote systems.
- [Codex](06-codex.md) — create your first character or location.
- [Dashboard](11-dashboard.md) — set up your daily word goal so the progress bar actually moves.
- [Hotkeys reference](26-hotkeys.md) — print this page for quick reference.
