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

**`EntityService`, writing an entry** — `SaveEntityAsync` writes the name, description and free-text sections; `SetEntityFieldsAsync` writes the entry's own typed fields (a character's age and eye colour, a location's region); `SetEntityCustomPropertyAsync` writes the properties the writer added themselves; `SetEntityRelationshipsAsync` writes the relationship rows. An importer that could bring a character across with their biography but not their hair colour produced an entry the writer had to finish by hand.

`SetEntityFieldsAsync` matches field names the way the Codex shows them, without regard to case, and **returns the names it could not write** rather than dropping them — a typo that silently loses a value is the worst way to find out about one. Only text fields are settable; a field holding a list or a date has its own call.

`SetEntityRelationshipsAsync` writes the far half of each row too when you give it an `InverseRole`. A relationship that exists from one side only is worse than none: the graph draws an edge that vanishes when you look from the other end. Leave `InverseRole` empty and the other entry is left alone rather than guessed at.

Use **`GetAiContextAsync`** rather than the `Load*` methods when you are assembling context for a model. The `Load*` methods return everything, because the Codex has to show everything. `GetAiContextAsync` applies the writer's per-entry AI inclusion setting and their per-section withholding — which your extension cannot reconstruct on its own. A writer who marks an entry "never" means it.

**`ArchiveService`** — the project as files and stored versions. `ListSnapshotsAsync` / `ReadSnapshotAsync` / `TakeSnapshotAsync` / `RestoreSnapshotAsync` use the snapshot store the writer already uses, so version tooling does not have to build a second history of the same book beside it. Take one before a pass rewrites anything. A restore is refused while that scene is open with unsaved changes, for the same reason a prose write is.

`GetChaptersOfDraftAsync` and `ReadSceneOfDraftAsync` read a draft that is **not** the open one — comparing two drafts is the most obvious reason to have a second, and it was the one thing an extension could not do. `ListProjectFiles` and `ReadProjectFileAsync` give the raw project folder for archive and backup tooling; a relative path that climbs out of the project is refused rather than followed.

**`StoryService`, the rest of the plan** — `GetCellNote` / `SetCellNoteAsync` reach the plot-grid cell notes: the tick says a thread is present, the note says what it is doing there, and that is the half a thread-coverage report needs to say anything useful. `GetSmartLists` reads the writer's saved lists with the rules behind them, so a report can respect a standing question instead of only ever reporting on the whole book. `GetMapsAsync` reads the maps and their pins, including which Codex entry a pin stands for and which map it opens. Maps are read-only — a map is a drawing, and the drawing surface is the host's.

**`StoryService`, chapters and scene metadata** — `GetChapterDetail` gives a chapter's status, act, in-world date and date range, description, word target, running word count, its scenes in order, and the writer's own fields; `ChapterInfo` carried a title and an order, which is enough to walk the book and nothing else. `SetChapterStatusAsync` writes the status back, refusing one the host cannot display.

`SetSceneMetadataAsync` takes a `SceneMetadataPatch` where **every field is nullable and null means "leave it alone"**. A pass that sets the point of view must not blank the synopsis it said nothing about, which is what a whole-object save would do. In `Properties`, a key with a null value removes that field. The analysis values are written into the scene's override block — the same place the writer's own answers go, so an extension cannot forge the host's detection.

**`ProjectService.CreateProjectAsync`** — writes a whole new project to disk and returns its folder. It deliberately does **not** open it: an importer building a binder should not be able to move somebody out of the book they are in the middle of a sentence of. Tell the writer where it is and let them decide when to open it.

**`ProjectService`, books and drafts** — `GetBooks`, `CreateBookAsync`, `RenameBookAsync`, `SwitchBookAsync`, and the same four for drafts, plus `ActiveBookId` / `ActiveDraftId`. Every other manuscript call on this interface acts on the active book and draft, so an importer building a second volume — or a revision pass wanting its own draft — had no way to say which one it meant. `CreateDraftAsync` takes an optional draft to copy, which is what a revision pass wants: the writer keeps the version it started from.

Creating a book does **not** switch to it; an extension adding a volume should not move the writer out of the one they are in. Switching book or draft is **refused while the editor holds unsaved changes** — the editor would be holding text for a book that is no longer the one being written to.

**`ProjectService`, structural editing** — `RenameChapterAsync`, `RenameSceneAsync`, `MoveSceneAsync`, `MoveChapterAsync`, `SetChapterActAsync`, `TrashChapterAsync`, `ArchiveSceneAsync`. An importer that could add a chapter but not title or order it produced a project the writer had to repair by hand. Note the two destructive verbs are **trash** and **archive** — both recoverable from the binder. There is deliberately no call that erases anything.

**`ResearchService`** — research items, readable and writable, plus `ImportFileAsync` to copy a file into the project. This is what a web-capture extension needs: somewhere to put what it fetched.

**`ReviewService`** — comments on a scene with an author, and `SuggestEditAsync`. That last one is the important one: it is how an extension changes prose it did not write. The proposal lands as a [suggested edit](manual/05-editor.md) the writer takes or turns down, marked with who suggested it. There is no call that silently rewrites a sentence, and there will not be.

**`StoryService`** — what a scene *is* rather than what it says: point of view, intensity, emotion, conflict, stage, tags, plot threads, story dates, narrative mode, act, and the writer's own typed fields. Plus acts, plot threads (readable and creatable) and hand-entered timeline events. A pacing curve or a continuity rule needs this and could not be written without it.

**`EntityService.SaveEntityAsync`** — writes name, description and sections onto an existing entry of any kind. Sections you do not mention are left alone, so filling in one part of an entry does not wipe the rest.

**Pickers** — `PickFolderAsync`, `PickFileAsync`. Opens the real native dialog. Without these the only way to ask for a path was a text field the writer typed into by hand, and found out was wrong once the work had run.

**Wizards** — `RunWizardAsync` runs one and returns the answers. If you contribute a wizard through `IWizardContributor`, set `WizardDefinition.OnCompleted`: a wizard the writer reaches from the command palette hands its answers to the *host*, not to you, so without that callback your wizard is a form that goes nowhere. It fires on completion only, never on cancel.

**Commands** — `GetCommands`, `InvokeCommandAsync`, `RegisterCommand`. A command is a stable id, a title and an optional JSON Schema for its arguments. This is what makes a scripting extension worth having: a macro that can only call the extension hosting it is not automation.

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
| `IExportPostProcessor` | Checks a written export and reports what is wrong |

Each interface's XML documentation on the SDK type is the contract; read it before implementing.

### Reading a scene

`IExtensionStoryService.GetSceneDetail` returns point of view, intensity, emotion, conflict, stage, tags, plot threads, story dates, narrative mode and act — and now **`Cast`** (ids of the Codex entries the writer said are in the scene) and **`FocusEntityId`** (the one it is about). It also carries **`Inactive`**: a scene the writer has parked is out of the book but still in the plan, and an extension that cannot tell the two apart counts words the manuscript does not contain.

Novalist has known both since assigned casts shipped and never handed them over, so an extension reporting on who is in the book could only read the point of view: one name per scene, whoever else was standing there.

### Writing an export format

`IExportFormatContributor.GetExportFormats()` returns descriptors; each one's `Export` is handed an `ExportContext` when the writer runs it. The context carries everything the built-in formats resolve, so a contributed format can produce the same file:

| Property | What it is |
|---|---|
| `ProjectRoot` | The open project's folder |
| `OutputPath` | Where to write |
| `BookName` | The title the writer entered, or the book's name |
| `Author` | The author as entered in the Export view; empty when not given |
| `Language` | BCP-47 tag for the language the book is written in (`de`, `pt-BR`) |
| `CoverImagePath` | Absolute path of the cover, or empty when there is none or the writer turned it off |
| `IncludeTitlePage` | Whether the writer asked for a title page |

Use `Language` for whatever your format calls a language declaration, and read `CoverImagePath` off disk if your format can hold a picture. A format that ignores them produces a file that claims to be English and has no cover, whatever the writer set.

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

- **Silently rewriting prose.** `WriteSceneContentAsync` replaces a scene wholesale and should only be used on scenes you created, normally during an import. To change prose the writer authored, use `ReviewService.SuggestEditAsync` and let them answer. This is a deliberate wall, not a gap: a machine's opinion belongs in the manuscript only once a person has agreed to it.
- **Writing the scene that is open.** `WriteSceneContentAsync` throws when the scene is open in the editor with unsaved changes. Without that refusal your write and the editor's autosave overwrite each other, whichever lands second wins, and somebody's words are gone with no error anywhere. A pass over the whole book should call `IsSceneBusyAsync` per scene and skip the busy one rather than fail on the scene the writer happens to be in:

```csharp
foreach (var scene in host.ProjectService.GetScenesForChapter(chapterGuid))
{
    if (await host.ProjectService.IsSceneBusyAsync(chapterGuid, scene.Id)) continue;
    await host.ProjectService.WriteSceneContentAsync(chapterGuid, scene.Id, html);
}
```
- **Erasing anything.** You can trash a chapter and archive a scene, both of which the writer can undo. Nothing in the SDK deletes a chapter, a scene or an entry for good.
- **Merging or splitting scenes.** Create, rename, move, trash and archive are yours; merge and split are the host's.
- **Replacing the Codex's own rules.** You can create entries and write their names, descriptions and sections. Match settings, AI inclusion, state overrides and per-context resolution stay host-owned, and entity extraction returns *proposals* the writer confirms.
- **Drawing native UI.** All extension interface is HTML in a frame. There is no Avalonia or React surface to attach to.
- **Reaching the network from a web view.** The frame is sandboxed. Do network work in .NET.
- **Deleting a book or a draft.** You can add and rename both. Nothing here erases one, for the same reason nothing erases a chapter.
- **Modifying an export you were handed.** `IExportPostProcessor` gets a path so it can *read* the file and report on it. Rewriting an export the writer is about to send is the worst possible moment to be clever.

If a feature you want needs one of these, say so in an issue rather than working around it.

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
