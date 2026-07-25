<p align="center">
  <img src="docs/manual/images/interface-overview.png" alt="Novalist" width="820" />
</p>

<h3 align="center">A desktop novel-writing application for authors who want to stay organized.</h3>

<p align="center">
  <a href="https://github.com/Drommedhar/novalist-official/actions/workflows/ci.yml">
    <img src="https://github.com/Drommedhar/novalist-official/actions/workflows/ci.yml/badge.svg" alt="CI" />
  </a>
  <!-- Live: CI publishes coverage.json to the `badges` branch (eng/Publish-CoverageBadge.ps1).
       Gated at 100% by eng/Check-Coverage.ps1, so the build fails below that. -->
  <img src="https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/Drommedhar/novalist-official/badges/coverage.json" alt="Coverage" />
</p>

---

> **Disclaimer**
>
> Novalist Standalone is provided as is. It was originally developed to help me write my book and may occasionally be updated from my internal version. However, there is no guarantee of ongoing maintenance of the project. Users are free to open pull requests to be merged into the repository or fork it to customise it to their liking.

---

## What is Novalist?

Novalist is an offline-first desktop application for writing novels. It handles the full scope of a writing project — manuscript editing, worldbuilding, plotting, timelines, exporting, and version control — in a single, self-contained tool. It runs on Windows, macOS, and Linux: an Electron + React interface in front of a bundled .NET 8 core process that owns all project logic.

Rather than scattering notes across separate apps, browser tabs, and Markdown files, Novalist keeps everything about a project in one folder of plain files: chapters and scenes as HTML, entities and metadata as JSON, images and research alongside. The folder is yours — back it up, sync it, version-control it, edit it with any text editor when Novalist is closed.

## Documentation

A full **User Manual** lives in [`docs/manual/`](docs/manual/README.md). It covers every feature in detail with cross-linked pages for getting started, the interface, projects and books, the editor, worldbuilding, plotting, exporting, extensions, hotkeys, and troubleshooting.

For extension authors, the [Extension Guide](docs/extension-guide.md) walks through the SDK, hooks, packaging, and store submission.

## Features

### Interface

- **Activity-bar layout** — a slim icon rail on the far left switches views, the binder (chapter/scene tree and smart lists) sits beside it, the active view fills the center with an optional scene-notes dock beneath the editor, and a context sidebar is on the right.
- **Scene context & analysis inspector** — the right sidebar's **Context** tab shows the entities present in the current scene, a cross-chapter mention matrix, and an auto-computed scene analysis (POV, emotion, intensity, conflict, tags) with manual overrides; the **Footnotes** tab lists footnotes and comments. Synopsis and notes live in the bottom scene-notes dock; snapshots in a toolbar dialog.
- **Command palette** (`Ctrl+Shift+P`) and number-key view switching for keyboard-driven navigation.
- On macOS 26+ the window uses native **Liquid Glass**, with vibrancy on older macOS; Windows and Linux render opaque themed surfaces from the same design tokens.

### Writing

- WYSIWYG editor with formatting, paragraph styles (heading, subheading, blockquote, poetry), inline comments, and numbered footnotes.
- Auto-save with per-scene **snapshot history** and a side-by-side compare view — revert a single scene without touching the rest of the project.
- **Focus Mode** that hides every panel except the editor.
- **Split editor** for editing two scenes side by side.
- **Auto-replacements** for smart quotes, em-dashes, and ellipses with language presets (English, German, French, Spanish, Italian, Portuguese, Russian, Polish, Czech, Slovak).
- **Dialogue punctuation correction** as you type.
- **Grammar and spelling check** via LanguageTool (public endpoint by default; self-hosted endpoint supported).
- Live word count, reading time, and Flesch readability score in the status bar; per-chapter readability in the Project Overview.

### Project structure

- Multi-book projects with a shared **World Bible** for entities used across books.
- Chapters with status tracking (Outline → First Draft → Revised → Edited → Final), optional acts, optional in-world date ranges, label colors, and favorites.
- Scenes with synopsis, notes, label color, plotline membership, in-world date range, POV / emotion / intensity / conflict / tags (auto-detected with manual overrides).
- **Smart Lists** — saved scene queries by status, POV, tag, or plotline.
- **Filesystem is the source of truth** — add, move, rename, or delete scenes and chapters with any file manager and Novalist reconciles the changes, both on open and live while running. Scene identity travels in a one-line comment in each file; chapter identity in a hidden folder marker.

### Worldbuilding (Codex)

- **Characters** with name/surname, gender, age (manual or computed from birth date and the in-world calendar), role, group, physical traits, images, relationships, and per-act / per-chapter / per-scene overrides for any field as well as for images, relationships, and sections.
- **Locations** with hierarchical parents, types, and custom fields.
- **Items** with origin, type, and description.
- **Lore** entries for magic systems, religions, history, in-world books.
- **Custom entity types** — define your own (Factions, Spells, Vehicles, Races, …) with custom field schemas.
- **Templates** per entity type with default sections, custom properties, and field defaults.
- **Sections** of long-form Markdown content per entity.
- **Relationships** with auto-learned inverse-role pairs and inverse-prompting on new relationships.
- **Focus peek** card on entity hover inside the editor.
- **Wiki** — a read-only, Wikipedia-style reader over the whole Codex: one cross-linked article per entity with a lead descriptor, table of contents, infobox and image gallery, sections, relationships and reverse "referenced by", an at-a-glance stats strip, "appears with", plotlines, map pins, a per-entity Appearances timeline built from the scenes that mention it, and an optional on-demand AI summary (when an AI extension is installed).

### Planning & visualization

- **Plot Grid** — spreadsheet view of plotlines (rows) by scenes (columns); toggle scene membership in a thread with a click.
- **Timeline** — chronological view of acts, chapters, scenes, and manual events with vertical/horizontal layout and day/week/month/year zoom.
- **In-world Calendar** — Gregorian calendar view with Week, Month, and Year layouts; scenes and events appear on their in-world dates. (Custom-calendar data model exists but no editor UI yet — Gregorian only in the app today.)
- **Relationships graph** — auto-clustered force-directed graph of characters with family detection in English and German.
- **Manuscript view** — read the whole book end-to-end, switch to Corkboard for index-card planning, or Outliner for a sortable scene table.
- **Maps** — interactive layered map view: recursive layer tree with drag-and-drop nesting, per-layer opacity / lock / zoom-range / floor-stack mode; per-image rotate, resize, polygon clip mask; entity-linked colour pins; text labels; road & river spline tool with typed profiles (casing, fill, lane markings) and per-point width; terrain shapes (grass, forest, sand, …) with feathered, blendable edges and z-ordering; typed buildings (homes, schools, stations, …) with procedurally-generated footprints that snap to roads and optional multi-floor interior plans (walls, doors, windows, stairs); a freeform clip border that frames the whole map; and a one-click **3D view** — a GPU-rendered, free-fly walkthrough of the whole map with extruded buildings, sloped roofs, interiors, terrain and roads.
- **Dashboard** — totals, status breakdown, chapter pacing, echo phrases, daily / project word goals with deadlines, plus a wide project banner and a portrait book cover (the cover shows for each project on the welcome screen).

### Research & assets

- **Research view** — notes, links, files, images, and PDFs attached to the project with tags and search.
- **Image Gallery** — every project image at a glance with lazy thumbnails, search, and copy-path / reveal actions.
- Add images from file, clipboard, URL, or the existing project gallery.

### Output

- Export to **EPUB**, **DOCX**, **PDF**, **Markdown**, **Final Draft**, **LaTeX**, and **Codex Markdown**.
- **Layout presets** — Default, **Shunn Manuscript Format** (industry-standard submission format), Ebook Flow, and **Normseiten** (German standard pages: 60 characters per line, 30 lines per page, exact page count) set fonts, spacing, and margins; a one-click Shunn toggle for DOCX and PDF.
- **Exposé** — a per-book pitch document with its own editor, live character and Normseiten counts against limits you set, and a Normseiten DOCX export.
- Chapter-level selection with select-all / select-none, optional title page, custom title and author.
- Extensions can contribute additional formats and presets.

### Version control

- Built-in **Git** client — stage, commit, push, pull from the app; branch and changed-file count in the status bar.
- Per-scene snapshot history is complementary to Git for fine-grained, per-file recovery.

### Find & Replace

- Plain-text, whole-word, case-sensitive, or .NET regex search.
- Scope to the current scene, the selection, the active book, or every book in the project.
- Replace one or all matches; snapshots cover the replacements.

### Customization

- **Hotkeys** — every action is rebindable; defaults documented in [`docs/manual/26-hotkeys.md`](docs/manual/26-hotkeys.md).
- **Command Palette** (`Ctrl+Shift+P`) — every action by name.
- **Localization** — drop-in JSON locale files; English, German, and Simplified Chinese ship in the box.
- **Theme** — light/dark following the OS, plus bundled Discord and Catppuccin Mocha palettes and a custom accent color; on macOS 26+ the window uses native Liquid Glass, with a vibrancy fallback on older macOS.
- **Global or per-project settings** — appearance, editor, and writing-assistance settings default to global but can be overridden per project (e.g. an English book and a German book each with their own language, quotes, and theme); project overrides live in `.novalist/` and sync via git.
- **Book preview** — render the editor as a printed page with configurable trim size and book font.

### Extension system

Novalist has a plugin architecture through the **Novalist SDK**. Extensions can contribute:

- Webview panels that appear as activity-bar view icons under Extensions (SDK v2)
- Editor hooks (lifecycle, inline actions, grammar checks)
- Export formats
- Custom entity types and custom property types
- AI integration hooks (prompt building, response processing)
- Themes

Extensions are .NET 8 class libraries loaded by the core process at runtime from the user extensions folder; their UI is delivered as sandboxed webviews. The bundled AI Assistant is the reference example, contributing the AI Chat, Character Chat, and Story Analysis panels. See the `Novalist.Sdk.Example` project for a working implementation.

The Extensions view has a built-in **store**: browse the online extension gallery, read each extension's README and release notes, and install or update extensions in place (with download progress and a cancel option) without leaving the app. It can also check installed extensions for updates on startup.

## Building

Novalist is an Electron + React front end over a bundled .NET 8 core process (`Novalist.Backend`).

```
# build the backend the app spawns, then run the app in development
dotnet build Novalist.Backend/Novalist.Backend.csproj
cd app
npm install
npm run dev
```

To produce distributable installers for the current platform:

```
cd app
npm run package
```

## Project structure

```
app/                  Electron + React front end — renderer, main, preload
Novalist.Backend      .NET 8 core process the app spawns (JSON-RPC over stdio)
Novalist.Core         Core library — models, services, serialization, localization, utilities
Novalist.Sdk          Extension SDK — public interfaces, hooks, host-service contracts, descriptor models
Novalist.Sdk.Example  Reference extension demonstrating the hook types
docs/                 User manual, extension guide, screenshots
```

## Support the Project

If you find Novalist useful and want to support its development:

[<img src="https://www.paypalobjects.com/en_US/i/btn/btn_donate_LG.gif" alt="Donate with PayPal" />](https://www.paypal.com/donate/?hosted_button_id=EQJG5JHAKYU4S)

[Buy me a coffee on Ko-fi](https://ko-fi.com/drommedhar)

## License

[MIT](LICENSE)
