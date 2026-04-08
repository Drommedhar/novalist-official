# Novalist.Sdk

SDK for building extensions for [Novalist](https://github.com/Drommedhar/novalist-official), a novel writing assistant built with Avalonia.

## Quick Start

```bash
dotnet new classlib -n MyExtension -f net8.0
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
| `IThemeContributor` | Override theme styles |
| `IAiHook` | Intercept AI prompts and responses |

## Documentation

See the full [Extension Developer Guide](https://github.com/Drommedhar/novalist-official/blob/main/docs/extension-guide.md) for detailed instructions.

## License

[MIT](https://github.com/Drommedhar/novalist-official/blob/main/LICENSE)
