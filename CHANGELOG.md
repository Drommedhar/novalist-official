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

### Added

- **Codex PDF export** — a new export format that writes your world bible as a single, self-contained PDF with entry images drawn into the document, so there is no image folder to send alongside it. Headings, bullet lists, bold text, and line breaks in your entry text are laid out as formatted prose. Every entry starts on its own page, and PDF bookmarks list each entry under its group so any reader can jump straight to a character.
- **Codex entry selection** — the Codex exports now show a checkbox per entry, grouped by Characters, Locations, Items, and Lore and sorted by name, with a search box, select-all and select-none buttons, and a running count. Untick the entries you do not want in the file; everything is included by default. Select all and select none apply to whatever the search currently shows, so you can tick or untick a group of entries in one click.
- **Bundled typefaces** — Fraunces, Newsreader, and Courier Prime now ship inside the app. The interface and the default writing face look identical on every machine, with no network and nothing to install, and all three are offered at the top of the editor's font list.
- **Formatted text fields** — Codex sections and per-scope overrides, entity long-text fields, research notes, timeline event descriptions, and wizard answers are now formatted as you write them: headings set larger, bold actually bold, list items bulleted, links and quotes picked out. The formatting marks are hidden, so a finished entry reads as clean prose; they reappear on whichever line your cursor is on, in case you want to edit them by hand. Each field carries a toolbar — bold, italic, strikethrough, heading, bulleted and numbered lists, quote, link — with `Ctrl+B` and `Ctrl+I`, so you never have to know the Markdown syntax to use it. What is saved is still plain Markdown, exactly as before: nothing is rewritten behind your back and existing entries open unchanged.

### Changed

- **A new default look** — the Default theme is now Novalist's own identity: a deep, near-black page with parchment-coloured text and gilt accents. Panels are the one raised surface, edges are fine parchment hairlines instead of grey rules, and primary buttons carry a gold foil fill with a dark pressed-ink label. The active choice in a set of options fills the same way, and selecting text tints it gilt.
- Headings are set in Fraunces and interface text in Newsreader. Anything the app worked out for you — word counts, reading times, file sizes, version numbers, timestamps, branch names — is now set in Courier Prime, so figures read as the record they are and stay in step in a column.
- Interface text is larger and panels sit on more generous spacing, following the identity's own type, spacing, and corner-radius scale rather than the old compact one.
- New projects open the editor in Newsreader at 17px, instead of Inter at 14px. Projects you have already set up keep the font and size you chose.
- Long passages now open with a gilded drop cap — a manual page, or the opening description of a Wiki article.
- Choosing the Discord or Catppuccin Mocha theme now changes colour only. Type, spacing, and corner radii belong to the Novalist identity and stay put whichever palette you pick.
- Setting a custom accent colour flattens the gold foil on primary buttons to a single fill in your colour, rather than leaving a gradient that no longer matches.
- Readability scores in the status bar are drawn from the theme's own palette and follow whichever theme is active, instead of a fixed set of traffic-light colours.
- **Codex entry fields sit side by side** — short properties like Eye Colour, Role, and Build now flow into as many columns as the panel is wide instead of one per row, so an entry that used to run well past the bottom of the window fits on a single screen. Long text fields still take the full width.
- Novalist opens maximised. Restoring the window down still gives you the previous, smaller size.
- **Panels remember their size** — drag the binder, the inspector, or the scene-notes panel to the width or height you want and it is still there next time you open Novalist.
- The binder and inspector now open at a width proportional to your display instead of a fixed 240 and 280 pixels, so on a large screen a scene title fits on one line out of the box rather than being cut off. They can also be dragged considerably wider than before.
- Dialogs are wider, and update release notes have more room. The update notice used to break a version number across three lines.
- The welcome screen's toolbar now shows only the Novalist wordmark. It used to carry Add Chapter, Add Scene, search, snapshots, and the three panel toggles, none of which do anything before a project is open.
- On Windows and Linux the window no longer shows the grey system title bar and menu strip above the app. Novalist's own toolbar is the title bar, and the minimise, maximise, and close buttons are painted to match the theme — the same integrated look macOS already had. Press `Alt` for the File / Edit / Go / View menus.
- The focus peek card is larger, so an entry's sections, relationships, and description no longer read as a narrow column of fine print.
- The live statistics in the status bar sit in the true centre of the bar rather than drifting with whatever is beside them.
- Codex exports now write their group headings and fixed field labels (Role, Age, Type, Relationships, …) in your interface language instead of always in English.
- Characters whose age is set as a birth date no longer get an Age line in codex exports, where it only repeated the date.

### Fixed

- The focus peek card showed a Codex section's raw Markdown — `# Strengths` and `* Brave` as typed — where the Wiki has always rendered it. It now renders the same way.
- With typewriter scrolling on, right-clicking in the editor could dismiss the context menu the instant it opened: the click recentred the caret, and that scroll counted as scrolling away from the menu. The menu now stays put, and a right-click no longer moves the page under it.
- Text fields in the Codex, the outliner, and the notes panel now look like text fields before you click them. They previously showed no border or background until hovered, so a value read as plain text.
- Extension settings pages rendered as an unstyled stack of full-width controls with no spacing. Their fields now lay out in two columns inside a proper panel, matching the built-in Settings.
- The divider above the scene notes panel now highlights in the accent colour while you drag it, like the binder and inspector dividers already did.
- Dragging the scene notes panel taller now grows the Summary box as well; the extra height used to go to Notes alone while Summary stayed capped.

---

## [2.3.1] - 2026-07-26

Nothing yet.

---

## [2.3] - 2026-07-25

### Added

- **Exposé view** - a per-book pitch document with its own editor, reachable from the Publish
  group in the activity bar. Above the writing surface, two live counters show how many characters
  and how many Normseiten you have used. Set a character limit, a page limit, or both: the counter
  turns amber as you approach a limit and red once you pass it, but typing is never blocked. The
  exposé and its limits are stored with the book, so they are there the next time you open the
  project.
- **Paragraph styles in the Exposé** - Title, Section, and Body buttons above the editor mark the
  paragraph the caret is in (or every paragraph in the selection). The active button follows the
  caret, and styled paragraphs are drawn larger and bolder so the structure is visible while you
  write. Title and Section are what become upper-case headings in the export.
- **Export Normseiten** from the Exposé view - a DOCX laid out as German standard pages, ready to
  send to an agent or publisher. The exposé exports line for line: consecutive paragraphs stay on
  adjacent lines and only an empty paragraph opens a blank one, so what you laid out is what the
  page shows.
- **Normseiten export preset** for the manuscript - Courier New 12pt at exactly 20pt line spacing
  on A4, every line hard-wrapped at 60 characters and a page break forced every 30 lines, with a
  running header carrying the title and the page count. Because the pagination comes from the grid
  rather than from Word's reflow, the page count in the document is the count a lector will read
  off it. DOCX only.

### Changed

- Paragraphs carrying a heading style - imported projects can have them - are now drawn as headings
  in the editor instead of looking like ordinary text.

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

[Unreleased]: https://github.com/Drommedhar/novalist-official/compare/v2.3.1...HEAD
[2.3.1]: https://github.com/Drommedhar/novalist-official/compare/v2.3...v2.3.1
[2.3]: https://github.com/Drommedhar/novalist-official/compare/v2.2...v2.3
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
