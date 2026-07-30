# Extensions

Novalist is extensible. An extension is a small .NET assembly that runs inside the Novalist core process and contributes new panels, export formats, entity types, AI integrations, grammar checkers, or property types. Anyone can install one; anyone can write one.

This page covers using extensions. For writing them, see [`extension-guide.md`](../extension-guide.md) in the parent docs folder.

## How extensions run

The Novalist interface is an Electron shell; all project logic lives in the bundled C# core process that starts with the app. Extensions are loaded by that core process, not by the interface — an extension is a .NET DLL plus an `extension.json` manifest, discovered at startup from the user extensions folder.

Since SDK v2, extensions can also contribute **webview views**: HTML panels declared in the manifest's `contributes.views` block. Each view ships its own HTML entry file inside the extension folder and talks to the extension's .NET code over a JSON message channel, so the panel's logic runs in the core process while its UI renders in the app window.

## The Extensions view

Novalist has a dedicated **Extensions** management view. Open it from the **Extensions** button in the bottom block of the activity bar (the slim icon rail on the far left, next to Settings), or from the **Go** menu in the app menu bar.

The view has two tabs:

- **Installed** — manage the extensions already on your machine (the default tab, described below).
- **Browse Store** — browse the online extension gallery and install or update extensions directly from Novalist (see [Browsing the extension store](#browsing-the-extension-store)).

The **Installed** tab lists every installed extension with its name, version, author, description, and enabled state, plus any load error. From here you can:

- **Install from Folder** — pick a folder that contains an `extension.json` manifest and its DLL; Novalist copies it into the user extensions folder, then loads and enables it immediately.
- **Enable / Disable** — toggle an extension on or off. Disabling unloads it and removes its contributions; enabling loads it back in.
- **Uninstall** — remove an extension and permanently delete its files from the extensions folder (after a confirmation).
- **Open Extensions Folder** — reveal the user extensions folder in your file manager.
- **Reload** — re-read the contributed views after a change.

## Extension panels in the activity bar and inspector

When an installed and enabled extension contributes views, where each view appears depends on its declared **placement** in the manifest:

- **`main`** — the view gets its own button in the **activity bar**, below the built-in views (just like Codex or Timeline). Each button uses the icon and title from the manifest; clicking it opens the panel in the main area.
- **`inspector`** — the view appears in the right-hand **inspector** (context sidebar), alongside the built-in Context and Footnotes tabs.

The flagship example is the **AI Assistant** extension, which contributes three panels:

- **AI Chat** — a project-aware chat with your configured AI provider.
- **Character Chat** — converse with one of your characters, grounded in their Codex entry.
- **Story Analysis** — AI-driven analysis findings for your manuscript.

## Installing an extension

The simplest way is the **Install from Folder** button in the Extensions view: point it at a folder containing the extension's `extension.json`, DLL, and assets, and Novalist installs, loads, and enables it in place — no restart required.

You can also install manually: unpack the extension's release package into a folder named after the extension id inside the user extensions folder (see below), then restart Novalist so the core process discovers it.

## Browsing the extension store

The **Browse Store** tab of the Extensions view lists extensions from the online **novalist-extension-gallery**. For each gallery entry Novalist shows the name, author, latest compatible version, and a short description, and marks whether it is already **Installed**, has an **Update** available, or is **Incompatible** with your Novalist version.

- **Search** — filter the list by name, author, description, or tag using the search box at the top.
- **Open an extension** — click a card to open its detail page, which renders the extension's **README** and its latest **release notes**.
- **Install / Update** — click the button on a card (or in the detail page) to download and install the latest compatible release. A progress dialog shows the download and lets you **Cancel**. On success the extension is loaded immediately — no restart required — and appears on the Installed tab.
- **Check for updates** — the button in the store toolbar checks every gallery-installed extension for a newer compatible release and reports how many updates are available. Extensions with an update show an **Update** action.

Novalist can also check for extension updates automatically on startup — toggle **Check for extension updates** under **Settings → Updates & Integrations**.

Browsing and installing use the public GitHub API, which is rate-limited for anonymous requests. If you hit the limit, add a **GitHub Personal Access Token** under **Settings → Updates & Integrations** to raise it; the token is stored locally and only ever sent to GitHub.

## Where extensions live

- **User-installed extensions** — `%APPDATA%/Novalist/Extensions/<extensionId>/` (Windows), `~/Library/Application Support/Novalist/Extensions/<extensionId>/` (macOS), `~/.config/Novalist/Extensions/<extensionId>/` (Linux).
- Each extension folder contains the **DLL**, the **extension.json manifest**, and any **Locales/**, **web/**, or other assets the extension ships.

To remove an extension, use the **Uninstall** button in the Extensions view, or close Novalist and delete its folder by hand.

## What extensions can do

Extensions implement hook interfaces from the Novalist SDK. The main contribution points in the current interface:

| Contribution | Adds |
| --- | --- |
| Webview views (`contributes.views` + `IWebViewContributor`) | Rendered from the extension's own HTML. Placement `main` gives the view its own activity-bar icon (like Codex or Timeline); placement `inspector` shows it in the inspector alongside Context and Footnotes. |
| `IExportFormatContributor` | New export formats in the Export view. A contributed format is told the language you write in, your title and author, and where your cover is, so it produces the same file the built-in formats would. A format that says it can hold a cover gets the **Include the book cover** toggle. |
| `IEntityTypeContributor` | New entity types in the Codex (registered into the open project's custom types). |
| `IPropertyTypeContributor` | New property types for templates. |
| `IAiHook` | Extends AI system prompts and processes responses. |
| `IInlineActionContributor` | Selection actions in the editor's right-click menu (e.g. AI rewrite / expand / describe). |
| `IContextMenuContributor` | Extra items in the editor context menu that act on the current scene (e.g. "generate synopsis"). |
| `IGrammarCheckContributor` | Custom grammar / style checkers, merged with the built-in check in the editor. |
| `IArticleGeneratorContributor` | Generates the AI summary shown at the top of a [Wiki](30-wiki.md) article, from a deterministic dossier the host assembles for the entity. |
| `IEntityExtractionContributor` | Proposes new [Codex](06-codex.md) entries for the people, places, and things a scene mentions but the Codex does not have yet. The extension only ever *suggests*: Novalist assembles the passage, filters out names it already knows, shows you a review list, and does the writing itself. |
| `IHotkeyContributor` | Keyboard shortcuts that fire the extension's own commands. |
| `IThemeContributor` | Colour themes, listed in Settings alongside the built-in ones. |
| `IStatusBarContributor` | Live text items in the status bar, with optional click commands. |
| `ISettingsContributor` | Registers a settings category (shown in the Extensions view). |
| `ISettingsSchemaContributor` | A declarative settings form rendered inline in the Extensions view. |
| `IWizardContributor` | Adds guided setup wizards the extension (or the user) can run. |
| `IExportPostProcessor` | Checks a finished export and reports problems before you send it. Validating an EPUB properly means knowing the EPUB specification, which does not have to live in Novalist to be available in it. A check reads the file and reports; it never rewrites it. |

An extension can also **propose an edit** to your prose. It appears as a [suggested edit](05-editor.md) with the extension's name on it, and you take it or turn it down like any other. Nothing an extension does rewrites a sentence you wrote without asking, and nothing an extension does deletes a chapter, a scene or a Codex entry for good — the strongest verbs available to one are moving a chapter to the trash and archiving a scene, both of which you can undo.

Extensions can register **multiple** hooks — a single extension might add a panel, an export format, and a custom entity type. The manifest's `minHostVersion` / `maxHostVersion` fields declare which Novalist versions the extension supports.

### Editor contributions

- **Inline actions** appear in the editor's right-click menu when you have text selected, grouped by the label the extension chooses. Picking one sends the selection to the extension, which returns text that either replaces the selection or is inserted after it.
- **Context-menu items** for a scene appear in the editor menu regardless of selection and act on the scene you are editing (the AI Assistant's "generate synopsis" is one).
- **Grammar contributors** run alongside the built-in grammar check after a typing pause; their issues are underlined and merged with the built-in results.
- **Article generators** produce the optional AI summary on a [Wiki](30-wiki.md) article. The host builds a deterministic, plain-text dossier of the entity (its fields, sections, relationships, and appearances) and hands it to the generator; the returned prose is cached per entity and shown at the top of the article. With no such extension installed, the Wiki simply omits the summary — everything else works unchanged. (The AI Assistant provides one.)

- **Entity extractors** power the Inspector's **Find new entries in this scene** button. The host sends the scene's plain text plus every name the Codex already knows; the extension returns a list of *proposals* (a name, a suggested kind, and a one-line note). Novalist drops anything it already knows or that carries an unknown type, then shows you a review list with a checkbox per proposal — nothing is created until you tick it and confirm, and you can change the suggested kind before accepting. The extension is never given write access to the project. With no such extension installed the button does not appear. (The AI Assistant provides one.)

### Hotkeys, themes, and the status bar

- **Hotkeys** contributed by an extension fire globally (and from inside the editor). Their default gestures are shown together with the built-in shortcuts.
- **Themes** contributed by an extension are listed in **Settings, Appearance, Theme** together with the built-in themes and any you dropped into your own Themes folder; the Extensions view names the ones your extensions provide and points there. A contributed theme carries a table of design tokens, a stylesheet shipped in the extension folder, or simply an accent colour — the same formats described in [Custom themes & language packs](34-custom-themes-and-languages.md).
- **Status-bar items** render at the left of the status bar and refresh about once a second; clicking one runs the extension's command.

## Host UI an extension can drive

Beyond contributing views, an extension can drive shared UI surfaces through the host services it receives in `Initialize`:

- **Notifications** — `ShowNotification(message)` raises a toast in the bottom-right corner. Extension load failures surface the same way.
- **Busy progress** — `ShowBusyProgress(options)` opens a progress dialog with an optional Cancel button (wired to a cancellation token). The AI Assistant uses this for its knowledge scan and synopsis generation.
- **Setup wizards** — `RunWizardAsync(definition)` runs a step-by-step wizard: one question per screen, with conditional steps, host-provided choice lists, and inline validation. Wizards an extension contributes via `IWizardContributor` also appear in the **Extension settings** section at the bottom of the Extensions view, where you can run them on demand.

Extension-contributed **settings categories** are listed in that same Extension settings section. An extension that provides a declarative settings schema (`ISettingsSchemaContributor`) gets an editable form rendered inline there (changes save automatically, like the rest of Settings — there is no separate Save button) — for the AI Assistant this exposes the full provider, model, generation-parameter, analysis-check, and system-prompt configuration. Schema fields can declare **conditional visibility** (`SettingsField.VisibleWhenKey` / `VisibleWhenValues`): a field is shown only while another field currently holds one of the listed values, so — for example — the LM Studio connection fields appear only when the provider is set to LM Studio, and the Copilot fields only when the provider is Copilot. A schema can also include **action buttons** (`SettingsFieldType.Action`) that call back into the extension (`ISettingsSchemaContributor.ExecuteSchemaActionAsync`) with the form's current values and return a refreshed schema — used, for instance, by the AI Assistant's **Refresh models** button, which queries the configured provider and fills the model field's autocomplete **suggestions** (`SettingsField.Suggestions`) with the available models while still letting you type any value. Extensions that only ship a native settings page (`ISettingsContributor`) still appear as a card, and their configuration is driven through the extension's setup wizard.

## AI integration

The AI Assistant extension integrates large-language-model providers. Typical settings exposed by an AI extension:

- **Provider** and **endpoint URL** — for self-hosted or local servers.
- **Model name** and **API token** — stored locally.
- **Response language** and **system prompt** overrides.
- **Analysis checks** — toggles for the individual analysis passes.

There are two shapes of provider, and the difference matters for what you have to set up.

**Direct API providers** talk to a service over HTTP with a key you paste in:

- **LM Studio and anything OpenAI-compatible.** An **Endpoint preset** drop-down fills in the address for LM Studio, Ollama, OpenAI, OpenRouter, Groq, DeepSeek, Mistral, Together and xAI — they all speak the same protocol, so only the address differs. Anything not on the list still works: type its address in and leave the preset alone.
- **Anthropic.** Calls the Messages API directly with your own key. The model list is fetched from the API rather than built into the extension, so a newly released model appears without waiting for an extension update. The generation parameters (temperature, top-P, min-P, frequency penalty, repeat-last-N) are deliberately not sent to this provider — current Claude models reject them outright, so sending them would fail the request rather than being quietly ignored.

**CLI providers** run whichever `copilot` or `claude` binary you already have on your PATH, using the subscription (or API key) you logged that tool into — Novalist never stores a credential of its own. The **Claude Code CLI** provider is a good fit when local models are too slow for the first full analysis pass: it reaches hosted Claude models (pick a model alias — `sonnet`, `opus`, `haiku`, or `fable`) at the speed of a remote service, while the per-scene analysis cache means that expensive pass only runs once. Like the Copilot CLI, it drives a single subprocess and so runs one scene at a time regardless of the parallel-prompts setting.

A finding that names something your Codex does not contain yet — a person, place, item or piece of lore the prose introduced — carries an **Add to Codex** button. It creates the entry with the finding's description and disappears once the entry exists, so you can act on a suggestion where you read it instead of retyping it in the Codex. Entries land under the type the analysis identified, falling back to Lore when it is unsure.

### Writing from the caret: slash commands

Most inline actions transform a passage you have selected. Two things a writer wants have no selection at all: carrying on from where they stopped, and writing towards a beat they can describe in a few words.

Type **`/`** at the start of an empty line in the editor. A menu appears listing the actions that work without a selection; type to filter, arrow up and down, Enter to run. The slash and everything you typed after it are removed before the action runs, so you are left with the prose rather than the instruction.

The AI Assistant contributes two:

- **`/continue`** — writes the next 80–150 words from where you stopped, picking up mid-flow from your last sentence rather than restating it.
- **`/beat <what happens>`** — everything after the keyword is the beat to write towards. `/beat she finally admits it` dramatizes that beat in 80–200 words and stops there rather than running on into the next one.

Both read the prose before the caret, plus the same scene context and character roster the brainstorm actions use, so a continuation stays consistent with what came before. Both insert at the caret and replace nothing.

Actions that allow an empty selection also appear in the right-click menu with nothing selected — the slash menu is a faster way to reach the same thing while your hands are on the keys.

### Controlling what AI sees of your Codex

Every Codex entry decides for itself whether an AI extension may see it, under **What AI may see of this entry** in the [Codex](06-codex.md) detail pane:

- **When a scene mentions it** — the default, and what Novalist always did. The entry goes along when the scene names it.
- **Always** — sent with every scene, mentioned or not. For the handful of things a model needs to know about your world constantly.
- **Never** — kept out of anything sent to a model, however relevant it looks. For an unrevealed twist, or for anything you simply do not want a model to see.

Below that, any of the entry's **sections** can be withheld individually. That is how one secret stays hidden while the character it belongs to still reaches the model — mark the section that says who the killer is, and the rest of the profile goes as normal.

Novalist enforces this itself rather than trusting each extension to. The host computes the allowed set and hands an extension only that, with withheld sections already removed, so an extension has to go out of its way to see something you excluded. Set an entry to Never and it stays out.

The setting only affects what is *sent to a model*. Everything is still fully visible to you in the Codex, in exports, and in search.

### Scene analysis and character knowledge

Each scene is analysed **once**, producing a record under `.novalist/analysis/<sceneId>.json` that holds the entities the scene involves (each marked as physically present or only mentioned), what every present character observed, learned, said and now wants, and any findings. Story Analysis, character knowledge, and the focus peek all read that same record, so whichever feature reaches a scene first pays for it and the others get it free. A scene whose text has not changed is never re-analysed.

Because the record already describes every character in the scene, the **character-knowledge scan** derives each character's knowledge from it rather than asking the model once per character — a scan costs one pass per scene instead of characters multiplied by scenes.

These records are part of your project (not ignored by version control), so a scan run on one machine travels with the project even to a machine with no AI configured.

The **Knowledge** view (View tab of the ribbon, alongside AI Chat and Analysis) is where this data lives. It lists every character with how many scenes they are recorded as present in — distinguishing "not scanned yet" from "scanned, but not in any scene" — and shows, per scene, exactly what the model concluded: what the character observed, learned, said, was uncertain about, their emotion, location, companions, goals, relationship changes, secrets and inventory changes, plus which model produced it. Scenes the character was absent from are listed too, since "not present here" is itself information. The scan is started (and stopped) from the same view.

Reading this before relying on "Talk as character" is worth the minute it takes: the roleplay is only as good as what the scan concluded, and this is the only place to check it.

Two related options live in the **Character Knowledge** settings group:

- **Analyse scenes in the background** — after you save a scene, analyse it quietly so the results are ready before a feature asks. Off by default, since it uses the model without you starting it. While it runs, the status bar shows which scene is being analysed.
- If you already had character knowledge from an older Novalist version, you are asked once per project whether to **keep it as a fallback** or **clear it and re-run**; the old data was generated per character and does not agree with a shared scene record.

The AI Assistant walks you through provider, endpoint, model, token, and response-language on **first project open** via its setup wizard. You can re-run the wizard later from the **Extension settings** section of the Extensions view.

Refer to the extension's README for specifics; AI extensions are not part of the core app, and nothing leaves your machine unless you configure a remote provider.

## Writing your own extension

See [`extension-guide.md`](../extension-guide.md). In short:

1. Create a .NET 8 class library.
2. Reference the `Novalist.Sdk` package.
3. Implement `IExtension` and any hook interfaces you want to contribute. For a UI panel, declare it in `extension.json` under `contributes.views` and implement `IWebViewContributor` for its message handling.
4. Add an `extension.json` manifest.
5. Build, copy the output into your user extensions folder, restart Novalist.

To publish:

1. Host the source on a public Git host.
2. Build the Release output, zip with files at the archive root, attach to a GitHub release tagged with a semantic version.
3. Submit a PR to the `novalist-extension-gallery` repo adding your manifest entry.

The full submission flow is in the extension guide.

## Troubleshooting extensions

- **Extension didn't load.** Check that the folder contains the DLL and a valid `extension.json`, and that the manifest's `minHostVersion` is not higher than your Novalist version. Load errors are written to the core process log.
- **Crashes on startup.** Close Novalist and delete or rename `<extensions>/<extensionId>/`. The next startup skips the missing extension.
- **AI extension consuming credits.** Check the extension's settings for an enable toggle, or remove the extension.

## Where to go next

- [`extension-guide.md`](../extension-guide.md) — full SDK and packaging guide for developers.
- [Settings](23-settings.md) — app settings, including the diagnostics log that captures extension load errors.
- [Export](20-export.md) — extension-contributed formats appear here.
