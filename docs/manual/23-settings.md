# Settings

Settings is where you configure Novalist's appearance, the editor, writing goals, writing assistance, entity templates, keyboard shortcuts, updates and integrations, diagnostics, and installed extensions.

## Opening Settings

In the activity bar (the icon rail on the far left), click **Settings** in the bottom block next to Extensions, or use the command palette (`Ctrl+Shift+P`, `Cmd` on macOS) and pick "Settings". Settings opens in the main area like any other view.

## Finding a setting

The Settings view has a **search box** in the header and a **category navigation** rail down the left side.

- **Search** — type a word (for example `grammar`, `deadline`, `accent`, `github`) to hide every card that does not match its title or keywords, so only the relevant sections remain.
- **Category rail** — click a category name (Appearance, Editor, Writing Goals, and so on) to jump straight to that card.

## Global vs project settings

The **Appearance**, **Editor**, and **Writing Assistance** cards each have an **Override for this project** checkbox at the top. It appears only while a project is open.

- **Off (default)** — that section uses your global settings, shared by every project. The controls stay editable, and what you change is your global defaults; a line under the checkbox says so, so it is never ambiguous which one you are editing.
- **On** — the section's values are saved with the project, in its `.novalist` folder, and override the global values whenever this project is open. Because they live inside the project folder, the overrides travel with the project through Git and across devices.

Ticking the checkbox pins the section's **current** values to the project straight away, so the project keeps looking exactly as it does at that moment and later changes to your global defaults no longer reach it. You do not have to edit a field for the override to exist.

Unticking clears that section's overrides, and the values revert to your **global** settings immediately.

The checkbox reflects what is actually stored with the project, so it reads the same every time you open Settings.

This lets you, for example, keep one book in English with English quotation marks and another in German with German quotes and a German interface, without changing your global defaults. Switching projects re-applies the effective theme, accent color, and interface language for the project you open.

The **Templates** and **Writing Goals** sections are per-project (they only apply while a project is open). **Hotkeys**, **Updates & Integrations**, **Diagnostics**, and **Extensions** are always global.

## Appearance

- **Interface Language** — UI language. English, German, and Chinese (Simplified) ship built in, and any language you dropped into your `Locales/` folder is listed too. Changes apply immediately without restart.
- **Theme** — the active color scheme. Built-in themes come first, then your own, then any contributed by extensions:
  - **Default** — Novalist's own identity. A deep "Ink Night" page with parchment-coloured text and gilt accents; panels are the one raised surface, edges are hairlines rather than grey rules, and primary buttons carry a gold foil fill. Headings are set in Fraunces, text and interface labels in Newsreader, and anything the app computed for you — word counts, versions, file sizes, timestamps — in Courier Prime. All three typefaces ship inside the app, so the interface looks the same offline and on a machine that has none of them installed.
  - **Discord**
  - **High Contrast** — pure black behind pure white text, with borders you can actually see. Built for low vision and for working in a bright room; every text-on-background pair in it clears WCAG AAA, which the other palettes do not attempt.
  - **Catppuccin Mocha**
- **Accent Color** — pick a custom highlight color used throughout the interface. A **Reset** button next to the color picker clears the custom accent and returns to the theme's default. A custom accent also flattens the gold foil on primary buttons to a single fill in your colour.
- **Themes folder** / **Languages folder** — open the folders you drop your own colour schemes and interface languages into. **Rescan** in the Language packs panel picks up what you dropped in without a restart. See [Custom themes & language packs](34-custom-themes-and-languages.md) for the file formats.

The alternate themes change colour only. Type, spacing, and corner radii belong to the Novalist identity and stay the same whichever palette you pick — that holds for themes you write yourself as well.

On macOS 26 and later the window uses the native Liquid Glass material; older macOS versions get standard vibrancy.

## Editor

- **Font Family** — typeface used in the editor. Defaults to **Newsreader**, which ships with the app. Type any installed family; a list of common typefaces is offered as suggestions, with the three bundled faces (Newsreader, Fraunces, Courier Prime) listed first because those are guaranteed to be present.
- **Font Size** — editor font size in pixels (8-36). Defaults to 17.
- **Line Height** — the space between lines, as a multiple of the font size (1-2.5). Defaults to 1.7. Opening the lines up is the single most effective change for lines that are hard to track, and no theme can do it — themes are colour only.
- **Letter Spacing** — extra space between letters in pixels (-1 to 4). Defaults to 0, which leaves the typeface exactly as its designer set it.
- **Paragraph Spacing** — how large a gap **Book Paragraph Spacing** inserts between paragraphs, in ems (0-3). Defaults to 0.75.

All three apply to the scene editor, to Manuscript mode, and to the Expose.
- **Typewriter Scrolling** — keeps the active line at a fixed vertical position so you don't write near the bottom of the page. When on, an anchor choice appears: **Top**, **Middle**, or **Bottom**.
- **Page View** — renders the editor as a printed-book-style page with paper background, margins, and shadow.
- **Book Paragraph Spacing** — adds extra vertical space between lines for a book-like reading experience.
- **Book Width** — constrains the editor's text column to a real trim size so you can preview how the manuscript will set on the page. When on, a sub-panel appears:
  - **Page Format** — US Trade (6x9), Digest (5.5x8.5), A5 (5.83x8.27), Mass Market (4.25x6.87), or **Custom**.
  - **Custom Width** — the text-block width in inches, shown only when Page Format is Custom.
  - **Book Font Family** and **Book Font Size** — the typeface and size used to measure the column.
  - A live **characters-per-line** estimate updates as you change the format, width, font, and size, so you can tune the layout to a target line length.

### Editor extras

- **Dim other paragraphs while writing** — fades every paragraph but the one your caret is in. See [Focus mode](05-editor.md#focus-mode).

## Writing Goals

Per-project targets that feed the Dashboard's goal cards.

- **Project Deadline** — the date you aim to finish; pacing on the Dashboard is measured against it.
- **Author** — the author name stored with the project (also used as the default author when exporting).
- **Daily Word Goal** — how many new words a day you are aiming for.
- **Project Word Goal** — the total word count you are aiming at for the whole project.
- **Word targets** — the same panel the [Dashboard](11-dashboard.md#word-targets) shows: every [target](04-chapters-and-scenes.md#word-targets) you have set on an act, chapter, or scene with its progress, and a drop-down to set a new one.

The two word goals can also be edited on the Dashboard by clicking a goal card's title; it is the same setting either way.

## Scene Labels

The labels a scene in the open book can carry: a name and a colour each. See [Scene labels](10-manuscript.md#scene-labels) for how they are used.

Labels live with the book, like the scene stages. Removing one takes it off the scenes carrying it.

## Scene and Chapter Fields

Fields of your own on every scene or every chapter of the open book, beyond the fixed set Novalist ships. Each has a label, a scope (scene or chapter) and a type:

- **Text** - free text.
- **Number** - sorts and totals as a number.
- **Yes / no** - a checkbox.
- **Date** - a date picker.
- **One of a list** - a drop-down of choices you type in, comma separated.

Scene fields can be ticked **Show as a column in the outliner** to appear in the [Manuscript outliner](10-manuscript.md#your-own-scene-and-chapter-fields), editable in place. They are always editable in the scene notes dock under the editor; chapter fields are edited in the Chapter dialog.

Definitions live with the book, not globally - the things worth tracking in a thriller and in a short-story collection are rarely the same list. **Removing a field removes the values you filled in for it**, in every scene and chapter.

## Writing Assistance

- **Quote Style** — the language preset for smart quotes, em-dashes, and ellipsis replacement as you type. Presets: `en`, `de-low`, `de-guillemet`, `fr`, `es`, `it`, `pt`, `ru`, `pl`, `cs`, `sk`. A preview line shows the exact replacements for the chosen preset.
- **Dialogue Punctuation Correction** — automatically corrects comma and period placement around quotation marks in dialogue, based on the selected quote style language.
- **Check spelling as I write** — underlines misspellings using the spell checker your operating system already has. On by default. Needs no server and no account, works offline, and nothing you write leaves the machine. When enabled, a sub-panel appears:
  - **Dictionaries** — the language tags this build can load. Leave them all unticked to follow your Quote Style language, or tick several to check against more than one at once. On macOS the list is empty and hidden, because the system checker decides for itself. On Windows and Linux a dictionary is downloaded once, the first time it is used, and works offline afterwards.
  - **Your dictionary** — the words you added by right-clicking a red-underlined word in your prose and choosing **Add to dictionary**. They are stored with your settings rather than in the system dictionary, so they follow you to another machine. Each has a delete button to forget it again.
  - **Codex names are never flagged.** Every character name, surname and alias, and every location, item and lore entry in the open book, is given to the spell checker automatically. A secondary-world manuscript is otherwise a wall of red underlines despite the Codex holding every one of those names. They are not added to your dictionary — they follow the Codex, so renaming a character stops the old spelling being accepted.
- **Grammar & Spelling Check** — underlines grammar, spelling, and style issues in the editor using **LanguageTool**. Unlike spell check, this one requires an internet connection (or a self-hosted LanguageTool server). When enabled, a configuration sub-panel appears:
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

A read-only list of the extensions installed for Novalist. Each row shows the extension's name, its version, whether it is enabled or disabled, and a note if it failed to load. Install and manage extensions from the Extensions view; see [Extensions](24-extensions.md).

## Where settings live

- **Global settings** — in your user app-data folder, per machine.
- **Project overrides** — in `<Project>/.novalist/`, versioned with the project.

## Tips

- **Set the editor font to taste, not to the theme.** The interface keeps the identity's faces whatever you choose, so picking your own writing typeface in Editor → Font Family changes only the page you write on.
- **Disable grammar check if it slows you down.** It calls a remote API; some networks are slow enough that the underlines lag. Spell check is unaffected — it runs locally and stays on.
- **Use a self-hosted LanguageTool for offline use.** A `docker-compose` LanguageTool image takes minutes and removes the cloud dependency — point the API URL at it.
- **Preview your trim size early.** Turning on Book Width with your real page format and book font shows the characters-per-line you will actually get in print.

## Where to go next

- [Templates](07-templates.md) — how entity templates are applied.
- [Hotkeys](26-hotkeys.md) — every default shortcut and how to rebind them.
- [Localization](27-localization.md) — the bundled interface languages.
