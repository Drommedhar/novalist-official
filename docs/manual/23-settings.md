# Settings

Settings is where you configure Novalist's appearance, the editor, writing goals, writing assistance, entity templates, keyboard shortcuts, updates and integrations, diagnostics, and installed extensions.

## Opening Settings

**Go → Settings** in the menu bar, or the command palette (`Ctrl+Shift+P`, `Cmd` on macOS). Settings opens in the main area like any other view.

Settings is not on the mode rail. It is application-scoped — a preference is about your installation rather than about a book — so its home is the menu bar, and controls elsewhere in the app can still deep-link into the section they belong to.

## Finding a setting

Settings shows **one section at a time**. The rail down the left side groups every section under four headings, and the header carries a search box.

| Category | What lives there |
| --- | --- |
| **General** | Appearance, Accessibility, Keyboard shortcuts, Theme tokens |
| **Writing** | Editor, Writing assistance |
| **Project** | Writing goals, Word completion, Templates, Scene stages, Scene labels, Groups and factions, Scene templates, Tags, Scene and chapter fields |
| **System** | Backups, Updates and integrations, Language packs, Diagnostics, Extensions |

- **Search** — type a word (for example `grammar`, `deadline`, `accent`, `github`) and the results name the **individual controls** that match, not just the sections holding them. Picking a result opens its section and takes you to the control itself. If nothing matches, Settings says so rather than showing you an empty page.
- **Category rail** — pick a section to open it. Only that section is on screen, so a long page of unrelated controls is no longer between you and the one you came for.

Settings opens **without a project**, so your global preferences are reachable the moment Novalist starts. Controls that need a project are visibly disabled rather than missing, so you can see what a project would give you.

When you reach Settings from a control elsewhere — the editor's **More writing settings**, for example — the header carries a **Back to...** link that returns you to the view you came from.

## Global vs project settings

The **Appearance**, **Editor**, and **Writing Assistance** sections each have an **Override for this project** checkbox at the top. It appears only while a project is open.

Every section states its reach with a badge, so you never have to remember which is which:

| Badge | Meaning |
| --- | --- |
| **All projects** | The section is global. What you change here applies everywhere. |
| **Project only** | The section belongs to the open project and does not exist without one. |
| **Global default** | The section can be pinned to a project and currently is not, so you are editing your defaults. |
| **Overridden here** | The section is pinned to the open project; your global defaults are untouched. |

- **Off (default)** — that section uses your global settings, shared by every project. The controls stay editable, and what you change is your global defaults; a line under the checkbox says so, so it is never ambiguous which one you are editing.
- **On** — the section's values are saved with the project, in its `.novalist` folder, and override the global values whenever this project is open. Because they live inside the project folder, the overrides travel with the project through Git and across devices.

Ticking the checkbox pins the section's **current** values to the project straight away, so the project keeps looking exactly as it does at that moment and later changes to your global defaults no longer reach it. You do not have to edit a field for the override to exist.

Unticking clears that section's overrides, and the values revert to your **global** settings immediately.

The checkbox reflects what is actually stored with the project, so it reads the same every time you open Settings.

This lets you, for example, keep one book in English with English quotation marks and another in German with German quotes and a German interface, without changing your global defaults. Switching projects re-applies the effective theme, accent color, and interface language for the project you open.

Everything under **Project** is per-project and only applies while a project is open. **Keyboard shortcuts**, **Updates and integrations**, **Backups**, **Language packs**, **Diagnostics**, and **Extensions** are always global.

## Appearance

- **Interface Language** — UI language. English, German, and Chinese (Simplified) ship built in, and any language you dropped into your `Locales/` folder is listed too. Changes apply immediately without restart.
- **Theme** — the active color scheme. Built-in themes come first, then your own, then any contributed by extensions:
  - **Default** — Novalist's own identity. A deep "Ink Night" page with parchment-coloured text and gilt accents; panels are the one raised surface, edges are hairlines rather than grey rules, and primary buttons carry a gold foil fill. Headings are set in Fraunces, text and interface labels in Newsreader, and anything the app computed for you — word counts, versions, file sizes, timestamps — in Courier Prime. All three typefaces ship inside the app, so the interface looks the same offline and on a machine that has none of them installed.
  - **Discord**
  - **High Contrast** — pure black behind pure white text, with borders you can actually see. Built for low vision and for working in a bright room; every text-on-background pair in it clears WCAG AAA, which the other palettes do not attempt.
  - **Catppuccin Mocha**
- **Interface size** — how large Novalist's own interface is drawn, from 75% to 150%, with **Reset to 100%** beside it. This is the whole interface: the toolbar, the binder, the inspector, dialogs and Settings itself. It is **not** the size of your manuscript — that is the editor's own font size, and the two no longer move together, so you can have small chrome around large prose or the reverse. **View → Increase / Decrease / Reset interface scale** change the same setting, so the menu and Settings never disagree about it, and the size you pick is remembered. Those three ship without keyboard shortcuts; give them ones you like in **Keyboard shortcuts**.

  Your operating system's own display scaling is still respected on top of this. Interface size is the adjustment you make when the OS setting is right for everything else and Novalist alone is too small or too large.
- **Accent Color** — pick a custom highlight color used throughout the interface. A **Reset** button next to the color picker clears the custom accent and returns to the theme's default. A custom accent also flattens the gold foil on primary buttons to a single fill in your colour.
- **Themes folder** / **Languages folder** — open the folders you drop your own colour schemes and interface languages into. **Rescan** in the Language packs panel picks up what you dropped in without a restart. See [Custom themes & language packs](34-custom-themes-and-languages.md) for the file formats.

The alternate themes change colour only. Type, spacing, and corner radii belong to the Novalist identity and stay the same whichever palette you pick — that holds for themes you write yourself as well. If you want to change them anyway, **Theme tokens** below is where you do it.

## Theme tokens

Appearance used to offer a theme, an accent colour and two folder buttons. Anything else — a surface a shade darker, a slightly larger body size, squarer corners — meant hand-writing a JSON token map or a `.css` file and restarting to see the result.

**Theme tokens** edits the same values in the app, grouped into Surfaces, Text, Accent, Type sizes, and Corners and spacing. Changes apply as you make them, and they sit **on top of** whichever theme is selected: switch theme and your overrides come with you, so a colour you chose is not lost the moment you try another palette.

- A token you have not touched simply follows the theme; the field shows what the theme currently resolves to.
- The **reset arrow** beside a token you have changed puts it back to the theme's value. It only appears once there is something to undo.
- **Reset everything** clears the lot.
- The **Profile** box is the whole override set as JSON. Copy it to keep a look, or to send it to somebody; paste one in and press **Apply pasted profile** to adopt it. Only tokens Novalist knows are read from a pasted profile, so a profile from a different version cannot reach anything else.

The list is deliberately short. `tokens.css` declares over a hundred values, most of them derived or internal, and a wall of a hundred colour pickers is not an editor — it is a way of breaking the interface by accident.


On macOS 26 and later the window uses the native Liquid Glass material; older macOS versions get standard vibrancy.

## Editor

- **Font Family** — typeface used in the editor. Defaults to **Newsreader**, which ships with the app. Type any installed family; a list of common typefaces is offered as suggestions, with the three bundled faces (Newsreader, Fraunces, Courier Prime) listed first because those are guaranteed to be present.
- **Font Size** — editor font size in pixels (8-36). Defaults to 17.
- **Line Height** — the space between lines, as a multiple of the font size (1-2.5). Defaults to 1.7. Opening the lines up is the single most effective change for lines that are hard to track, and no theme can do it — themes are colour only.
- **Letter Spacing** — extra space between letters in pixels (-1 to 4). Defaults to 0, which leaves the typeface exactly as its designer set it.
- **Paragraph Spacing** — how large a gap **Book Paragraph Spacing** inserts between paragraphs, in ems (0-3). Defaults to 0.75.

All three apply to the scene editor, to Manuscript mode, and to the Expose.

- **Mark hard-to-read sentences** — tints difficult and very difficult sentences in the editor. Toggled from the gauge button on the editor toolbar; the setting is where it is remembered. See [Readability marking](05-editor.md#readability-marking).
- **Read-aloud speed** — how fast the speaker button reads the scene back (0.5-2). Defaults to 1.
- **Read-aloud voice** — which system voice to use. The default, **Match the writing language**, asks for a voice in the language the scene is written in.

  On Windows the list comes from **the system speech engine**, so it holds every voice your machine can speak with — including any you have added with a third-party voice adapter. Elsewhere it is the browser's own list.

  **If your list is short and a voice you installed is missing**, Windows has two voice stores and two kinds of voice:

  - Voices added under **Accessibility → Narrator → Add natural voices** are downloaded as app packages. On their own they are reserved for Narrator and appear in no application. A third-party adapter such as *NaturalVoiceSAPIAdapter* republishes them to the system speech engine, and Novalist picks them up from there.
  - Voices that arrive with a **language** — **Time & language → Language & region → Add a language**, with the optional **Speech** feature ticked — are available to everything without help.

  Restart Novalist after installing either kind.

## Writing Goals

Per-project targets that feed the Dashboard's goal cards.

- **Project Deadline** — the date you aim to finish; pacing on the Dashboard is measured against it.
- **Author** — the author name stored with the project (also used as the default author when exporting).
- **Daily Word Goal** — how many new words a day you are aiming for.
- **Weekly Word Goal** — words for the calendar week, Monday to Sunday. Leave it empty for none. Worth setting if you write a few heavy days rather than a little every day: a daily goal marks you down four days in seven for being exactly on schedule.
- **Monthly Word Goal** — words for the calendar month. Leave it empty for none.
- **Project Word Goal** — the total word count you are aiming at for the whole project.
- **Word targets** — the same panel the [Dashboard](11-dashboard.md#word-targets) shows: every [target](04-chapters-and-scenes.md#word-targets) you have set on an act, chapter, or scene with its progress, and a drop-down to set a new one.

The two word goals can also be edited on the Dashboard by clicking a goal card's title; it is the same setting either way.

## Word completion

Novalist's only completion was the **@-mention** picker over Codex names, in scene prose and nowhere else. That leaves out everything a secondary world is full of and the Codex is not: a settled spelling of a place, a rank, a coined verb, a phrase that has to read the same way every time. Those get retyped, and retyped slightly differently, and the inconsistency surfaces in copy-edit.

Put one word or phrase per line in the box. Type three characters of one anywhere in the editor and the rest is offered.

- **Tab** accepts the highlighted suggestion, **up/down** move through them, **Escape** dismisses. **Enter is deliberately not used** — in prose it starts a paragraph, and a popup that swallows it would be worse than no completion at all.
- **Characters before suggesting** decides how much you have to type first. Three is the minimum: two characters match half the list, which makes the popup something to dismiss rather than something to use.
- **Add every Codex name** pours your cast, places, items, lore and custom entries into the list in one go, so the names complete outside scene prose too. Running it twice does not double anything, and what you typed yourself stays first.

The list belongs to the book, so it travels with the project rather than following the machine you work on.

## Tags

Every tag in the project, wherever it is used: on scenes, on Codex entries, and on research notes. They were three unrelated lists before this — a tag on a scene and the same word on a character were not the same tag, could not be counted, and could not be renamed together.

Each row shows the tag, a colour swatch, and how many scenes, entries and notes carry it.

- **Colour** — click the swatch. A tag can be coloured before anything uses it, which is how a vocabulary gets planned rather than accumulated.
- **Rename or merge** — click the tag's name. Typing a new name renames it everywhere at once. Typing the name of a tag that **already exists** merges the two: everything that carried either now carries one, and nothing carries it twice.
- **Remove** — takes the tag off everything that has it. This cannot be undone from here.

Renaming and merging reach scenes, Codex entries and research notes in the same pass, which is the point: a rename that fixed the scenes and left the Codex spelling it the old way is exactly the drift this replaces.

## Scene Labels

The labels a scene in the open book can carry: a name and a colour each. See [Scene labels](10-manuscript.md#scene-labels) for how they are used.

Labels live with the book, like the scene stages. Removing one takes it off the scenes carrying it.

## Your Own Fields

Fields of your own on the open book's scenes, chapters, plotlines, timeline events and research items, beyond the fixed set Novalist ships. Each has a label, a scope and a type:

- **Text** - free text.
- **Number** - sorts and totals as a number.
- **Yes / no** - a checkbox.
- **Date** - a date picker.
- **One of a list** - a drop-down of choices you type in, comma separated.

The scope decides where the field is asked for:

| Scope | Edited in |
| --- | --- |
| Scene | The scene notes dock, and the [Manuscript outliner](10-manuscript.md#your-own-scene-and-chapter-fields) when ticked as a column |
| Chapter | The Chapter dialog |
| Plotline | Right-click a plotline in the [Plot Grid](08-plot-grid.md) and pick **Your fields** |
| Timeline event | The event editor in the [Timeline](12-timeline.md), once the event exists |
| Research item | Under the item in the [Research](15-research.md) view |

Scene fields can be ticked **Show as a column in the outliner**, editable in place. A dozen fields is not a dozen columns anybody wants to read, so it is off by default.

The same key means different things in different scopes: a plotline's "status" and a research item's "status" are two separate questions and neither overwrites the other.

Definitions live with the book, not globally - the things worth tracking in a thriller and in a short-story collection are rarely the same list. **Removing a field removes the values you filled in for it**, everywhere it reached.

## Writing Assistance

- **Replace quotes and dashes as I type** — on by default. Untick it and nothing is substituted while you write: a straight quote stays straight, `--` stays two hyphens, `...` stays three dots. The preview line below says so instead of showing the pairs, and the two substitution rules in **Clean up the manuscript** are greyed out so a cleanup pass cannot put them back either. Turning it on again restores the preset you had — the pairs are kept, not discarded.
- **Quote Style** — the language preset for smart quotes, em-dashes, and ellipsis replacement as you type. Presets: `en`, `de-low`, `de-guillemet`, `fr`, `es`, `it`, `pt`, `ru`, `pl`, `cs`, `sk`. A preview line shows the exact replacements for the chosen preset, and choosing one rewrites the replacements to match. Setting it for a single book leaves your other books alone; clearing that override lets the book inherit yours again. It stays available with replacement switched off, because it is also the language your book is written in — export, grammar check, spell check and the statistics all read it.
- **Replacement rules** — the full list of what gets substituted as you type, in order, and yours to edit. Each rule is either **Text** (the characters you type) or **Pattern** (a regular expression, whose replacement can put back what it captured with `$1`). **Closing** is filled in only for a rule that should alternate, the way an opening quote alternates with a closing one. Picking a Quote Style above fills the list in with that language's rules; everything after that is up to you, and deleting them all sticks. An invalid pattern, or one that matches nothing, is refused as you enter it rather than saved and skipped. Each rule has a **Try it on** box that runs it against a sentence of your own as you write the rule, showing what the sentence becomes and what a pattern captured, and **How matching and replacement work** folds open into the full account with a reference for the pattern pieces. See [Auto-replacements](05-editor.md#your-own-rules) for the full description.
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

A rebinding editor listing **every command Novalist has**, not only the ones that ship with a gesture — so anything in the app can be given a shortcut, and most commands start unbound. Commands are grouped by category (Navigation, Panels, Editor, Project, Scenes & Chapters, General).

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

## Narration

The speech engines installed on this machine, and where you get one ready.

- **Each engine** shows what it is doing — ready, with the device and model it is using; not ready, with the reason; or getting ready.
- **Prepare** downloads and installs what an engine needs. The size is on the button, because it is gigabytes and the decision is yours. It is a once-per-machine step.
- **Open Narration** goes to the [Narration](46-narration.md) view, where casting, per-character voices and per-line direction live.

With no engine installed the section says so: an engine comes from [Extensions](24-extensions.md), and until you have one, reading aloud uses the voices your operating system already has.

An engine that is already installed starts itself when Novalist opens, so this is a page you visit once rather than every morning.

## Diagnostics

- **Read display information** — reports what Novalist can see about your screen: your operating system's scale factor, the current interface size, the window size, the content size, and the monitor's usable area. This is what to send if the interface is the wrong size or is clipped, and it is the fastest way to tell an OS scaling problem from a Novalist one. Like the log, it contains **dimensions only** — never a project name, a file path, or any of your writing.

  **Copy system information** on the [About](44-about.md#copy-system-information) page reports the same display facts, with the version numbers and your locale alongside them, as one block ready to paste into a bug report.
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
- [About](44-about.md) — versions, the changelog in the app, licences, and the support block to send with a bug.
- [Hotkeys](26-hotkeys.md) — every default shortcut and how to rebind them.
- [Localization](27-localization.md) — the bundled interface languages.
