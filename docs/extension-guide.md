# Extension Developer Guide

Novalist is extensible: an extension is a .NET 8 class library that the core process loads at startup, plus a manifest that describes it. Extensions add views, panels, export formats, entity types, editor actions, themes, wizards, and AI providers — everything Novalist itself does not do.

This guide covers building one, what the SDK gives you, packaging it, and submitting it to the extension gallery. For using extensions, see the manual's [Extensions page](manual/24-extensions.md).

## Contents

- [How an extension runs](#how-an-extension-runs)
- [Your first extension](#your-first-extension)
- [The manifest](#the-manifest)
- [Host services](#host-services)
- [Hook interfaces](#hook-interfaces)
- [Web views](#web-views)
- [Localization](#localization)
- [Storing data](#storing-data)
- [What the SDK will not let you do](#what-the-sdk-will-not-let-you-do)
- [Packaging](#packaging)
- [Submitting to the gallery](#submitting-to-the-gallery)
- [Versioning against the host](#versioning-against-the-host)
- [Debugging](#debugging)

## How an extension runs

Novalist's interface is an Electron shell. Every piece of project logic lives in a bundled .NET core process that starts with the app and talks to the shell over JSON-RPC.

**Extensions are loaded by that core process, not by the interface.** They are ordinary .NET assemblies running in the same process as the rest of Novalist's backend, with full framework access. That means:

- Your code is trusted. There is no sandbox around a .NET extension. Only install extensions you trust, and expect your users to apply the same standard to yours.
- A crash in your `Initialize` is caught and recorded, and the extension is skipped — it does not take Novalist down. Anything you start yourself (threads, timers, file watchers) is yours to stop in `Shutdown`.
- Anything you draw is HTML in a sandboxed frame (see [Web views](#web-views)), which *is* isolated from the shell.

Extensions are discovered at startup from the user extensions folder, and installing one from a folder loads it immediately without a restart.

## Your first extension

```bash
dotnet new classlib -n MyExtension -f net8.0
cd MyExtension
dotnet add package Novalist.Sdk
```

One class per assembly implements `IExtension`:

```csharp
using Novalist.Sdk;
using Novalist.Sdk.Services;

public sealed class MyExtension : IExtension
{
    private IHostServices? _host;

    public string Id => "com.example.myextension";      // reverse-domain, unique
    public string DisplayName => "My Extension";
    public string Description => "Counts what nobody asked to be counted.";
    public string Version => "1.0.0";                    // semantic version
    public string Author => "Your Name";

    public void Initialize(IHostServices host)
    {
        _host = host;
        host.ProjectLoaded += project => host.ShowNotification($"Opened {project.Name}");
    }

    public void Shutdown()
    {
        // Unhook events, stop timers, dispose anything you started.
    }
}
```

`Initialize` is called once, when the extension loads. `Shutdown` is called when it is disabled or the app closes. Keep `Initialize` fast and never block on it — a project may not even be open yet, which is what `ProjectLoaded` is for.

## The manifest

Every extension folder needs an `extension.json` beside the DLL:

```json
{
  "id": "com.example.myextension",
  "name": "My Extension",
  "description": "Counts what nobody asked to be counted.",
  "version": "1.0.0",
  "author": "Your Name",
  "entryAssembly": "MyExtension.dll",
  "minHostVersion": "2.5.0",
  "maxHostVersion": "",
  "tags": ["analysis"],
  "icon": "icon.png",
  "dependencies": []
}
```

| Field | Meaning |
|---|---|
| `id` | Must match `IExtension.Id`. Also the folder name Novalist installs into and the key for your settings and data folders. |
| `entryAssembly` | The DLL that holds your `IExtension`, relative to the folder. |
| `minHostVersion` | The oldest Novalist that can load this. Set it to the version whose SDK you built against. |
| `maxHostVersion` | Leave empty unless you know a future host breaks you. |
| `dependencies` | Ids of other extensions that must be present. |
| `contributes` | Declarative web contributions — see [Web views](#web-views). |

An extension whose `minHostVersion` is above the running Novalist is skipped with a load error rather than being loaded and failing later.

## Host services

`Initialize` hands you an `IHostServices`, which is the whole host surface. The important parts:

**`ProjectService`** — the manuscript. Chapters and scenes in order, scene content, synopses, and the currently open scene. It can also *build* structure: `CreateChapterAsync`, `CreateSceneAsync` and `WriteSceneContentAsync` exist so a format importer can be an extension rather than something that has to be written into core.

**`EntityService`** — the Codex. Characters, locations, items, lore, and custom types; creating entries; per-context character profiles and images with all the chapter/scene/act override rules already applied.

Use **`GetAiContextAsync`** rather than the `Load*` methods when you are assembling context for a model. The `Load*` methods return everything, because the Codex has to show everything. `GetAiContextAsync` applies the writer's per-entry AI inclusion setting and their per-section withholding — which your extension cannot reconstruct on its own. A writer who marks an entry "never" means it.

**`FileService`** — file I/O, so you are not reaching for `System.IO` paths the host may relocate.

**Scene analysis storage** — `GetSceneAnalysisAsync`, `SaveSceneAnalysisAsync`, `IsSceneAnalysisStaleAsync`, `GetStaleSceneIdsAsync`. The host owns storage, staleness and the schema; you supply the analysis. Anything cumulative (what a character knows by chapter nine) is a roll-up over these records and needs no further model calls.

**UI** — `ShowNotification`, `ShowBusyProgress`, `ActivateContentView`, `ToggleRightSidebar`, `RunWizardAsync`, `RegisterHotkey`, and `PostToUI` for anything that must run on the UI thread.

**Events** — `ProjectLoaded`, `SceneOpened`, `SceneSaved`, `BookChanged`, `LanguageChanged`. Unsubscribe in `Shutdown`.

## Hook interfaces

Implement any of these alongside `IExtension`; the host finds them by type.

| Interface | What it contributes |
|---|---|
| `IRibbonContributor` | Toolbar buttons |
| `IStatusBarContributor` | Status-bar items |
| `IContextMenuContributor` | Context-menu entries |
| `IInlineActionContributor` | Actions in the editor's caret/selection menu |
| `IEditorExtension` | Reacts to scene open/close/save |
| `ISettingsContributor` | A settings section |
| `ISettingsSchemaContributor` | Typed settings the host renders and stores for you |
| `IHotkeyContributor` | Default keyboard shortcuts |
| `IExportFormatContributor` | An export format in the Export view |
| `IEntityTypeContributor` | A custom Codex entity type |
| `IPropertyTypeContributor` | A custom property type for entity templates |
| `IEntityExtractionContributor` | Proposes Codex entries found in prose |
| `IArticleGeneratorContributor` | Generates Wiki article prose |
| `IGrammarCheckContributor` | A grammar/style checker |
| `IThemeContributor` | Colour themes for the Settings theme picker |
| `IWizardContributor` | Multi-step wizards |
| `IWebViewContributor` | Message handling for a contributed web view |
| `IAiHook` | Intercepts AI prompts and responses |

Each interface's XML documentation on the SDK type is the contract; read it before implementing.

## Web views

Anything with a user interface is HTML rendered in a sandboxed frame. Declare it in the manifest:

```json
"contributes": {
  "views": [
    {
      "key": "com.example.myextension.panel",
      "title": "My Panel",
      "iconPath": "M3 3h18v18H3z",
      "placement": "main",
      "entry": "web/panel.html"
    }
  ]
}
```

- `placement` is `"main"` for a full content view (like Dashboard or Timeline) or `"inspector"` for a right-hand panel.
- `entry` is an HTML file inside your extension folder. Ship its CSS and JS beside it; there is no network access.
- `iconPath` is Lucide-style SVG path data. Novalist's interface uses no emoji anywhere; do not put one in a title either.
- `title` may be a localization key, resolved through your extension's locales.

Implement `IWebViewContributor` to receive messages from the frame and reply to them. That channel is the only way your HTML reaches your .NET code.

## Localization

Ship a `Locales/` folder of JSON files named by language code (`en.json`, `de.json`, …), and resolve keys through `host.GetLocalization(Id)`. Missing keys fall back to English. Subscribe to `LanguageChanged` if you cache resolved strings.

## Storing data

Two folders, and the difference matters:

- `host.GetExtensionDataPath(Id)` → `.novalist/extensions/{id}/` **inside the project**. Use it for anything about *this book*. It travels with the project through Git and to another machine.
- `host.GetExtensionSettingsPath(Id)` → `%APPDATA%/Novalist/extensions/{id}/` **beside the app**. Use it for the writer's own configuration — API endpoints, model choices, preferences. It must never contain project content.

Never put an API key in the project folder. Projects get committed and shared.

## What the SDK will not let you do

Being explicit about the ceiling saves you finding it the hard way:

- **Editing prose in place.** `WriteSceneContentAsync` replaces a scene's content wholesale. There is no API for a diff, a range edit, or a tracked change, so an extension should only write scenes it created — normally as part of an import.
- **Restructuring the manuscript.** You can create chapters and scenes at the end of the book. Reordering, moving, merging, splitting, archiving and deleting are the host's.
- **Writing directly to the Codex model.** `CreateEntityAsync` and `SaveCustomEntityAsync` exist; everything else about an entry is host-owned. Entity extraction returns *proposals*, and the writer confirms them.
- **Drawing native UI.** All extension interface is HTML in a frame. There is no Avalonia or React surface to attach to.
- **Reaching the network from a web view.** The frame is sandboxed. Do network work in .NET.

If a feature you want needs one of these, say so in an issue rather than working around it — several of these are known gaps rather than deliberate walls.

## Packaging

1. Build Release: `dotnet build -c Release`.
2. Collect into one folder: your DLL, `extension.json`, any `Locales/`, `web/`, and asset files, plus any third-party DLLs you depend on that Novalist does not already ship.
3. Test it locally with **Install from Folder** in the Extensions view. It loads immediately — no restart.

The user extensions folder is `%APPDATA%/Novalist/extensions/` on Windows and the equivalent application-support folder elsewhere. Each extension lives in its own `{id}` subfolder.

## Submitting to the gallery

Novalist's extension store reads a public gallery repository and installs from GitHub releases.

1. Host the source on a public Git host.
2. Zip the Release output **with the files at the archive root** — `extension.json` must be the top level of the zip, not inside a folder.
3. Attach the zip to a GitHub release tagged with a semantic version (`v1.0.0`).
4. Open a pull request against the `novalist-extension-gallery` repository adding your manifest entry: id, name, description, author, repository, tags, and icon URL.

Once merged, your extension appears in **Browse Store** inside Novalist, and the store's update check will offer new releases to anyone who installed it.

Browsing and installing use the public GitHub API, which is rate-limited for anonymous requests. Users who hit the limit can add a personal access token under Settings → Updates & Integrations.

## Versioning against the host

Set `minHostVersion` to the Novalist version whose SDK you built against, and raise it when you start using a newer SDK member. Novalist skips an extension whose `minHostVersion` is above the running app and reports it as a load error, which is a far better failure than a `MissingMethodException` halfway through a writing session.

The SDK follows semantic versioning. A breaking change to a hook interface is a major bump and is called out in the changelog.

## Debugging

- Load errors and anything your extension logs go to the core process log. Turn on **Settings → Diagnostics** to write it to a file: `%APPDATA%/Novalist/logs/`.
- **Never log project content.** The diagnostic log exists so users can send it to us; a user who cannot send it without exposing their manuscript will not send it. Log counts, ids, timings and exception types — never titles, prose, or filesystem paths.
- If the app crashes on startup because of an extension, close Novalist and delete or rename `<extensions>/<yourId>/`. The next start skips it.
- The SDK's own README carries a condensed quick-start for reference.

## Where to go next

- [Extensions (manual)](manual/24-extensions.md) — installing, the store, and the AI Assistant extension from a user's point of view.
- [Settings (manual)](manual/23-settings.md) — where extension settings sections appear.
