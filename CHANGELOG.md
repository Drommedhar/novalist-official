# Changelog

All notable user-facing changes to the Novalist desktop app.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and Novalist
follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html). Entries are grouped as
**Added**, **Changed**, **Fixed**, **Removed**, and **Security**.

This file covers the desktop app only. The iOS companion app is released separately under its
own `ios-*` tags and is not tracked here.

Changes land under **Unreleased**. When a tag is pushed, the release workflow uses that section as
the GitHub release notes and stamps it with the tag's version and date.

---

## [Unreleased]

Nothing yet.

---

## [2.2] - 2026-07-23

### Added

- **Wiki view** - a read-only, encyclopedia-style article for every Codex entry, reachable from
  the World group in the activity bar. Each article has a lead summary with aliases, a table of
  contents, an infobox with an image gallery, your authored sections, and automatically derived
  cross-links: relationships, "referenced by", "appears with", plotlines, and map pins. Characters
  also get a "changes over time" section built from their chapter and scene overrides (including
  per-chapter portraits) and an Appearances timeline. Images open full size in a lightbox.
- **AI article summaries in the Wiki** (optional) - when an extension provides an article
  generator, articles gain an on-demand summary with a Generate / Regenerate button, a busy state,
  and an "out of date" chip once the entity has changed since the summary was written. Summaries
  are cached inside the project. Without such an extension installed, the Wiki simply shows no
  summary section.
- **Shared scene analysis** - a scene is now analysed once and every feature reads the same
  record: entity presence (present / mentioned / absent), per-character knowledge, and findings.
  Records are stored per scene and keyed by a content hash, so an unchanged scene is never
  re-analysed and project diffs stay clean.
- **Quick Open**, **quick capture**, and **global search** for jumping to scenes, chapters and
  Codex entries without leaving the keyboard.
- **Entity proposals** - analysis can now offer to create a Codex entry it found missing, instead
  of only reporting it. Extensions gained the API to create characters, locations, items, lore and
  custom entities.
- **Claude CLI** as an AI provider, plus opt-in background scene analysis.
- Localized scene-analysis wording for English, German and Simplified Chinese.
- A privacy notice shipped with the app.

### Changed

- Scrollbars, checkboxes, dropdowns, dropdown popups and text inputs now follow the active theme
  instead of being painted in the browser's light style over a dark UI. This includes the editor,
  manuscript and map panes, and it follows live theme switches.
- Git features report themselves as unavailable, rather than failing, on platforms where Novalist
  is not allowed to launch external processes.

### Fixed

- Your settings are now loaded when the app starts, not only the first time you open the Settings
  view.
- A project's language and theme override is applied when the project opens.

---

## [2.1.1] - 2026-07-21

Re-tagged build of 2.1 to republish the release artifacts. No functional changes.

## [2.1] - 2026-07-21

### Added

- macOS builds are now code-signed and notarized, so Gatekeeper accepts the DMG without a
  right-click-to-open workaround.
- A Mac App Store build (Apple Silicon).

### Changed

- The Mac App Store build hides the in-app update UI - updates arrive through the App Store.
- Tags with a prerelease suffix (for example `2.1-beta1`) are published as GitHub prereleases.

### Fixed

- The recent-projects list no longer breaks when a project's cover image cannot be read under the
  macOS sandbox.

## [2.1-beta1] - 2026-07-21 (prerelease)

### Added

- First cut of the signed and notarized macOS DMG plus the Mac App Store packaging pipeline.

---

## [2.0] - 2026-07-20

### Fixed

- The Windows installer.

## [2.0-preview1] - 2026-07-20 (prerelease)

**Novalist was rebuilt from the ground up.** The Avalonia desktop UI was replaced by a new
Electron shell backed by a headless .NET service, and the whole feature set was rebuilt on top of
it. Projects created with 1.x open unchanged.

### Added

- **New application shell** - three-pane layout with an activity bar, binder, editor, and
  inspector, a command palette, and a full hotkey system.
- **Editor** - formatting toolbar, live word and character counts in the status bar, page view,
  entity mentions with hover cards, focus mode, focus peek, split editor for two scenes side by
  side, comments, footnotes, and a grammar-check round trip.
- **Binder** - drag and drop to reorder chapters and scenes or move scenes between chapters,
  rename, delete, status cycling, act headers, and scene archiving with a restore browser.
- **Codex** - entity sidebar with grouping, a location tree, search, and move-to-World-Bible; a
  typed, grouped detail pane; create and delete entities; aliases, sections and relationships
  editing with autocomplete and automatic inverse sync; entity images; typed custom properties;
  character chapter and scene overrides; entities from book templates; custom entity types with a
  type manager; and a guided entity-creation wizard with a character interview.
- **Planning views** - Dashboard, Manuscript, Plot Grid, Smart Lists, Timeline (with zoom
  grouping, source and character/location filters, event chips, structure templates and outline
  export), Calendar (week / month / year, with drag-and-drop scene rescheduling), and the
  Relationships graph.
- **Maps**, **Image gallery**, and the **Research library**.
- **Export** in all seven formats, and **Git** with status, commit, push, pull and discard.
- **Find and replace** across scenes.
- **Snapshots** - take, list and restore from the inspector.
- **Scene analysis** in the inspector, alongside footnote and comment lists.
- **Settings** with scoped overrides (app-level and project-level) that propagate to the editor
  live.
- **Templates editor** covering known and custom fields, properties, sections and age mode.
- **Books and drafts** - switch, create and pick straight from the toolbar; project rename by
  double-clicking the title.
- **Writing goals** editing.
- **Obsidian vault import**.
- **New-project creation UI**.
- **Update notifications** with inline in-app updates.
- **Extensions** - a headless extension host and the SDK v2 webview contribution surface, with
  declarative settings schemas that support conditional field visibility, action buttons and
  field suggestions. The settings form auto-saves; the Save button is gone.
- Native Liquid Glass styling on macOS, and the theme set migrated to the new shell.

### Changed

- The manual and README were rewritten for the new UI, with screenshots, and the in-app manual
  viewer renders them.
- The status bar was decluttered.

### Removed

- The old Avalonia UI, and the SDK's Avalonia dependency. Extensions built against SDK 9 or
  earlier must be updated to SDK 10.

### Fixed

- Unreadable light theme under macOS glass (input surfaces are now opaque).
- Entity and gallery images failing to load.
- Book-relative image paths in Maps.
- macOS Gatekeeper rejecting the app (it is now ad-hoc signed).
- Installation on machines where the optional Liquid Glass module is unavailable.

---

## [1.14.5] - 2026-07-05

### Added

- Simplified Chinese (zh-CN) localization, contributed by the community.

### Fixed

- Inserting a LanguageTool suggestion into the scene.
- Character replacement and automatic dialogue punctuation.
- Scrolling when typewriter mode is switched off.

## [1.14.4] - 2026-06-17

### Fixed

- Several small bugs across the editor and project handling.

## [1.14.3] - 2026-06-17

### Added

- LanguageTool Premium support - use your own Premium account for grammar checking.

## [1.14.2] - 2026-05-25

### Fixed

- LanguageTool grammar-check issues and assorted small bugs.
- Documentation corrections.

## [1.14.1] - 2026-05-23

### Fixed

- Keyboard handling issues.
- Act assignment.
- Assorted bugs, plus documentation corrections.

## [1.14.0] - 2026-05-21

### Added

- **Opt-in diagnostic logging** (Settings -> Diagnostics) so a log can be sent when a problem
  cannot be reproduced. The log never contains your story text.
- **Project-level settings** - override app settings per project.

### Changed

- **New on-disk project layout (v3)**, with automatic migration when an older project is opened.
  Scenes carry front matter, and the app reconciles changes made to the files outside Novalist.

---

## [1.13.3] - 2026-05-18

### Fixed

- The WebKit install wizard on Linux.

## [1.13.2] - 2026-05-18

### Added

- Linux AppImage builds.
- Catppuccin theme.

### Fixed

- Linux build and WebView issues, plus UI polish.

## [1.13.1] - 2026-05-16

### Fixed

- The 3D map on macOS.

## [1.13.0] - 2026-05-16

### Added

- **Maps** - place and browse map pins, including a 3D map view.
- **Wizards** - a project outlining wizard (snowflake method), a guided entity-creation wizard,
  and a character interview. Extensions can contribute their own wizards.
- **Drafts** - keep multiple drafts of a book, clone from the current one, switch from the
  toolbar, and delete drafts you no longer need.
- **Scene archiving** with restore, a read-only archive preview, and an archive panel.
- **Typewriter scrolling** with a configurable anchor (top, middle, bottom).
- **Page view** - render the editor as a printed-book page with paper background, margins and
  shadow.
- **Aliases** for entities.
- **Writing streak and word history** on the dashboard.

### Changed

- Relationship type names are localized.

### Fixed

- Themes not applying.

---

## [1.12.0] - 2026-05-12

### Added

- A recent-activity feed on the dashboard, with timestamps.
- A busy/progress dialog for long-running operations, available to extensions through the SDK.

### Changed

- The SDK surface was expanded; the manual was updated.

### Fixed

- Assorted bugs.

## [1.11.0] - 2026-05-11

### Added

- **Calendar view** with in-world calendars and story dates, including a story-date-range dialog.
- **Relationships graph**.
- **Footnotes panel**.
- Favourites and per-item colours in the explorer.
- **Inline actions** - extensions can contribute actions that appear directly in the editor.
- The full user manual (`docs/manual/`).

### Fixed

- Focus peek.

## [1.10.0] - 2026-05-10

### Added

- **Snapshots** - take a snapshot of a scene before risky edits, list them, restore, and compare
  against the current text with line-level apply.
- **Find and replace** across scenes.
- **Command palette**.
- **Smart Lists** - saved, rule-based scene queries in the binder.
- **Plot Grid** - map plot threads against scenes.
- **Research library** - notes, links and imported files.
- **Comments** on selected text.
- **Scene notes and synopsis** panel.
- **Manuscript view modes** - manuscript, corkboard and outliner.
- **Export presets** and **project templates**.
- Point-of-view detection.
- Split editor toggle.

### Fixed

- Plot grid columns, the editor splitter, screen capture on macOS, and several macOS-specific
  issues.

## [1.9.0] - 2026-05-10

### Added

- Toast notifications.
- Editor tabs.
- Design tokens behind the theme system, and a UI polish pass across the dashboard, Codex hub,
  manuscript and timeline.
- Activity-bar contribution point in the SDK, so extensions can add their own top-level entries.

### Changed

- The AI assistant moved out of the core app into its own extension, which now registers itself
  through the SDK.

### Fixed

- Performance of project loading and the image gallery, timeline and manuscript views.

## [1.8.0] - 2026-05-03

### Added

- **AI-assisted grammar checking** - extensions can contribute a grammar checker, and the AI
  assistant ships one.
- More actions can be bound to hotkeys.

---

## [1.7.5] - 2026-04-30

### Added

- **Built-in grammar and style checking** with a configurable language checker.

### Changed

- Settings were reorganized into clearer sections.
- The update check moved to the splash screen, so it no longer interrupts you mid-session.

### Fixed

- Image paths.
- The image overlay is now scrollable.
- Context sidebar behaviour.

## [1.7.4] - 2026-04-22

### Fixed

- A crash in HTTP requests.

## [1.7.3] - 2026-04-22

### Fixed

- A crash on startup, and Avalonia was updated.

## [1.7.2] - 2026-04-22

### Added

- Renaming projects and books.

### Fixed

- Small bugs, plus documentation updates.

## [1.7.1] - 2026-04-16

### Fixed

- Hotkeys not working.

## [1.7] - 2026-04-12

### Added

- Codex hub quality-of-life improvements, including opening an entity's folder from the host.

---

## [1.6] - 2026-04-10

### Fixed

- Theme issues; Avalonia was updated.

## [1.5] - 2026-04-09

### Added

- **Custom entity types** - define your own Codex categories beyond characters, locations, items
  and lore, with an SDK example showing how extensions can use them.

### Fixed

- Small UI issues.

## [1.4] - 2026-04-08

### Added

- **Extension store** - browse and install extensions from inside the app.
- The SDK is published to NuGet, with a getting-started guide.

### Fixed

- macOS build and release-packaging issues.

## [1.3] - 2026-04-07

### Added

- Automatic dialogue punctuation correction.

### Fixed

- Automatic text replacement.

## [1.2] - 2026-04-06

### Fixed

- The macOS build on Apple Silicon.

## [1.1] - 2026-04-06

### Added

- In-app update checking and installation.

## [1.0] - 2026-04-06

First public release.

### Added

- Rich text editor with spellcheck.
- Books organized into chapters and scenes with status tracking (Outline, First Draft, Revised,
  Edited, Final) and scene metadata: point of view, emotion, intensity, conflict and tags.
- Scene notes panel beside the editor.
- Multi-book projects sharing one World Bible.
- **Codex / World Bible** - characters (with demographics, relationships, roles, groups, custom
  properties and per-chapter overrides), locations, items and lore, plus reusable templates per
  entity type and fast peek cards inside the editor.
- **Timeline** with manual events linked to chapters and scenes, categorized as plot, character or
  world events.
- **Dashboard** with word counts, daily progress and goal tracking.
- **Image gallery**.
- **AI assistant** extension with chat, story analysis (character and story consistency, scene
  statistics, revision suggestions) and project-aware prompt templating. Supports LM Studio and
  GitHub Copilot.
- **Export** to EPUB, DOCX, PDF and Markdown, with title-page customization.
- **Git integration** - branch status, ahead/behind counts, commit, push and pull.
- **Extension system** with the Novalist SDK.
- English and German localization.

---

[Unreleased]: https://github.com/Drommedhar/novalist-official/compare/v2.2...HEAD
[2.2]: https://github.com/Drommedhar/novalist-official/compare/v2.1...v2.2
[2.1.1]: https://github.com/Drommedhar/novalist-official/releases/tag/v2.1.1
[2.1]: https://github.com/Drommedhar/novalist-official/compare/v2.0...v2.1
[2.1-beta1]: https://github.com/Drommedhar/novalist-official/compare/v2.0...v2.1-beta1
[2.0]: https://github.com/Drommedhar/novalist-official/compare/v2.0-preview1...v2.0
[2.0-preview1]: https://github.com/Drommedhar/novalist-official/compare/v1.14.5...v2.0-preview1
[1.14.5]: https://github.com/Drommedhar/novalist-official/compare/v1.14.4...v1.14.5
[1.14.4]: https://github.com/Drommedhar/novalist-official/compare/v1.14.3...v1.14.4
[1.14.3]: https://github.com/Drommedhar/novalist-official/compare/v1.14.2...v1.14.3
[1.14.2]: https://github.com/Drommedhar/novalist-official/compare/v1.14.1...v1.14.2
[1.14.1]: https://github.com/Drommedhar/novalist-official/compare/v1.14.0...v1.14.1
[1.14.0]: https://github.com/Drommedhar/novalist-official/compare/v1.13.3...v1.14.0
[1.13.3]: https://github.com/Drommedhar/novalist-official/compare/v1.13.2...v1.13.3
[1.13.2]: https://github.com/Drommedhar/novalist-official/compare/v1.13.1...v1.13.2
[1.13.1]: https://github.com/Drommedhar/novalist-official/compare/v1.13.0...v1.13.1
[1.13.0]: https://github.com/Drommedhar/novalist-official/compare/v1.12.0...v1.13.0
[1.12.0]: https://github.com/Drommedhar/novalist-official/compare/v1.11.0...v1.12.0
[1.11.0]: https://github.com/Drommedhar/novalist-official/compare/v1.10.0...v1.11.0
[1.10.0]: https://github.com/Drommedhar/novalist-official/compare/v1.9.0...v1.10.0
[1.9.0]: https://github.com/Drommedhar/novalist-official/compare/v1.8.0...v1.9.0
[1.8.0]: https://github.com/Drommedhar/novalist-official/compare/v1.7.5...v1.8.0
[1.7.5]: https://github.com/Drommedhar/novalist-official/compare/v1.7.4...v1.7.5
[1.7.4]: https://github.com/Drommedhar/novalist-official/compare/v1.7.3...v1.7.4
[1.7.3]: https://github.com/Drommedhar/novalist-official/compare/v1.7.2...v1.7.3
[1.7.2]: https://github.com/Drommedhar/novalist-official/compare/v1.7.1...v1.7.2
[1.7.1]: https://github.com/Drommedhar/novalist-official/compare/v1.7...v1.7.1
[1.7]: https://github.com/Drommedhar/novalist-official/compare/v1.6...v1.7
[1.6]: https://github.com/Drommedhar/novalist-official/compare/v1.5...v1.6
[1.5]: https://github.com/Drommedhar/novalist-official/compare/v1.4...v1.5
[1.4]: https://github.com/Drommedhar/novalist-official/compare/v1.3...v1.4
[1.3]: https://github.com/Drommedhar/novalist-official/compare/v1.2...v1.3
[1.2]: https://github.com/Drommedhar/novalist-official/compare/v1.1...v1.2
[1.1]: https://github.com/Drommedhar/novalist-official/compare/v1.0...v1.1
[1.0]: https://github.com/Drommedhar/novalist-official/releases/tag/v1.0
