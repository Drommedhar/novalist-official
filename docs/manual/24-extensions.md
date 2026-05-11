# Extensions

Novalist is extensible. An extension is a small .NET assembly that contributes new buttons, panels, views, export formats, entity types, editor behaviors, AI integrations, or themes. Anyone can install one; anyone can write one.

This page covers using extensions. For writing them, see [`extension-guide.md`](../extension-guide.md) in the parent docs folder.

## The Extensions view

Click the **plug** icon in the activity bar (bottom group). The Extensions view has two tabs:

### Installed

A list of every extension currently installed. Each entry shows:

- **Name**, **version**, **author**.
- **Description**.
- **Enabled / Disabled** toggle — disable to silence an extension without uninstalling.
- **Update** button — visible when a newer version is available in the gallery.
- **Uninstall** button — removes the extension's files.

Disabling an extension takes effect after the next restart for some hooks, but most (sidebar tabs, status bar items, content views) update immediately.

### Browse Store

Connects to the Novalist extension gallery (a GitHub-hosted index of community extensions). Each entry shows:

- **Name**, **author**, **description**, **tags**.
- **Install** button.
- A short readme excerpt and an optional icon (128×128 PNG).

The gallery is filtered to extensions compatible with your running Novalist version (the gallery entries declare `minHostVersion` and `maxHostVersion`).

A search box filters by name, description, and tags.

## Installing an extension

1. Open the **Browse Store** tab.
2. Find the extension you want.
3. Click **Install**. The DLL and its assets are downloaded and unpacked into the user extensions folder.
4. The extension loads immediately. Look for its activity-bar icon, sidebar tab, ribbon button, status-bar item, or settings page to confirm.

Some extensions need configuration after install — check their settings page (under Settings → the extension's category).

## Enabling and disabling

In the **Installed** tab, toggle the **Enabled** switch. A disabled extension contributes nothing.

This is the safest way to debug a misbehaving extension: disable, restart, see if the problem persists.

## Uninstalling

In the **Installed** tab, click **Uninstall**. Confirms, then removes the extension folder. The extension's settings (if any) are preserved unless you delete the corresponding settings file under the app data folder.

## Where extensions live

- **User-installed extensions** — `%APPDATA%/Novalist/Extensions/<extensionId>/` (Windows), `~/Library/Application Support/Novalist/Extensions/<extensionId>/` (macOS), `~/.config/Novalist/Extensions/<extensionId>/` (Linux).
- Each extension folder contains the **DLL**, the **extension.json manifest**, and any **Locales/**, **Themes/**, or assets the extension ships.

## What extensions can do

Extensions implement hook interfaces from the Novalist SDK. The full list of hooks:

| Hook | Adds |
| --- | --- |
| `IRibbonContributor` | Buttons on the ribbon/app bar. |
| `ISidebarContributor` | Tabs in the left or right sidebar. |
| `IContentViewContributor` | Full-screen content views with activity-bar icons. |
| `IStatusBarContributor` | Items in the status bar. |
| `ISettingsContributor` | Categories and pages in Settings. |
| `IHotkeyContributor` | Keyboard shortcuts. |
| `IEditorExtension` | Hooks into the editor's lifecycle (document open/close, save). |
| `IContextMenuContributor` | Items on right-click menus (explorer, entities, editor). |
| `IExportFormatContributor` | New export formats. |
| `IThemeContributor` | Color themes. |
| `IEntityTypeContributor` | New entity types in the Codex. |
| `IPropertyTypeContributor` | New property types for templates. |
| `IInlineActionContributor` | Editor inline actions (AI rewrite, expand, describe). |
| `IAiHook` | Extends AI system prompts and processes responses. |
| `IGrammarCheckContributor` | Custom grammar / style checkers. |

Extensions can register **multiple** hooks. A single extension might add a sidebar tab, a status-bar widget, a settings page, and a hotkey.

## Bundled example extension

`Novalist.Sdk.Example` is a reference extension demonstrating 11 hook types: a Pomodoro timer (status bar + ribbon toggle), word frequency analysis (content view), writing prompts (sidebar), a plain-text export format, two themes (`.axaml`), an AI prompt-injection hook, an editor lifecycle hook, a context-menu item, a custom "Factions" entity type, a settings page, and an inline action.

It's the best reading material if you're writing your own extension.

## AI integration

Some extensions integrate large-language-model providers (OpenAI, LM Studio, Claude, local copilot CLI). Typical settings exposed by an AI extension:

- **Provider** — OpenAI / Anthropic / LM Studio / local CLI.
- **Endpoint URL** — for self-hosted servers.
- **Model name**.
- **API token** — stored locally.
- **Temperature**, **context length**, **frequency penalty**, **repeat-last-N**.
- **Response language**.
- **System prompt** — override the extension's default.
- **Analysis checks** — toggles for entity-reference checking, inconsistency detection, suggestion generation, scene stats.

These settings appear in Settings under the AI extension's category. Refer to the extension's README for specifics; AI extensions are not part of the core app.

## Writing your own extension

See [`extension-guide.md`](../extension-guide.md). In short:

1. Create a .NET 8 class library.
2. Reference the `Novalist.Sdk` NuGet package.
3. Implement `IExtension` and any hook interfaces you want to contribute.
4. Add an `extension.json` manifest.
5. Build, copy output into your user extensions folder, restart.

To publish:

1. Host the source on a public Git host.
2. Build the Release output, zip with files at the archive root, attach to a GitHub release tagged with a semantic version.
3. Submit a PR to the `novalist-extension-gallery` repo adding your manifest entry.

The full submission flow is in the extension guide.

## Troubleshooting extensions

- **Extension didn't load.** Check the Extensions view — the bottom of an entry shows load errors.
- **Crashes on startup.** Disable the suspect extension via the file manager: delete or rename `<extensions>/<extensionId>/`.
- **Hotkey conflicts.** Two extensions binding the same gesture: rebind one of them in Settings → Hotkeys.
- **AI extension consuming credits.** Check the extension's settings for an "enabled" toggle.

## Where to go next

- [`extension-guide.md`](../extension-guide.md) — full SDK and packaging guide for developers.
- [Settings](23-settings.md) — extension-contributed pages live here.
- [Hotkeys](26-hotkeys.md) — extension-contributed bindings appear in the hotkeys grid.
