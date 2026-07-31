# Novalist User Manual

This manual covers everything Novalist does, from opening your first project to writing your own extensions. Each page describes a single feature area in depth and assumes no prior knowledge of the app.

Novalist is an offline-first writing application for novelists and worldbuilders. Your project lives in a folder on your disk as a set of plain JSON manifests and HTML scene files — nothing is locked behind a cloud account, and you can version-control the whole thing with Git.

## How to read this manual

If you are brand new, start with [Getting Started](01-getting-started.md) and then [Interface Overview](02-interface-overview.md). After that, pages can be read in any order — each one stands alone.

If you are looking for a specific feature, jump straight to its page from the table of contents below.

## Table of contents

### The basics

1. [Getting Started](01-getting-started.md) — install Novalist, create your first project, write your first scene.
2. [Interface Overview](02-interface-overview.md) — the three-pane window: binder, main area, inspector; toolbar, status bar, start screen, command palette.
3. [Importing a manuscript](38-manuscript-import.md) — bring an existing book in from Word, OpenDocument, EPUB, Markdown, plain text or RTF.
4. [Projects & Books](03-projects-and-books.md) — what a project is, the multi-book model, project folder layout, recent-projects list.

### Writing

5. [Chapters & Scenes](04-chapters-and-scenes.md) — the unit of writing in Novalist. Status, dates, plotlines, favorites, reordering, archiving.
6. [Editor](05-editor.md) — WYSIWYG editor, inline formatting, paragraph styles, focus mode, split editor, auto-replacements, dialogue correction, grammar check, entity hover cards.
7. [Manuscript view](10-manuscript.md) — read the whole book end-to-end, switch to corkboard or outliner.
8. [Find & Replace](21-find-replace.md) — project-wide search with scopes and snapshot-guarded replace-all.
9. [Quick Open](31-quick-open.md) — one search box over scenes, Codex, notes, comments, research, and events.

### Worldbuilding

10. [Codex (Characters, Locations, Items, Lore)](06-codex.md) — entities, sections, custom properties, chapter overrides, relationships, custom entity types, guided wizards.
11. [Wiki](30-wiki.md) — a read-only, Wikipedia-style reader over the whole Codex: infobox, sections, cross-links, and a per-entity Appearances timeline.
12. [Templates](07-templates.md) — entity templates, project templates, story-structure templates, default values.
13. [Plot Grid & Plotlines](08-plot-grid.md) — track which scenes belong to which threads.
14. [Relationships graph](14-relationships.md) — auto-clustered family/social graph of your characters.
15. [Dialogue](33-dialogue.md) — every line one character speaks, in story order, to catch voice drift; edit lines in place and correct who said what.
16. [Planning board](37-planning-board.md) — an infinite board of loose cards and labelled connectors for ideas that are not scenes yet.
17. [Style report](36-style-report.md) — offline craft checks: adverbs, filter words, passive voice, sticky sentences, repeated openers, sentence-length variation.
18. [Calendar & in-world dates](13-calendar.md) — Gregorian calendar, scene placement, story date ranges.
19. [Timeline](12-timeline.md) — chronological event view across acts, chapters, scenes, and manual events, with story-structure templates.
20. [Series](40-series.md) — every book in the project at once, and where the shared World Bible entries appear across them.
21. [Maps](29-maps.md) — interactive 2D/3D map view with layered images, terrain, roads, buildings, and entity-linked pins.

### Project management

22. [Dashboard](11-dashboard.md) — daily and project word goals, status breakdown, chapter pacing, echo phrases.
23. [Research](15-research.md) — notes attached to your project.
24. [Smart Lists](16-smart-lists.md) — saved scene queries by status, POV, tag, plotline, plus hand-curated collections and bookmarks.
25. [Snapshots](17-snapshots.md) — per-scene version history, taken from the inspector.
26. [Image Gallery](19-image-gallery.md) — every image in the project at a glance.
27. [Git integration](18-git.md) — commit, push, pull without leaving the app.
28. [Backups](35-backups.md) — automatic whole-project archives with in-app restore.

### Output

29. [Exposé](32-expose.md) — the per-book pitch document, with live character and Normseiten counts against your limits.
30. [Export](20-export.md) — EPUB, DOCX, PDF, Markdown, Normseiten, and more, plus formats added by extensions.
31. [Command line & links](41-command-line.md) — write an export without opening the app, and link to a scene from another program.

### The inspector

31. [Inspector](22-context-sidebar.md) — the right-hand context sidebar: Context and Footnotes tabs for the open scene. (Synopsis and notes live in the bottom scene-notes dock; snapshots in a toolbar dialog.)

### Customisation

32. [Settings](23-settings.md) — appearance, editor, writing assistance, templates, diagnostics, global vs per-project scope.
33. [Extensions](24-extensions.md) — .NET extensions in the core process, webview panels in the binder, the AI Assistant, writing your own.
34. [Command palette](25-command-palette.md) — every shell command in one searchable box.
35. [Hotkeys reference](26-hotkeys.md) — every default keyboard shortcut.
36. [Localization](27-localization.md) — bundled languages (English, German, Simplified Chinese), contributing translations.
37. [Custom themes & language packs](34-custom-themes-and-languages.md) — add your own colour schemes and interface languages by dropping a file into a folder.
38. [Accessibility](39-accessibility.md) — reading-comfort settings, the High Contrast theme, read-aloud, and what is honestly still missing.

### Help

39. [Troubleshooting & FAQ](28-troubleshooting.md) — the core process, common problems, where files live, how to recover.

## Conventions used in this manual

- **Activity bar** is the slim icon rail on the far left that switches the main area between views (Dashboard, Manuscript, Timeline, Plot Grid, Calendar, Relationships, Dialogue, Style report, Planning board, Series, Codex, Wiki, Maps, Research, Gallery, Exposé, Export, Git, Settings, plus extension views).
- **Binder** is the left pane next to the activity bar: the chapter/scene tree, with Smart Lists, Collections and Bookmarks tabs.
- **Main area** is the center pane showing the active view, with an optional **scene-notes dock** (Synopsis + Notes) beneath the editor.
- **Inspector** is the right pane: Context and Footnotes tabs for the open scene.
- **Toolbar** is the slim bar at the top: binder toggle, backstage-drawer burger, book and draft selectors, project name, new chapter / new scene, Search, Snapshots, scene-notes toggle, inspector toggle.
- **Status bar** is the bottom strip with live word counts, a project metrics strip with an overview popover, goals, git, and the core-connection status.
- Keyboard shortcuts are written as `Ctrl+Shift+P`. On macOS, read `Ctrl` as `Cmd`.

## A note on data safety

Everything in your project lives in a single folder. The `.novalist/` subfolder holds the JSON manifests; the `Books/` subfolder holds your chapters, scenes, and entity files. You can back up your project by copying the folder, version-control it with Git, or sync it through any file-sync tool you already use.

Novalist also protects your work in three ways of its own:

- **Backups** archive the whole project folder to a ZIP outside the project, on open, on close, and on a timer, with a restore button in Settings. On by default. See [Backups](35-backups.md).
- **Snapshots** cover a single scene: take one from the toolbar Snapshots button at any time, and replace-all operations take one automatically before touching your text — so you can revert one scene without affecting the rest. See [Snapshots](17-snapshots.md).
- **Git** gives you full project history when you want to drive it yourself. See [Git integration](18-git.md).
