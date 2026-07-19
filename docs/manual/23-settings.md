# Settings

Settings is where you configure Novalist's appearance, the editor, writing assistance, entity templates, and diagnostics.

## Opening Settings

In the binder's view rail (below the chapter tree), click **Settings** in the **Application** group, or use the command palette (`Ctrl+Shift+P`, `Cmd` on macOS) → "Settings". Settings opens in the main area like any other view.

## Global vs project settings

The **Appearance**, **Editor**, and **Writing Assistance** cards each have an **Override for this project** checkbox at the top. It appears only while a project is open.

- **Off (default)** — that section uses your global settings, shared by every project.
- **On** — the section's values are saved with the project, in its `.novalist` folder, and override the global values whenever this project is open. Because they live inside the project folder, the overrides travel with the project through Git and across devices.

Switching a section back to global clears that section's overrides and the values revert to your global settings immediately.

This lets you, for example, keep one book in English with English quotation marks and another in German with German quotes and a German interface, without changing your global defaults. Switching projects re-applies the effective theme, accent color, and interface language for the project you open.

The **Templates** section is always per-project (it only appears while a project is open). **Diagnostics** is always global.

## Appearance

- **Interface Language** — UI language. English, German, and Chinese (Simplified) ship built in. Changes apply immediately without restart.
- **Theme** — the active color scheme:
  - **Default** — follows your operating system's light/dark preference.
  - **Discord**
  - **Catppuccin Mocha**
- **Accent Color** — pick a custom highlight color used throughout the interface.

On macOS 26 and later the window uses the native Liquid Glass material; older macOS versions get standard vibrancy.

## Editor

- **Font Family** — typeface used in the editor.
- **Font Size** — editor font size in pixels (8–36).
- **Typewriter Scrolling** — keeps the active line at a fixed vertical position so you don't write near the bottom of the page. When on, an anchor choice appears: **Top**, **Middle**, or **Bottom**.
- **Page View** — renders the editor as a printed-book-style page with paper background, margins, and shadow.
- **Book Paragraph Spacing** — adds extra vertical space between lines for a book-like reading experience.

## Writing Assistance

- **Quote Style** — the language preset for smart quotes, em-dashes, and ellipsis replacement as you type. Presets: `en`, `de-low`, `de-guillemet`, `fr`, `es`, `it`, `pt`, `ru`, `pl`, `cs`, `sk`.
- **Dialogue Punctuation Correction** — automatically corrects comma and period placement around quotation marks in dialogue, based on the selected quote style language.
- **Grammar & Spelling Check** — underlines grammar, spelling, and style issues in the editor using **LanguageTool**. Requires an internet connection (or a self-hosted LanguageTool server).

## Templates

Per-entity-type template management, for Characters, Locations, Items, Lore, and every custom entity type. Each type lists its templates with **edit** and **delete** buttons plus an **Add template** button.

The template editor covers:

- **Template name**.
- **Known fields** — tick the built-in fields the template should include and give each a default value. For characters, the Age field can be a plain **number** or a **date** (birth date) with a unit of years, months, or days.
- **Custom fields** — extra key/default-value pairs.
- **Default custom properties** — typed properties (String, Int, Bool, Date, Enum, Timespan) with defaults; Enum properties take a comma-separated option list.
- **Sections** — pre-created sections with optional default content.
- **Options** — whether entities created from the template include images, relationships (characters and custom types), and per-chapter overrides (characters).

See [Templates](07-templates.md) for how templates are used when creating entities.

## Diagnostics

- **Diagnostic logging** — toggle, off by default. When on, Novalist writes a technical log to a file you can send for support (e.g. to report a bug we cannot reproduce).
  - **What it records:** app events, lifecycle and startup phases, settings state, and errors / stack traces.
  - **What it never records:** your story text, characters, locations, items, lore, scene or chapter titles, notes, or file names. The log is content-safe by design, and you can open and read the file before sending it.

## Moved elsewhere

- **Writing goals** (daily and project word goals) are edited directly on the [Dashboard](11-dashboard.md) — click a goal card's title.
- **Keyboard shortcuts** are listed in [Hotkeys](26-hotkeys.md).

## Where settings live

- **Global settings** — in your user app-data folder, per machine.
- **Project overrides** — in `<Project>/.novalist/`, versioned with the project.

## Tips

- **Switch theme by light.** Dark for evening sessions, light for daylight; with the Default theme this follows your OS automatically.
- **Disable grammar check if it slows you down.** It calls a remote API; some networks are slow enough that the underlines lag.
- **Use a self-hosted LanguageTool for offline use.** A `docker-compose` LanguageTool image takes minutes and removes the cloud dependency.

## Where to go next

- [Templates](07-templates.md) — how entity templates are applied.
- [Hotkeys](26-hotkeys.md) — every default shortcut.
- [Localization](27-localization.md) — the bundled interface languages.
