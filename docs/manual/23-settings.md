# Settings

Settings is where you configure Novalist's appearance, the editor, writing goals, writing assistance, entity templates, keyboard shortcuts, updates and integrations, diagnostics, and installed extensions.

## Opening Settings

In the binder's view rail (below the chapter tree), click **Settings** in the **Application** group, or use the command palette (`Ctrl+Shift+P`, `Cmd` on macOS) and pick "Settings". Settings opens in the main area like any other view.

## Finding a setting

The Settings view has a **search box** in the header and a **category navigation** rail down the left side.

- **Search** — type a word (for example `grammar`, `deadline`, `accent`, `github`) to hide every card that does not match its title or keywords, so only the relevant sections remain.
- **Category rail** — click a category name (Appearance, Editor, Writing Goals, and so on) to jump straight to that card.

## Global vs project settings

The **Appearance**, **Editor**, and **Writing Assistance** cards each have an **Override for this project** checkbox at the top. It appears only while a project is open.

- **Off (default)** — that section uses your global settings, shared by every project.
- **On** — the section's values are saved with the project, in its `.novalist` folder, and override the global values whenever this project is open. Because they live inside the project folder, the overrides travel with the project through Git and across devices.

Switching a section back to global clears that section's overrides and the values revert to your global settings immediately.

This lets you, for example, keep one book in English with English quotation marks and another in German with German quotes and a German interface, without changing your global defaults. Switching projects re-applies the effective theme, accent color, and interface language for the project you open.

The **Templates** and **Writing Goals** sections are per-project (they only apply while a project is open). **Hotkeys**, **Updates & Integrations**, **Diagnostics**, and **Extensions** are always global.

## Appearance

- **Interface Language** — UI language. English, German, and Chinese (Simplified) ship built in. Changes apply immediately without restart.
- **Theme** — the active color scheme:
  - **Default** — follows your operating system's light/dark preference.
  - **Discord**
  - **Catppuccin Mocha**
- **Accent Color** — pick a custom highlight color used throughout the interface. A **Reset** button next to the color picker clears the custom accent and returns to the theme's default.

On macOS 26 and later the window uses the native Liquid Glass material; older macOS versions get standard vibrancy.

## Editor

- **Font Family** — typeface used in the editor. Type any installed family; a list of common typefaces is offered as suggestions.
- **Font Size** — editor font size in pixels (8-36).
- **Typewriter Scrolling** — keeps the active line at a fixed vertical position so you don't write near the bottom of the page. When on, an anchor choice appears: **Top**, **Middle**, or **Bottom**.
- **Page View** — renders the editor as a printed-book-style page with paper background, margins, and shadow.
- **Book Paragraph Spacing** — adds extra vertical space between lines for a book-like reading experience.
- **Book Width** — constrains the editor's text column to a real trim size so you can preview how the manuscript will set on the page. When on, a sub-panel appears:
  - **Page Format** — US Trade (6x9), Digest (5.5x8.5), A5 (5.83x8.27), Mass Market (4.25x6.87), or **Custom**.
  - **Custom Width** — the text-block width in inches, shown only when Page Format is Custom.
  - **Book Font Family** and **Book Font Size** — the typeface and size used to measure the column.
  - A live **characters-per-line** estimate updates as you change the format, width, font, and size, so you can tune the layout to a target line length.

## Writing Goals

Per-project targets that feed the Dashboard's goal cards.

- **Project Deadline** — the date you aim to finish; pacing on the Dashboard is measured against it.
- **Author** — the author name stored with the project (also used as the default author when exporting).
- **Watch Filesystem** — when on, Novalist watches the project folder and reconciles scenes and chapters that are added, moved, renamed, or deleted with an external tool while the app is running.

Daily and project word goals themselves are edited on the [Dashboard](11-dashboard.md) by clicking a goal card's title.

## Writing Assistance

- **Quote Style** — the language preset for smart quotes, em-dashes, and ellipsis replacement as you type. Presets: `en`, `de-low`, `de-guillemet`, `fr`, `es`, `it`, `pt`, `ru`, `pl`, `cs`, `sk`. A preview line shows the exact replacements for the chosen preset.
- **Dialogue Punctuation Correction** — automatically corrects comma and period placement around quotation marks in dialogue, based on the selected quote style language.
- **Grammar & Spelling Check** — underlines grammar, spelling, and style issues in the editor using **LanguageTool**. Requires an internet connection (or a self-hosted LanguageTool server). When enabled, a configuration sub-panel appears:
  - **API URL** — the LanguageTool endpoint. Leave blank for the public endpoint, or point it at your own server (for example `https://api.languagetool.org/v2/check`).
  - **Username** and **API Key** — credentials for a LanguageTool Premium account; the API key field is masked. A **Get API key** link opens LanguageTool's access-token page in your browser.
  - **Picky Mode** — turns on LanguageTool's stricter style and typography rules.
  - **Mother Tongue** — your native language, which helps LanguageTool catch false-friend and interference errors. Choose "None" to skip it.

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

## Hotkeys

A full rebinding editor for every keyboard shortcut. Shortcuts are grouped by category (Navigation, Panels, Editor, General).

- **Search** — filter the list by action name, category, or current gesture.
- **Rebind** — click a shortcut's gesture button; it starts capturing, and the next key combination you press becomes the new binding. Press `Escape` to cancel the capture.
- **Conflict detection** — if the combination you press is already used by another action, a conflict warning names the clashing action so you can pick something else.
- **Reset one** — the reset button next to a shortcut restores that action's factory default; it is enabled only for shortcuts you have changed.
- **Reset all** — restores every shortcut to its default.

The default bindings are listed in [Hotkeys](26-hotkeys.md).

## Updates & Integrations

- **Check for updates** — when on, Novalist checks for a newer application version on startup.
- **Check for extension updates** — when on, Novalist checks installed extensions for newer versions.
- **GitHub token** — a personal access token (masked) used for GitHub operations such as extension updates and authenticated Git remotes.

## Diagnostics

- **Diagnostic logging** — toggle, off by default. When on, Novalist writes a technical log to a file you can send for support (e.g. to report a bug we cannot reproduce).
  - **What it records:** app events, lifecycle and startup phases, settings state, and errors / stack traces.
  - **What it never records:** your story text, characters, locations, items, lore, scene or chapter titles, notes, or file names. The log is content-safe by design, and you can open and read the file before sending it.
- **Open Log Folder** — reveals the folder that holds the log files in your file manager.
- **Open Current Log** — opens today's log file so you can read it before sending it.
- **Clear Logs** — deletes the stored log files.

## Extensions

A read-only list of the extensions installed for Novalist. Each row shows the extension's name, its version, whether it is enabled or disabled, and a note if it failed to load. Install and manage extensions from the Extensions rail; see [Extensions](24-extensions.md).

## Where settings live

- **Global settings** — in your user app-data folder, per machine.
- **Project overrides** — in `<Project>/.novalist/`, versioned with the project.

## Tips

- **Switch theme by light.** Dark for evening sessions, light for daylight; with the Default theme this follows your OS automatically.
- **Disable grammar check if it slows you down.** It calls a remote API; some networks are slow enough that the underlines lag.
- **Use a self-hosted LanguageTool for offline use.** A `docker-compose` LanguageTool image takes minutes and removes the cloud dependency — point the API URL at it.
- **Preview your trim size early.** Turning on Book Width with your real page format and book font shows the characters-per-line you will actually get in print.

## Where to go next

- [Templates](07-templates.md) — how entity templates are applied.
- [Hotkeys](26-hotkeys.md) — every default shortcut and how to rebind them.
- [Localization](27-localization.md) — the bundled interface languages.
