# Novalist.Sdk

SDK for building extensions for [Novalist](https://github.com/Drommedhar/novalist-official), a novel writing assistant.

SDK 15 targets .NET 10 and requires Novalist 3.6.0 or later. Extensions built with this SDK must set `minHostVersion` to at least `3.6.0` in `extension.json`.

## Quick Start

```bash
dotnet new classlib -n MyExtension -f net10.0
dotnet add package Novalist.Sdk
```

Implement `IExtension`:

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

    public void Initialize(IHostServices host) { }
    public void Shutdown() { }
}
```

## Extension Hooks

Implement any of these interfaces alongside `IExtension` to integrate with the host:

| Interface | Purpose |
|-----------|---------|
| `IRibbonContributor` | Add buttons to the ribbon bar |
| `ISidebarContributor` | Add sidebar panels |
| `IContentViewContributor` | Add full-area views (Dashboard, Timeline, etc.) |
| `IContextMenuContributor` | Add context menu items |
| `IEditorExtension` | React to document open/close events |
| `ISettingsContributor` | Add settings pages |
| `IStatusBarContributor` | Add status bar items |
| `IHotkeyContributor` | Register keyboard shortcuts |
| `IExportFormatContributor` | Add export formats |
| `IEntityTypeContributor` | Add custom entity types |
| `IThemeContributor` | Contribute colour themes to the Settings theme picker |
| `IAiHook` | Intercept AI prompts and responses |

## Documentation

See the full [Extension Developer Guide](https://github.com/Drommedhar/novalist-official/blob/main/docs/extension-guide.md) for detailed instructions.

## Deprecated chapter status

`ChapterDetailInfo.Status` and `IExtensionStoryService.SetChapterStatusAsync` are deprecated with non-error `[Obsolete]` notices. Chapter status is no longer supported as an app workflow; these members remain only for compatibility with existing extensions and saved queries. Their stored values and read/write behavior are unchanged, and no chapter status is calculated from scenes.

Use `SceneDetailInfo.Stage` and `SetSceneMetadataAsync` with `SceneMetadataPatch.Stage` for writing state. Existing compiled extensions continue to work. Recompiling callers emits a warning; projects treating warnings as errors must migrate or explicitly suppress the warning in compatibility code.

## License

[MIT](https://github.com/Drommedhar/novalist-official/blob/main/LICENSE)
