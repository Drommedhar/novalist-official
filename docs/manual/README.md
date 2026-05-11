# Novalist User Manual

This manual covers everything Novalist does, from opening your first project to writing your own extensions. Each page describes a single feature area in depth and assumes no prior knowledge of the app.

Novalist is an offline-first writing application for novelists and worldbuilders. Your project lives in a folder on your disk as a set of plain JSON manifests and HTML scene files — nothing is locked behind a cloud account, and you can version-control the whole thing with Git.

## How to read this manual

If you are brand new, start with [Getting Started](01-getting-started.md) and then [Interface Overview](02-interface-overview.md). After that, pages can be read in any order — each one stands alone.

If you are looking for a specific feature, jump straight to its page from the table of contents below.

## Table of contents

### The basics

1. [Getting Started](01-getting-started.md) — install Novalist, create your first project, write your first scene.
2. [Interface Overview](02-interface-overview.md) — activity bar, sidebars, content area, status bar, start menu, command palette.
3. [Projects & Books](03-projects-and-books.md) — what a project is, the multi-book model, project folder layout, recent-projects list.

### Writing

4. [Chapters & Scenes](04-chapters-and-scenes.md) — the unit of writing in Novalist. Status, dates, plotlines, label colors, favorites, reordering.
5. [Editor](05-editor.md) — WYSIWYG editor, formatting, paragraph styles, focus mode, split editor, auto-replacements, dialogue correction, grammar check, focus peek.
6. [Manuscript view](10-manuscript.md) — read the whole book end-to-end, switch to corkboard or outliner, filter by status.
7. [Find & Replace](21-find-replace.md) — across-book search with regex, scopes, match list.

### Worldbuilding

8. [Codex (Characters, Locations, Items, Lore)](06-codex.md) — entities, sections, custom properties, chapter overrides, relationships, world bible vs book-scoped, custom entity types.
9. [Templates](07-templates.md) — entity templates, project templates, story-structure templates, default values.
10. [Plot Grid & Plotlines](08-plot-grid.md) — track which scenes belong to which threads.
11. [Relationships graph](14-relationships.md) — auto-clustered family/social graph of your characters.
12. [Calendar & in-world dates](13-calendar.md) — Gregorian and fully custom calendars, scene placement, story date ranges.
13. [Timeline](12-timeline.md) — chronological event view across acts, chapters, scenes, and manual events.

### Project management

14. [Dashboard](11-dashboard.md) — daily and project word goals, status breakdown, chapter pacing, echo phrases.
15. [Research](15-research.md) — notes, links, files, PDFs, images attached to your project.
16. [Smart Lists](16-smart-lists.md) — saved scene queries by status, POV, tag, plotline.
17. [Snapshots](17-snapshots.md) — per-scene version history with side-by-side compare.
18. [Image Gallery](19-image-gallery.md) — every image in the project at a glance.
19. [Git integration](18-git.md) — stage, commit, push, pull without leaving the app.

### Output

20. [Export](20-export.md) — EPUB, DOCX, PDF, Markdown, plus formats added by extensions.

### Context panels

21. [Context sidebar, Notes, Footnotes, Comments](22-context-sidebar.md) — the right-hand and bottom panels that surround the editor.

### Customisation

22. [Settings](23-settings.md) — appearance, editor, writing assistance, writing goals, accent color, language.
23. [Extensions](24-extensions.md) — install, enable, disable, browse the gallery, write your own.
24. [Command palette](25-command-palette.md) — every action in one searchable box.
25. [Hotkeys reference](26-hotkeys.md) — every default keyboard shortcut, and how to rebind.
26. [Localization](27-localization.md) — switching language, contributing translations.

### Help

27. [Troubleshooting & FAQ](28-troubleshooting.md) — common problems, where files live, how to recover.

## Conventions used in this manual

- **Activity bar** refers to the narrow vertical strip of icons on the far left of the window. Clicking one switches the main content view.
- **Sidebar** without qualification means the left sidebar (chapter/entity/smart-list explorer).
- **Context sidebar** is the right-hand panel that holds the Context tab, Footnotes tab, and any extension-contributed tabs.
- **Scene notes** is the bottom panel that holds the scene's synopsis, notes, and comments.
- Keyboard shortcuts are written as `Ctrl+Shift+P`. On macOS, read `Ctrl` as `Cmd`.

## A note on data safety

Everything in your project lives in a single folder. The `.novalist/` subfolder holds the JSON manifests; the `Books/` subfolder holds your chapters, scenes, and entity files. You can back up your project by copying the folder, version-control it with Git, or sync it through any file-sync tool you already use.

Novalist also takes **snapshots** of scenes automatically when you save, so you can revert an individual scene to a previous version without affecting the rest of the project. See [Snapshots](17-snapshots.md).
