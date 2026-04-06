# Novalist Extension Developer Guide

This guide explains how to build extensions for Novalist Standalone. Extensions are .NET 8 class libraries discovered from a folder, loaded at startup, and integrated through well-defined hook interfaces.

---

## Quick Start

### 1. Create a class library project

```bash
dotnet new classlib -n MyExtension -f net8.0
```

### 2. Add the SDK reference

```xml
<PackageReference Include="Novalist.Sdk" Version="0.0.1" />
```

Or, if building from source, use a project reference:

```xml
<ProjectReference Include="..\Novalist.Sdk\Novalist.Sdk.csproj" />
```

If your extension provides UI, also reference Avalonia:

```xml
<PackageReference Include="Avalonia" Version="11.3.12" />
```

Enable compiled bindings (recommended):

```xml
<PropertyGroup>
    <AvaloniaUseCompiledBindingsByDefault>true</AvaloniaUseCompiledBindingsByDefault>
</PropertyGroup>
```

### 3. Implement `IExtension`

```csharp
using Novalist.Sdk;
using Novalist.Sdk.Services;

public class MyExtension : IExtension
{
    public string Id => "com.example.myextension";
    public string DisplayName => "My Extension";
    public string Description => "Does something useful.";
    public string Version => "1.0.0";
    public string Author => "Your Name";

    private IHostServices _host = null!;

    public void Initialize(IHostServices host)
    {
        _host = host;
        // Subscribe to events, initialize state
    }

    public void Shutdown()
    {
        // Clean up resources
    }
}
```

### 4. Create `extension.json`

Place this file in your extension's output directory:

```json
{
    "id": "com.example.myextension",
    "name": "My Extension",
    "description": "Does something useful.",
    "version": "1.0.0",
    "author": "Your Name",
    "entryAssembly": "MyExtension.dll",
    "minHostVersion": "0.0.1",
    "maxHostVersion": "",
    "dependencies": [],
    "tags": ["utility"]
}
```

**Fields:**

| Field | Required | Description |
|---|---|---|
| `id` | Yes | Reverse-domain identifier. Must match `IExtension.Id`. |
| `name` | Yes | Human-readable display name. |
| `description` | Yes | Short description. |
| `version` | Yes | Semantic version of the extension. |
| `author` | Yes | Author name. |
| `entryAssembly` | Yes | Filename of the DLL containing the `IExtension` implementation. |
| `minHostVersion` | Yes | Minimum compatible host version (e.g. `"0.0.1"`). |
| `maxHostVersion` | No | Maximum compatible host version. Empty string or omit for no upper bound. |
| `dependencies` | No | List of extension IDs this extension depends on. |
| `tags` | No | Tags for categorization. |

### 5. Install

Copy your extension's output (DLL + `extension.json` + any resources) to:

```
%APPDATA%/Novalist/Extensions/com.example.myextension/
```

The folder name should match the extension ID. Restart Novalist to load the extension.

---

## Extension Lifecycle

1. **Discovery** — On startup, the host scans `%APPDATA%/Novalist/Extensions/` for subfolders containing `extension.json`.
2. **Version check** — The host verifies `minHostVersion` (and `maxHostVersion` if set) against the running host version.
3. **Assembly loading** — The `entryAssembly` DLL is loaded via `Assembly.LoadFrom`. The host finds the first type implementing `IExtension`.
4. **Initialization** — `IExtension.Initialize(IHostServices host)` is called. The extension receives all host services and should register hooks.
5. **Hook collection** — The host checks which hook interfaces the extension implements and collects contributed items (ribbon buttons, sidebar panels, etc.).
6. **Runtime** — The extension responds to events and hooks throughout the app session.
7. **Shutdown** — `IExtension.Shutdown()` is called when the app exits or the extension is disabled.

Extensions can be enabled/disabled at runtime through the Extensions panel in the Start menu without restarting the app.

---

## Host Services

The `IHostServices` object passed to `Initialize` is the extension's gateway to the host application.

### File Operations (`IHostServices.FileService`)

```csharp
Task<string> ReadTextAsync(string path)
Task WriteTextAsync(string path, string content)
Task<bool> ExistsAsync(string path)
Task<bool> DirectoryExistsAsync(string path)
Task CreateDirectoryAsync(string path)
Task<IReadOnlyList<string>> GetFilesAsync(string directory, string pattern = "*", bool recursive = false)
Task<IReadOnlyList<string>> GetDirectoriesAsync(string directory)
string CombinePath(params string[] parts)
string GetFileName(string path)
string GetFileNameWithoutExtension(string path)
string GetDirectoryName(string path)
```

### Project Data (`IHostServices.ProjectService`)

```csharp
string? ProjectRoot { get; }              // Root path of the loaded project
string? ActiveBookRoot { get; }           // Path to the active book
string? WorldBibleRoot { get; }           // Path to the World Bible
bool IsProjectLoaded { get; }

Task<string> ReadSceneContentAsync(string chapterGuid, string sceneId)
IReadOnlyList<ChapterInfo> GetChaptersOrdered()
IReadOnlyList<SceneInfo> GetScenesForChapter(string chapterGuid)
```

### Entity Data (`IHostServices.EntityService`)

```csharp
Task<IReadOnlyList<CharacterInfo>> LoadCharactersAsync()
Task<IReadOnlyList<LocationInfo>> LoadLocationsAsync()
Task<IReadOnlyList<ItemInfo>> LoadItemsAsync()
Task<IReadOnlyList<LoreInfo>> LoadLoreAsync()
List<string> GetProjectImages()
string GetImageFullPath(string relativePath)
```

### Data Storage

Extensions can store data in two locations:

```csharp
// Per-project data (inside the .novalist project folder)
string projectDataPath = host.GetExtensionDataPath("com.example.myextension");

// App-level settings (persists across projects)
string settingsPath = host.GetExtensionSettingsPath("com.example.myextension");
```

### UI Thread

Always use `PostToUI` when modifying UI state from background threads:

```csharp
host.PostToUI(() => {
    // Safe to update UI here
});
```

### Events

```csharp
host.ProjectLoaded += info => { /* A project was opened */ };
host.SceneOpened += scene => { /* A scene was opened in the editor */ };
host.SceneSaved += scene => { /* A scene was saved */ };
host.BookChanged += book => { /* The active book was switched */ };
host.LanguageChanged += lang => { /* UI language changed ("en", "de", etc.) */ };
```

---

## Hook Interfaces

Implement any combination of the following interfaces on your `IExtension` class to contribute to different parts of the application. The host detects implemented interfaces automatically — no registration needed.

### IRibbonContributor

Add buttons to the ribbon toolbar.

```csharp
public IReadOnlyList<RibbonItem> GetRibbonItems() =>
[
    new RibbonItem
    {
        Tab = "Extensions",        // "Edit", "View", or "Extensions"
        Group = "My Tools",
        Label = "My Button",
        Icon = "🔧",
        Tooltip = "Does something",
        Size = "Large",            // "Large" or "Small"
        IsToggle = false,
        IsActive = () => false,    // For toggles: current state
        OnClick = () => { /* handle click */ }
    }
];
```

### ISidebarContributor

Add panels to the left or right sidebar.

```csharp
public IReadOnlyList<SidebarPanel> GetSidebarPanels() =>
[
    new SidebarPanel
    {
        Id = "myext.panel",
        Label = "My Panel",
        Icon = "📋",
        Side = "Right",           // "Left" or "Right"
        Tooltip = "My custom panel",
        CreateView = () => new MyPanelView()
    }
];
```

### IEditorExtension

React to document open/close events in the scene editor.

```csharp
public string Name => "MyEditorExtension";
public int Priority => 100;  // Lower runs first

public void OnDocumentOpened(EditorDocumentContext context)
{
    // context.SceneId, context.ChapterGuid, context.SceneTitle,
    // context.ChapterTitle, context.FilePath
}

public void OnDocumentClosing(EditorDocumentContext context)
{
    // Clean up
}
```

Register/unregister additional editor extensions at runtime:

```csharp
host.RegisterEditorExtension(myEditorExt);
host.UnregisterEditorExtension(myEditorExt);
```

### IAiHook

Extend the AI system prompt and process response chunks.

```csharp
public string? OnBuildSystemPrompt(AiPromptContext context)
{
    // context.CurrentChapterTitle, context.CurrentSceneTitle,
    // context.CharacterNames, context.LocationNames, context.Language
    return "Additional instructions for the AI.";
    // Return null to skip
}

public string OnResponseChunk(string chunk)
{
    // Process or transform the AI response chunk
    return chunk; // Default: pass through unchanged
}
```

### ISettingsContributor

Add custom settings pages to the Settings overlay.

```csharp
public IReadOnlyList<SettingsPage> GetSettingsPages() =>
[
    new SettingsPage
    {
        Category = "My Extension",
        Icon = "⚙",
        CreateView = () => new MySettingsView(),
        OnSave = () => { /* Persist settings */ }
    }
];
```

### IContentViewContributor

Add full content area views (like Dashboard, Plot Board, etc.).

```csharp
public IReadOnlyList<ContentViewDescriptor> GetContentViews() =>
[
    new ContentViewDescriptor
    {
        ViewKey = "ext.myview",
        DisplayName = "My View",
        Icon = "📊",
        CreateView = () => new MyContentView(),
        OnActivated = () => { /* View shown */ },
        OnDeactivated = () => { /* View hidden */ }
    }
];
```

### IStatusBarContributor

Add items to the bottom status bar.

```csharp
public IReadOnlyList<StatusBarItem> GetStatusBarItems() =>
[
    new StatusBarItem
    {
        Id = "myext.status",
        Alignment = "Right",     // "Left", "Center", or "Right"
        Order = 100,
        GetText = () => "Status text",
        GetTooltip = () => "Tooltip",
        OnClick = () => { /* click handler */ },
        OnRefresh = () => { /* called on periodic refresh */ }
    }
];
```

### IContextMenuContributor

Add items to context menus in the explorer and entity lists.

```csharp
public IReadOnlyList<ContextMenuItem> GetContextMenuItems() =>
[
    new ContextMenuItem
    {
        Label = "My Action",
        Icon = "🔧",
        Context = "Scene",        // "Chapter", "Scene", "Character", "Location",
                                  // "Item", "Lore", "Editor"
        OnClick = ctx => { /* handle; ctx is the right-clicked item data */ },
        IsVisible = ctx => true   // Optional: conditional visibility
    }
];
```

### IExportFormatContributor

Add custom export formats.

```csharp
public IReadOnlyList<ExportFormatDescriptor> GetExportFormats() =>
[
    new ExportFormatDescriptor
    {
        FormatKey = "my_format",
        DisplayName = "My Format",
        FileExtension = ".myfmt",
        Icon = "📄",
        Export = async context =>
        {
            // context.ProjectRoot, context.OutputPath, context.BookName
            // Read scenes via host services, write to context.OutputPath
        }
    }
];
```

### IThemeContributor

Contribute custom color themes. Provide either an Avalonia `Styles` object or a path to an `.axaml` resource dictionary in your extension folder.

```csharp
public IReadOnlyList<ThemeOverride> GetThemeOverrides() =>
[
    new ThemeOverride
    {
        Name = "My Theme",
        ResourcePath = "Themes/MyTheme.axaml"  // Relative to extension folder
    }
];
```

Example theme `.axaml` file:

```xml
<ResourceDictionary xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Color x:Key="MyBackground">#FF1A1A2E</Color>
    <SolidColorBrush x:Key="MyBackgroundBrush" Color="{StaticResource MyBackground}" />
</ResourceDictionary>
```

### IEntityTypeContributor

Define custom entity types with their own folder structure and editor UI.

```csharp
public IReadOnlyList<EntityTypeDescriptor> GetEntityTypes() =>
[
    new EntityTypeDescriptor
    {
        TypeKey = "faction",
        DisplayName = "Faction",
        DisplayNamePlural = "Factions",
        Icon = "⚔",
        FolderName = "Factions",
        CreateNew = () => new FactionData(),
        CreateEditorView = data => new FactionEditorView { DataContext = data }
    }
];
```

---

## Data Models Reference

### Event Data

| Type | Properties |
|---|---|
| `ProjectInfo` | `Name`, `RootPath` |
| `BookInfo` | `Id`, `Name` |
| `ChapterInfo` | `Guid`, `Title`, `Order`, `Date` |
| `SceneInfo` | `Id`, `Title`, `ChapterGuid`, `ChapterTitle`, `WordCount` |

### Entity Data

| Type | Properties |
|---|---|
| `CharacterInfo` | `Id`, `DisplayName`, `Role`, `Aliases` |
| `LocationInfo` | `Id`, `Name`, `Type` |
| `ItemInfo` | `Id`, `Name`, `Type` |
| `LoreInfo` | `Id`, `Name`, `Category` |

### Editor Context

| Type | Properties |
|---|---|
| `EditorDocumentContext` | `SceneId`, `ChapterGuid`, `SceneTitle`, `ChapterTitle`, `FilePath` |
| `AiPromptContext` | `CurrentChapterTitle`, `CurrentSceneTitle`, `CharacterNames`, `LocationNames`, `Language` |
| `ExportContext` | `ProjectRoot`, `OutputPath`, `BookName` |

---

## Example Extension

The `Novalist.Sdk.Example` project in the repository is a complete, working extension that demonstrates all 11 hook interfaces:

- **Pomodoro timer** — status bar item + ribbon toggle button
- **Word frequency analysis** — content view with ListBox
- **Writing prompts** — sidebar panel with prompt generation and history
- **Plain text export** — custom export format
- **Sepia & Dark Ocean themes** — `.axaml` resource dictionaries
- **AI hook** — appends context about available tools to the system prompt
- **Editor hook** — tracks document open/close for dirty-state management
- **Context menu** — adds "Analyze word frequency" to chapter and scene menus
- **Settings page** — configurable Pomodoro duration

Browse the source at `Novalist.Sdk.Example/` for a full working reference.

---

## Debugging Tips

1. **Check the extensions folder** — Ensure your `extension.json` and DLL are in `%APPDATA%/Novalist/Extensions/<your-id>/`.
2. **Version mismatch** — If your extension doesn't load, verify `minHostVersion` in `extension.json` matches or is lower than the running Novalist version.
3. **Load errors** — Open the Extensions panel in the Start menu. Extensions with load errors show the error message.
4. **UI thread** — Avalonia requires UI updates on the UI thread. Use `host.PostToUI(() => { ... })` from background threads.
5. **Compiled bindings** — If using `AvaloniaUseCompiledBindingsByDefault`, ensure all `DataTemplate` elements have `x:DataType` attributes.
6. **Shared context** — Extensions run in the same process as the host. Unhandled exceptions in your extension can crash the app. Wrap risky operations in try-catch blocks.
7. **Hot reload** — Disable and re-enable an extension from the Extensions panel to reload it without restarting the app.
