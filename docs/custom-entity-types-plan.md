# Custom Entity Types — Implementation Plan

## Current State

Novalist has **4 hardcoded entity types**: `Character`, `Location`, `Item`, `Lore`. Each has:
- A dedicated data class (`CharacterData`, `LocationData`, etc.) with typed C# properties
- A dedicated template class (`CharacterTemplate`, `LocationTemplate`, etc.)
- Hardcoded methods in `IEntityService` (`LoadCharactersAsync`, `SaveCharacterAsync`, etc.)
- Hardcoded switch/case logic in `EntityEditorViewModel` and `EntityPanelViewModel`
- Hardcoded folder names in `BookData` and `ProjectMetadata`
- A fixed `CustomPropertyType` enum: `String`, `Int`, `Bool`, `Date`, `Enum`, `Timespan`

The SDK already has an `IEntityTypeContributor` hook and `EntityTypeDescriptor` model, but these are stubs — they collect descriptors but have no backing data model, persistence, or template support.

---

## Goals

1. **In-app custom entity types**: Users can define new entity types (e.g. "Faction", "Magic System") directly inside Novalist, with full template support like the built-in types.
2. **SDK-defined custom entity types**: Extensions can register custom entity types with default properties and templates.
3. **Extensible property types**: Extensions can define new `CustomPropertyType` values (e.g. `Color`, `Rating`, `Url`, `Markdown`) usable by both built-in and custom entity types.

---

## Phase 1: Core Data Model for Custom Entities

### 1.1 — `CustomEntityData` model

Create a generic entity data class in `Novalist.Core/Models/` that stores all its fields dynamically (unlike `CharacterData` which has typed C# properties).

```csharp
// Novalist.Core/Models/CustomEntityData.cs
public class CustomEntityData : IEntityData
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonIgnore]
    public bool IsWorldBible { get; set; }

    [JsonPropertyName("entityTypeKey")]
    public string EntityTypeKey { get; set; } = string.Empty;  // "faction", "magic_system"

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("fields")]
    public Dictionary<string, string> Fields { get; set; } = [];  // known fields from type definition

    [JsonPropertyName("customProperties")]
    public Dictionary<string, string> CustomProperties { get; set; } = [];

    [JsonPropertyName("images")]
    public List<EntityImage> Images { get; set; } = [];

    [JsonPropertyName("sections")]
    public List<EntitySection> Sections { get; set; } = [];

    [JsonPropertyName("relationships")]
    public List<EntityRelationship> Relationships { get; set; } = [];

    [JsonPropertyName("templateId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TemplateId { get; set; }
}
```

### 1.2 — `CustomEntityTypeDefinition` model

A user-defined or extension-provided entity type schema stored in project metadata.

```csharp
// Novalist.Core/Models/CustomEntityTypeDefinition.cs
public class CustomEntityTypeDefinition
{
    [JsonPropertyName("typeKey")]
    public string TypeKey { get; set; } = string.Empty;  // unique key, lowercase_snake

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("displayNamePlural")]
    public string DisplayNamePlural { get; set; } = string.Empty;

    [JsonPropertyName("icon")]
    public string Icon { get; set; } = "📋";

    [JsonPropertyName("folderName")]
    public string FolderName { get; set; } = string.Empty;

    [JsonPropertyName("source")]
    public string Source { get; set; } = "user";  // "user" or extension ID

    [JsonPropertyName("defaultFields")]
    public List<CustomEntityFieldDefinition> DefaultFields { get; set; } = [];

    [JsonPropertyName("features")]
    public CustomEntityFeatures Features { get; set; } = new();
}

public class CustomEntityFieldDefinition
{
    [JsonPropertyName("key")]
    public string Key { get; set; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public CustomPropertyType Type { get; set; } = CustomPropertyType.String;

    [JsonPropertyName("defaultValue")]
    public string DefaultValue { get; set; } = string.Empty;

    [JsonPropertyName("enumOptions")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? EnumOptions { get; set; }

    [JsonPropertyName("required")]
    public bool Required { get; set; }
}

public class CustomEntityFeatures
{
    [JsonPropertyName("includeImages")]
    public bool IncludeImages { get; set; } = true;

    [JsonPropertyName("includeRelationships")]
    public bool IncludeRelationships { get; set; }

    [JsonPropertyName("includeSections")]
    public bool IncludeSections { get; set; } = true;
}
```

### 1.3 — `CustomEntityTemplate` model

A unified template for custom entity types (replaces the need for per-type template classes).

```csharp
// Novalist.Core/Models/EntityTemplate.cs (add to existing file)
public class CustomEntityTemplate
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("entityTypeKey")]
    public string EntityTypeKey { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("builtIn")]
    public bool BuiltIn { get; set; }

    [JsonPropertyName("fields")]
    public List<TemplateField> Fields { get; set; } = [];

    [JsonPropertyName("customPropertyDefs")]
    public List<CustomPropertyDefinition> CustomPropertyDefs { get; set; } = [];

    [JsonPropertyName("sections")]
    public List<TemplateSection> Sections { get; set; } = [];

    [JsonPropertyName("includeImages")]
    public bool IncludeImages { get; set; } = true;

    [JsonPropertyName("includeRelationships")]
    public bool IncludeRelationships { get; set; }
}
```

---

## Phase 2: Storage & Persistence

### 2.1 — Extend `ProjectMetadata`

```csharp
// Add to ProjectMetadata.cs
[JsonPropertyName("customEntityTypes")]
public List<CustomEntityTypeDefinition> CustomEntityTypes { get; set; } = [];
```

This stores type definitions at the **project** level so the entity types are available to all books.

### 2.2 — Extend `BookData`

```csharp
// Add to BookData.cs
[JsonPropertyName("customEntityTemplates")]
public List<CustomEntityTemplate> CustomEntityTemplates { get; set; } = [];

[JsonPropertyName("activeCustomEntityTemplateIds")]
public Dictionary<string, string> ActiveCustomEntityTemplateIds { get; set; } = [];
```

Templates are per-book (matching the existing pattern), keyed by `entityTypeKey`.

### 2.3 — Extend `IEntityService`

```csharp
// Add to IEntityService.cs
// Custom entities
Task<List<CustomEntityData>> LoadCustomEntitiesAsync(string entityTypeKey);
Task SaveCustomEntityAsync(CustomEntityData entity);
Task DeleteCustomEntityAsync(string entityTypeKey, string id, bool isWorldBible = false);

// Custom entity type definitions
Task<List<CustomEntityTypeDefinition>> GetCustomEntityTypesAsync();
Task SaveCustomEntityTypeAsync(CustomEntityTypeDefinition definition);
Task DeleteCustomEntityTypeAsync(string typeKey);
```

### 2.4 — File layout

Custom entities are stored in their own folder per type (following the existing pattern):

```
MyProject/
  .novalist/
    project.json             ← contains customEntityTypes[]
  Book 1/
    Characters/              ← existing
    Locations/               ← existing
    Factions/                ← custom entity folder
      faction-001.json
      faction-002.json
    Magic Systems/           ← custom entity folder
      magic-001.json
```

### 2.5 — Extend `EntityType` enum

The `EntityType` enum stays as-is for the 4 built-in types. Custom types are identified by their `string TypeKey`. A new helper enum value is added:

```csharp
public enum EntityType
{
    Character,
    Location,
    Item,
    Lore,
    Custom   // signals "look at the TypeKey string"
}
```

Alternatively, entity type resolution can use a unified approach:

```csharp
// Novalist.Core/Models/EntityTypeRef.cs
public readonly record struct EntityTypeRef(EntityType BuiltInType, string? CustomTypeKey = null)
{
    public bool IsCustom => BuiltInType == EntityType.Custom;
    public string DisplayKey => IsCustom ? CustomTypeKey! : BuiltInType.ToString();
}
```

---

## Phase 3: Extensible Property Types (SDK)

### 3.1 — `IPropertyTypeContributor` hook

```csharp
// Novalist.Sdk/Hooks/IPropertyTypeContributor.cs
public interface IPropertyTypeContributor
{
    IReadOnlyList<PropertyTypeDescriptor> GetPropertyTypes();
}
```

### 3.2 — `PropertyTypeDescriptor` model

```csharp
// Novalist.Sdk/Models/PropertyTypeDescriptor.cs
public sealed class PropertyTypeDescriptor
{
    /// <summary>Unique key, e.g. "color", "rating", "url".</summary>
    public string TypeKey { get; init; } = string.Empty;

    /// <summary>Display name shown in template editor dropdowns.</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>
    /// Factory that creates the Avalonia editor control for this property type.
    /// Receives the current string value; returns a Control that can edit it.
    /// Must call the provided callback when the value changes.
    /// </summary>
    public Func<string, Action<string>, Control>? CreateEditor { get; init; }

    /// <summary>Validates a string value. Returns null if valid, or error message.</summary>
    public Func<string, string?>? Validate { get; init; }

    /// <summary>Returns a default value for new properties of this type.</summary>
    public string DefaultValue { get; init; } = string.Empty;
}
```

### 3.3 — Unify `CustomPropertyType`

Replace the `CustomPropertyType` enum with a string-based system while keeping backward compatibility:

```csharp
// The enum stays for serialization compat, but we add:
public static class WellKnownPropertyTypes
{
    public const string String = "String";
    public const string Int = "Int";
    public const string Bool = "Bool";
    public const string Date = "Date";
    public const string Enum = "Enum";
    public const string Timespan = "Timespan";

    public static readonly string[] All = [String, Int, Bool, Date, Enum, Timespan];
}
```

Extend `CustomPropertyDefinition` to support string-based types:

```csharp
public class CustomPropertyDefinition
{
    // existing properties remain for backwards compat

    [JsonPropertyName("typeKey")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TypeKey { get; set; }  // extension-provided type key

    // Resolution: if TypeKey is set, use it; otherwise fall back to Type enum
}
```

### 3.4 — Property type registry

```csharp
// Novalist.Core/Services/IPropertyTypeRegistry.cs
public interface IPropertyTypeRegistry
{
    IReadOnlyList<string> GetAllTypeKeys();  // built-in + extension types
    bool IsKnownType(string typeKey);
    // The desktop layer will also hold CreateEditor delegates from extensions
}
```

---

## Phase 4: Enhance `IEntityTypeContributor` (SDK)

### 4.1 — Expand `EntityTypeDescriptor`

```csharp
// Novalist.Sdk/Models/EntityTypeDescriptor.cs (updated)
public sealed class EntityTypeDescriptor
{
    public string TypeKey { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string DisplayNamePlural { get; init; } = string.Empty;
    public string Icon { get; init; } = "🧩";
    public string FolderName { get; init; } = string.Empty;

    /// <summary>Default field definitions for this entity type.</summary>
    public IReadOnlyList<EntityFieldDescriptor> DefaultFields { get; init; } = [];

    /// <summary>Feature toggles (images, relationships, sections).</summary>
    public EntityTypeFeatures Features { get; init; } = new();

    /// <summary>Optional: custom editor view (overrides the generic editor).</summary>
    public Func<object, Control>? CreateEditorView { get; init; }

    /// <summary>Optional: factory for creating a new empty entity.</summary>
    public Func<object>? CreateNew { get; init; }
}

public sealed class EntityFieldDescriptor
{
    public string Key { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string TypeKey { get; init; } = "String";  // built-in or extension type key
    public string DefaultValue { get; init; } = string.Empty;
    public List<string>? EnumOptions { get; init; }
    public bool Required { get; init; }
}

public sealed class EntityTypeFeatures
{
    public bool IncludeImages { get; init; } = true;
    public bool IncludeRelationships { get; init; }
    public bool IncludeSections { get; init; } = true;
}
```

### 4.2 — Extend `IExtensionEntityService`

```csharp
// Add to IExtensionEntityService
Task<IReadOnlyList<CustomEntityInfo>> LoadCustomEntitiesAsync(string typeKey);
```

```csharp
public sealed class CustomEntityInfo
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string EntityTypeKey { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, string> Fields { get; init; } = new Dictionary<string, string>();
}
```

---

## Phase 5: UI Changes

### 5.1 — Entity Panel (sidebar)

- Add a section per custom entity type below the existing 4 tabs/sections
- Each custom type shows its entities in a flat list (like Items/Lore)
- **Add button** at the bottom of the entity panel: "Manage Entity Types..." opens the type editor

Changes to `EntityPanelViewModel`:
- Add `ObservableCollection<CustomEntityTypeDefinition> CustomEntityTypes`
- Add `Dictionary<string, ObservableCollection<CustomEntityData>> CustomEntities` keyed by type
- Update `ActiveEntityType` handling to support `EntityType.Custom` + `ActiveCustomTypeKey`

### 5.2 — Entity Editor

The existing `EntityEditorViewModel` handles all 4 built-in types with switch/case. For custom entities:

- Add a **generic editor mode** that renders fields dynamically from the type definition
- Each field is rendered as the appropriate control based on its type (text box, date picker, checkbox, dropdown, or extension-provided control)
- Custom properties, images, sections, and relationships work exactly like built-in types
- Template selection works via `CustomEntityTemplate`

Changes to `EntityEditorViewModel`:
- Add `CustomEntityData? EditingCustomEntity`
- Add `ObservableCollection<ObservableKeyValue> CustomEntityFields` (the fields specific to the type definition)
- Update `Open/Save/Delete` commands to handle `EntityType.Custom`

### 5.3 — Entity Type Manager Dialog

A new dialog accessible from the entity panel that allows users to:
- Create a new custom entity type (name, plural, icon, folder)
- Define default fields and their types (picking from built-in + extension property types)
- Toggle features (images, relationships, sections)
- Edit/delete existing custom entity types
- See which types came from extensions (read-only, with source label)

### 5.4 — Template Editor Updates

The existing template editor views need to:
- List custom entity types in the type selector
- When editing a custom entity template, show the type's default fields as "known fields"
- Allow adding custom properties with both built-in and extension-provided property types
- The property type dropdown should dynamically include extension types when available

---

## Phase 6: Extension Manager Integration

### 6.1 — `ExtensionManager` changes

When loading extensions:
1. Collect `IEntityTypeContributor.GetEntityTypes()` → convert to `CustomEntityTypeDefinition` records with `source = extensionId`
2. Collect `IPropertyTypeContributor.GetPropertyTypes()` → register in `PropertyTypeRegistry`
3. Merge extension-provided types into the project's type list (without persisting extension types — they're ephemeral and re-contributed on load)

When unloading/disabling extensions:
1. Remove contributed entity types (entities of that type remain on disk but are hidden)
2. Remove contributed property types (properties using them fall back to string display)

### 6.2 — Conflict resolution

- Extension type keys are prefixed with `ext.{extensionId}.` to avoid conflicts
- User-defined type keys are validated against existing keys
- If an extension is disabled, its types are hidden but data is preserved

---

## Phase 7: Serialization & Migration

### 7.1 — Project version bump

Increment `ProjectMetadata.Version` to `3` for projects that use custom entity types.

### 7.2 — Migration path

- Projects at version 2 load with `CustomEntityTypes = []` (empty, backward compatible)
- All new JSON properties use `[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]` or default empty collections
- No existing data changes

### 7.3 — Custom property type serialization

Extension-provided property types (`TypeKey`) are stored as strings in JSON. If the extension is no longer available, the raw string value is still preserved and displayed as plain text.

---

## Task Breakdown

### Milestone 1: Core Models & Persistence
1. [ ] Create `CustomEntityData` model
2. [ ] Create `CustomEntityTypeDefinition` and related models
3. [ ] Create `CustomEntityTemplate` model
4. [ ] Add `Custom` value to `EntityType` enum + `EntityTypeRef` helper
5. [ ] Extend `ProjectMetadata` with `CustomEntityTypes` collection
6. [ ] Extend `BookData` with `CustomEntityTemplates` collection
7. [ ] Extend `IEntityService` with custom entity CRUD methods
8. [ ] Implement custom entity persistence in `EntityService`

### Milestone 2: SDK Extension Points
9. [ ] Create `IPropertyTypeContributor` hook interface
10. [ ] Create `PropertyTypeDescriptor` model
11. [ ] Expand `EntityTypeDescriptor` with `DefaultFields` and `Features`
12. [ ] Create `EntityFieldDescriptor` and `EntityTypeFeatures` models
13. [ ] Add `CustomEntityInfo` to SDK and extend `IExtensionEntityService`
14. [ ] Add `WellKnownPropertyTypes` constants
15. [ ] Extend `CustomPropertyDefinition` with `TypeKey` fallback
16. [ ] Create `IPropertyTypeRegistry` interface

### Milestone 3: Extension Manager Wiring
17. [ ] Wire `IEntityTypeContributor` descriptors → `CustomEntityTypeDefinition` conversion
18. [ ] Wire `IPropertyTypeContributor` descriptors → property type registry
19. [ ] Handle extension enable/disable for entity types and property types

### Milestone 4: UI — Entity Panel & Editor
20. [ ] Extend `EntityPanelViewModel` for custom entity types
21. [ ] Add custom entity type tabs/sections to entity panel view
22. [ ] Add generic editor mode to `EntityEditorViewModel` for custom entities
23. [ ] Create generic entity editor view (AXAML) with dynamic field rendering
24. [ ] Wire up template selection for custom entity types

### Milestone 5: UI — Entity Type Manager
25. [ ] Create `EntityTypeManagerViewModel`
26. [ ] Create `EntityTypeManagerView` dialog (AXAML)
27. [ ] Add "Manage Entity Types..." button to entity panel
28. [ ] Implement field definition UI (add/remove/reorder fields, select types)

### Milestone 6: UI — Template Editor Updates
29. [ ] Add custom entity types to template type selector
30. [ ] Show extension property types in property type dropdown
31. [ ] Update template editor to handle `CustomEntityTemplate`

### Milestone 7: Polish & Integration
32. [ ] World Bible support for custom entities
33. [ ] Context menu integration (`IContextMenuContributor` context values)
34. [ ] AI hook integration (`AiPromptContext` with custom entity names)
35. [ ] Search/filter support for custom entities
36. [ ] Localization keys for custom entity type UI
37. [ ] Project migration (version 2 → 3)
