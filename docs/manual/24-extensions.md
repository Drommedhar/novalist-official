# Extensions

Novalist is extensible. An extension is a small .NET assembly that runs inside the Novalist core process and contributes new panels, export formats, entity types, AI integrations, grammar checkers, or property types. Anyone can install one; anyone can write one.

This page covers using extensions. For writing them, see [`extension-guide.md`](../extension-guide.md) in the parent docs folder.

## How extensions run

The Novalist interface is an Electron shell; all project logic lives in the bundled C# core process that starts with the app. Extensions are loaded by that core process, not by the interface — an extension is a .NET DLL plus an `extension.json` manifest, discovered at startup from the user extensions folder.

Since SDK v2, extensions can also contribute **webview views**: HTML panels declared in the manifest's `contributes.views` block. Each view ships its own HTML entry file inside the extension folder and talks to the extension's .NET code over a JSON message channel, so the panel's logic runs in the core process while its UI renders in the app window.

## Extension panels in the binder

When an installed extension contributes views, an **Extensions** group appears at the top of the view rail in the binder (the left pane), above the built-in Write / Plan / World / Publish groups. Each contributed view gets its own button with the icon and title from the manifest; clicking it opens the panel in the main area.

The flagship example is the **AI Assistant** extension, which contributes three panels:

- **AI Chat** — a project-aware chat with your configured AI provider.
- **Character Chat** — converse with one of your characters, grounded in their Codex entry.
- **Story Analysis** — AI-driven analysis findings for your manuscript.

## Installing an extension

1. Get the extension's release package (usually a zip with the DLL, `extension.json`, and assets at the archive root).
2. Unpack it into a folder named after the extension id inside the user extensions folder (see below).
3. **Restart Novalist.** Extensions are discovered when the core process starts. After the restart, a contributed panel appears under **Extensions** in the binder rail; other contributions (export formats, entity types, grammar checkers) show up in their respective features.

## Where extensions live

- **User-installed extensions** — `%APPDATA%/Novalist/Extensions/<extensionId>/` (Windows), `~/Library/Application Support/Novalist/Extensions/<extensionId>/` (macOS), `~/.config/Novalist/Extensions/<extensionId>/` (Linux).
- Each extension folder contains the **DLL**, the **extension.json manifest**, and any **Locales/**, **web/**, or other assets the extension ships.

To remove an extension, close Novalist and delete its folder.

## What extensions can do

Extensions implement hook interfaces from the Novalist SDK. The main contribution points in the current interface:

| Contribution | Adds |
| --- | --- |
| Webview views (`contributes.views` + `IWebViewContributor`) | Panels in the binder rail under Extensions, rendered from the extension's own HTML. |
| `IExportFormatContributor` | New export formats in the Export view. |
| `IEntityTypeContributor` | New entity types in the Codex. |
| `IPropertyTypeContributor` | New property types for templates. |
| `IAiHook` | Extends AI system prompts and processes responses. |
| `IGrammarCheckContributor` | Custom grammar / style checkers for the editor. |

Extensions can register **multiple** hooks — a single extension might add a panel, an export format, and a custom entity type. The manifest's `minHostVersion` / `maxHostVersion` fields declare which Novalist versions the extension supports.

## AI integration

The AI Assistant extension integrates large-language-model providers (for example LM Studio and the GitHub Copilot CLI). Typical settings exposed by an AI extension:

- **Provider** and **endpoint URL** — for self-hosted or local servers.
- **Model name** and **API token** — stored locally.
- **Response language** and **system prompt** overrides.
- **Analysis checks** — toggles for the individual analysis passes.

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
