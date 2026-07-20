# Extensions

Novalist is extensible. An extension is a small .NET assembly that runs inside the Novalist core process and contributes new panels, export formats, entity types, AI integrations, grammar checkers, or property types. Anyone can install one; anyone can write one.

This page covers using extensions. For writing them, see [`extension-guide.md`](../extension-guide.md) in the parent docs folder.

## How extensions run

The Novalist interface is an Electron shell; all project logic lives in the bundled C# core process that starts with the app. Extensions are loaded by that core process, not by the interface — an extension is a .NET DLL plus an `extension.json` manifest, discovered at startup from the user extensions folder.

Since SDK v2, extensions can also contribute **webview views**: HTML panels declared in the manifest's `contributes.views` block. Each view ships its own HTML entry file inside the extension folder and talks to the extension's .NET code over a JSON message channel, so the panel's logic runs in the core process while its UI renders in the app window.

## The Extensions view

Novalist has a dedicated **Extensions** management view. Open it from the **Extensions** button in the bottom block of the activity bar (the slim icon rail on the far left, next to Settings), or from **Extensions** in the start-menu drawer.

The view has two tabs:

- **Installed** — manage the extensions already on your machine (the default tab, described below).
- **Browse Store** — browse the online extension gallery and install or update extensions directly from Novalist (see [Browsing the extension store](#browsing-the-extension-store)).

The **Installed** tab lists every installed extension with its name, version, author, description, and enabled state, plus any load error. From here you can:

- **Install from Folder** — pick a folder that contains an `extension.json` manifest and its DLL; Novalist copies it into the user extensions folder, then loads and enables it immediately.
- **Enable / Disable** — toggle an extension on or off. Disabling unloads it and removes its contributions; enabling loads it back in.
- **Uninstall** — remove an extension and permanently delete its files from the extensions folder (after a confirmation).
- **Open Extensions Folder** — reveal the user extensions folder in your file manager.
- **Reload** — re-read the contributed views after a change.

## Extension panels in the activity bar

When an installed and enabled extension contributes views, each contributed view gets its own button in the **activity bar**, below the built-in views. Each button uses the icon and title from the manifest; clicking it opens the panel in the main area.

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
| Webview views (`contributes.views` + `IWebViewContributor`) | Panels in the binder rail under Extensions, rendered from the extension's own HTML. |
| `IExportFormatContributor` | New export formats in the Export view. |
| `IEntityTypeContributor` | New entity types in the Codex (registered into the open project's custom types). |
| `IPropertyTypeContributor` | New property types for templates. |
| `IAiHook` | Extends AI system prompts and processes responses. |
| `IInlineActionContributor` | Selection actions in the editor's right-click menu (e.g. AI rewrite / expand / describe). |
| `IContextMenuContributor` | Extra items in the editor context menu that act on the current scene (e.g. "generate synopsis"). |
| `IGrammarCheckContributor` | Custom grammar / style checkers, merged with the built-in check in the editor. |
| `IHotkeyContributor` | Keyboard shortcuts that fire the extension's own commands. |
| `IThemeContributor` | Selectable accent themes, applied from the Extensions view. |
| `IStatusBarContributor` | Live text items in the status bar, with optional click commands. |
| `ISettingsContributor` | Registers a settings category (shown in the Extensions view). |
| `ISettingsSchemaContributor` | A declarative settings form rendered inline in the Extensions view. |
| `IWizardContributor` | Adds guided setup wizards the extension (or the user) can run. |

Extensions can register **multiple** hooks — a single extension might add a panel, an export format, and a custom entity type. The manifest's `minHostVersion` / `maxHostVersion` fields declare which Novalist versions the extension supports.

### Editor contributions

- **Inline actions** appear in the editor's right-click menu when you have text selected, grouped by the label the extension chooses. Picking one sends the selection to the extension, which returns text that either replaces the selection or is inserted after it.
- **Context-menu items** for a scene appear in the editor menu regardless of selection and act on the scene you are editing (the AI Assistant's "generate synopsis" is one).
- **Grammar contributors** run alongside the built-in grammar check after a typing pause; their issues are underlined and merged with the built-in results.

### Hotkeys, themes, and the status bar

- **Hotkeys** contributed by an extension fire globally (and from inside the editor). Their default gestures are shown together with the built-in shortcuts.
- **Themes** contributed by an extension are listed under **Extension themes** in the Extensions view. Selecting one applies its accent colour; selecting it again clears it. (On this host only a theme's accent colour is applied; Avalonia style resources from older desktop extensions are ignored.)
- **Status-bar items** render at the left of the status bar and refresh about once a second; clicking one runs the extension's command.

## Host UI an extension can drive

Beyond contributing views, an extension can drive shared UI surfaces through the host services it receives in `Initialize`:

- **Notifications** — `ShowNotification(message)` raises a toast in the bottom-right corner. Extension load failures surface the same way.
- **Busy progress** — `ShowBusyProgress(options)` opens a progress dialog with an optional Cancel button (wired to a cancellation token). The AI Assistant uses this for its knowledge scan and synopsis generation.
- **Setup wizards** — `RunWizardAsync(definition)` runs a step-by-step wizard: one question per screen, with conditional steps, host-provided choice lists, and inline validation. Wizards an extension contributes via `IWizardContributor` also appear in the **Extension settings** section at the bottom of the Extensions view, where you can run them on demand.

Extension-contributed **settings categories** are listed in that same Extension settings section. An extension that provides a declarative settings schema (`ISettingsSchemaContributor`) gets an editable form rendered inline there — for the AI Assistant this exposes the full provider, model, generation-parameter, analysis-check, and system-prompt configuration. Schema fields can declare **conditional visibility** (`SettingsField.VisibleWhenKey` / `VisibleWhenValues`): a field is shown only while another field currently holds one of the listed values, so — for example — the LM Studio connection fields appear only when the provider is set to LM Studio, and the Copilot fields only when the provider is Copilot. A schema can also include **action buttons** (`SettingsFieldType.Action`) that call back into the extension (`ISettingsSchemaContributor.ExecuteSchemaActionAsync`) with the form's current values and return a refreshed schema — used, for instance, by the AI Assistant's **Refresh models** button, which queries the configured provider and fills the model field's autocomplete **suggestions** (`SettingsField.Suggestions`) with the available models while still letting you type any value. Extensions that only ship a native settings page (`ISettingsContributor`) still appear as a card, and their configuration is driven through the extension's setup wizard.

## AI integration

The AI Assistant extension integrates large-language-model providers (for example LM Studio and the GitHub Copilot CLI). Typical settings exposed by an AI extension:

- **Provider** and **endpoint URL** — for self-hosted or local servers.
- **Model name** and **API token** — stored locally.
- **Response language** and **system prompt** overrides.
- **Analysis checks** — toggles for the individual analysis passes.

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
