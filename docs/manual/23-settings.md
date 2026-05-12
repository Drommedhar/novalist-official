# Settings

Settings is where you configure Novalist's appearance, the editor, writing assistance, writing goals, default templates, hotkeys, and integrations.

Settings are stored in your user app-data folder and apply to every project you open. A subset (templates, calendar, project name) are per-project; those appear next to their per-project counterparts.

## Opening Settings

- Click the **gear** icon in the activity bar.
- Or **Start menu → Settings** (open the start menu from the hamburger button at the far left of the app bar).
- Or the command palette → "Settings".

Settings opens as a full-window overlay. Click **Close** or press `Escape` to dismiss.

A search box at the top of the Settings overlay lets you find a setting by name across all categories.

## Categories

The Settings overlay is divided into the following sections.

### Appearance

- **Language** — UI language. Discovered from the `Assets/Locales/*.json` files (English and German ship by default). The display name comes from each file's `language.name` key. Changes apply immediately without restart.
- **Theme** — system / light / dark.
- **Accent color** — pick a custom accent or leave it on the theme default. The hex string is stored in `AppSettings.AccentColor`.

### Editor

- **Editor font family** — typeface used in the editor when book preview is off. Defaults to **Inter**.
- **Editor font size** — point size. Default 14.
- **Enable book paragraph spacing** — when on, the editor renders paragraphs the way a printed book would (first-line indents, tighter spacing).
- **Enable book width** — when on, constrains the column to a printed-page width.
- **Book page format** — choice of trim sizes. Default is **US Trade 6×9**. Other options include Digest (5.5×8.5), A5, Mass Market, and a Custom size.
- **Book text-block width** — optional manual override of the text-block width within the page.
- **Book font family** — typeface used in book preview / book export. Defaults to **Times New Roman**.
- **Book font size** — book-preview point size. Default 11.

### Writing Goals

- **Daily word goal** — integer. Drives the daily-goal progress bar in the status bar. Reset at local midnight.
- **Project word goal** — integer. Drives the project-goal progress bar.
- **Project deadline** — optional date. When set, the dashboard computes days remaining and a suggested daily pace.

### Writing Assistance

- **Auto-replacement language** — preset that decides how straight quotes, dashes, and ellipses are converted. Options: English, German (low), German (guillemet), French, Spanish, Italian, Portuguese, Russian, Polish, Czech, Slovak. Picking a preset replaces the auto-replacement table with that language's defaults.
- **Auto-replacement table** — editable list of `start`/`end` patterns and their `startReplace`/`endReplace` substitutions. Add custom replacements if you have specific quotation conventions.
- **Dialogue Punctuation Correction** — toggle. When on, dialogue punctuation is auto-corrected as you type.
- **Grammar & Spelling Check** — toggle. When on, the editor underlines grammar and spelling issues via a LanguageTool-compatible API.
- **Grammar check API URL** — optional override. Leave empty to use the free public LanguageTool API. Provide a URL like `http://localhost:8081/v2/check` for a self-hosted instance.

### Templates

Per-entity-type template management. For each of Character, Location, Item, Lore, and each custom entity type:

- A list of available templates with **Edit** and **Delete** actions.
- A **+New template** button.

See [Templates](07-templates.md) for the template editor itself.

### Hotkeys / Keyboard Shortcuts

A searchable grid of every registered action with:

- **Action label** — e.g. "Toggle Focus Mode", "Add Comment", "Open Codex".
- **Category** — e.g. "Editor", "Navigation", "Panels".
- **Default binding** — the shipped hotkey.
- **Current binding** — your override, if any.

To rebind, click an action's binding and press the new key combination. Click the **×** next to a binding to clear it back to default.

See [Hotkeys](26-hotkeys.md) for the full list of defaults.

### Updates & Integrations

- **Check for updates** — toggle. When on, Novalist checks for new releases on startup.
- **Check for extension updates** — toggle.
- **GitHub Personal Access Token** — optional. Increases the extension gallery API rate limit from 60 to 5000 requests per hour. Stored locally; never sent anywhere other than GitHub's public API.

### Extension settings

Each installed extension that contributes settings appears as its own category at the bottom of the Settings overlay. The category name and icon are chosen by the extension.

## Per-project settings

A small set of settings are project-scoped rather than app-scoped, stored in `<Project>/.novalist/settings.json`:

- **Author name** for exports.
- **Project default templates** (when distinct from the global ones).

## Where settings live

- **App-level** — `%APPDATA%/Novalist/` on Windows, `~/Library/Application Support/Novalist/` on macOS, `~/.config/Novalist/` on Linux.
- **Project-level** — `<Project>/.novalist/`.
- **Hotkey overrides** — `AppSettings.HotkeyBindings` (app-level).
- **Recent projects** — `AppSettings.RecentProjects` (app-level).
- **Window state** — width, height, position, maximized (app-level).

## Tips

- **Set the daily goal small at first.** A daily goal you hit eight days out of ten is better than one you hit twice a month.
- **Switch theme by light.** Dark mode for evening sessions, light for daylight; the eyes will thank you.
- **Disable grammar check if it slows you down.** It calls a remote API; some networks are slow enough that the underlines lag.
- **Use a self-hosted LanguageTool for offline use.** A `docker-compose` LanguageTool image takes minutes and removes the cloud dependency.

## Where to go next

- [Hotkeys](26-hotkeys.md) — every default shortcut.
- [Extensions](24-extensions.md) — extension contributions appear here.
- [Localization](27-localization.md) — adding new UI languages.
