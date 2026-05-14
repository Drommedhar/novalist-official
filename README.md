<p align="center">
  <img src="Novalist.Desktop/splash.png" alt="Novalist" width="400" />
</p>

<h3 align="center">A desktop novel-writing application for authors who want to stay organized.</h3>

---

> **Disclaimer**
>
> Novalist Standalone is provided as is. It was originally developed to help me write my book and may occasionally be updated from my internal version. However, there is no guarantee of ongoing maintenance of the project. Users are free to open pull requests to be merged into the repository or fork it to customise it to their liking.

---

## What is Novalist?

Novalist is an offline-first desktop application for writing novels. It handles the full scope of a writing project — manuscript editing, worldbuilding, plotting, timelines, exporting, and version control — in a single, self-contained tool. It runs on Windows, macOS, and Linux on top of .NET 8 and Avalonia.

Rather than scattering notes across separate apps, browser tabs, and Markdown files, Novalist keeps everything about a project in one folder of plain files: chapters and scenes as HTML, entities and metadata as JSON, images and research alongside. The folder is yours — back it up, sync it, version-control it, edit it with any text editor when Novalist is closed.

## Documentation

A full **User Manual** lives in [`docs/manual/`](docs/manual/README.md). It covers every feature in detail with cross-linked pages for getting started, the interface, projects and books, the editor, worldbuilding, plotting, exporting, extensions, hotkeys, and troubleshooting.

For extension authors, the [Extension Guide](docs/extension-guide.md) walks through the SDK, hooks, packaging, and store submission.

## Features

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

### Worldbuilding (Codex)

- **Characters** with name/surname, gender, age (manual or computed from birth date and the in-world calendar), role, group, physical traits, images, relationships, and per-act / per-chapter / per-scene overrides for any field.
- **Locations** with hierarchical parents, types, and custom fields.
- **Items** with origin, type, and description.
- **Lore** entries for magic systems, religions, history, in-world books.
- **Custom entity types** — define your own (Factions, Spells, Vehicles, Races, …) with custom field schemas.
- **Templates** per entity type with default sections, custom properties, and field defaults.
- **Sections** of long-form Markdown content per entity.
- **Relationships** with auto-learned inverse-role pairs and inverse-prompting on new relationships.
- **Focus peek** card on entity hover inside the editor.

### Planning & visualization

- **Plot Grid** — spreadsheet view of plotlines (rows) by scenes (columns); toggle scene membership in a thread with a click.
- **Timeline** — chronological view of acts, chapters, scenes, and manual events with vertical/horizontal layout and day/week/month/year zoom.
- **In-world Calendar** — full Gregorian or fully custom calendar (custom month names, days per month, weekday names, year label) with Week, Month, and Year views.
- **Relationships graph** — auto-clustered force-directed graph of characters with family detection in English and German.
- **Manuscript view** — read the whole book end-to-end, switch to Corkboard for index-card planning, or Outliner for a sortable scene table.
- **Maps** — interactive layered map view: multi-layer image stacks with per-layer opacity, lock, zoom-range visibility, and floor-stack mode; per-image rotate, resize, polygon clip mask; entity-linked colour pins with screen-space rendering at any zoom.
- **Dashboard** — totals, status breakdown, chapter pacing, echo phrases, daily / project word goals with deadlines.

### Research & assets

- **Research view** — notes, links, files, images, and PDFs attached to the project with tags and search.
- **Image Gallery** — every project image at a glance with lazy thumbnails, search, and copy-path / reveal actions.
- Add images from file, clipboard, URL, or the existing project gallery.

### Output

- Export to **EPUB**, **DOCX**, **PDF**, **Markdown**, **Final Draft / Fountain**, **LaTeX**, and **Codex Markdown**.
- Built-in **Shunn Modern Manuscript Format** preset for submissions.
- Chapter-level selection, optional title page, custom title and author.
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
- **Localization** — drop-in JSON locale files; English and German ship in the box.
- **Theme** — system / light / dark with a custom accent color.
- **Book preview** — render the editor as a printed page with configurable trim size and book font.

### Extension system

Novalist has a plugin architecture through the **Novalist SDK**. Extensions can contribute:

- Ribbon and status-bar items
- Sidebar and context-sidebar tabs
- Full-screen content views with activity-bar icons
- Settings categories and pages
- Hotkeys
- Editor hooks (lifecycle, inline actions, grammar checks)
- Context-menu items
- Export formats and presets
- Themes
- Custom entity types and custom property types
- AI integration hooks (prompt building, response processing)

Extensions are .NET 8 class libraries discovered at runtime from the user extensions folder. See the [Extension Guide](docs/extension-guide.md) and the bundled `Novalist.Sdk.Example` project for a working reference implementation.

## Building

```
dotnet build Novalist.Desktop/Novalist.Desktop.csproj
```

To run a Release build:

```
dotnet run --project Novalist.Desktop/Novalist.Desktop.csproj -c Release
```

## Project structure

```
Novalist.Desktop      Desktop application — views, view models, dialogs, editor extensions
Novalist.Core         Core library — models, services, serialization, localization, utilities
Novalist.Sdk          Extension SDK — public interfaces, hooks, host-service contracts, descriptor models
Novalist.Sdk.Example  Reference extension demonstrating 11 hook types
docs/                 User manual, extension guide, gallery images
```

## Support the Project

If you find Novalist useful and want to support its development:

[<img src="https://www.paypalobjects.com/en_US/i/btn/btn_donate_LG.gif" alt="Donate with PayPal" />](https://www.paypal.com/donate/?hosted_button_id=EQJG5JHAKYU4S)

[Buy me a coffee on Ko-fi](https://ko-fi.com/drommedhar)

## License

[MIT](LICENSE)
