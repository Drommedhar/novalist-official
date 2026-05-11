# Codex (Characters, Locations, Items, Lore, Custom types)

The Codex is Novalist's worldbuilding database. Every named thing in your story can live here: people, places, objects, organizations, magic systems, mythology, technology, languages — whatever you need.

This page covers the four built-in entity types (Character, Location, Item, Lore), custom entity types, the World Bible, chapter overrides, sections, custom properties, relationships, and the two ways to browse entities (the **Entities** sidebar tab and the **Codex Hub** full view).

For the visual relationship graph see [Relationships](14-relationships.md). For templates that pre-fill new entities see [Templates](07-templates.md).

## The built-in entity types

| Type | What it's for |
| --- | --- |
| **Character** | People in your story. Stores name, surname, gender, age, role, group, physical traits, images, relationships. |
| **Location** | Places. Stores name, type, parent (hierarchy), description, images. |
| **Item** | Objects. Stores name, type, description, origin, images. |
| **Lore** | Abstract worldbuilding entries (magic, religion, history). Name, description, images. |

All four share a common set of features: images, sections, custom properties, relationships, and templates.

## Shared entity fields

Every entity has:

- **Name** — the primary label. Characters also have a separate **Surname** field.
- **Images** — a list of images attached to the entity. Each has a relative path and an optional caption.
- **Sections** — named markdown sections (e.g. "Background", "Motivation", "Voice"). You can add, rename, reorder, and delete sections. Sections are where long-form prose about an entity lives.
- **Custom properties** — key-value pairs that don't fit a built-in field. Each property has a **type**: String, Int, Bool, Date, Enum (with predefined values), Timespan, or EntityRef (a typed link to another entity). Extensions can contribute additional types.
- **Relationships** — typed connections to other entities (e.g. "Father", "Mentor", "Enemy", "Owns", "Located in"). Each relationship has a **description** (the role) and a comma-separated list of **target** entity names.
- **Template ID** — the template this entity was created from, if any. Changing the template can re-apply defaults.

## Characters

A character has all the shared fields plus:

- **Gender**
- **Age** — either a literal value or **age mode** with a birth date and an interval unit (the age is computed from the active in-world date).
- **Birth date** — used by age computation in age-mode-`birthDate`.
- **Role** — e.g. "Protagonist", "Antagonist", "Mentor", "Foil". Used by Smart Lists, the Plot Grid, and the Relationships graph.
- **Group** — e.g. "House Stark", "The Crew", "Faculty". Free-text grouping; surfaces in filtered views.
- **Eye color, Hair color, Hair length, Height, Build, Skin tone, Distinguishing features** — physical traits.
- **Chapter overrides** — see below.

### Chapter overrides

A character can have **per-act / per-chapter / per-scene overrides** that change any of the character's fields at a specific point in the story. Example uses:

- A character changes name (Frodo → Mr. Underhill) in a specific chapter.
- A character ages over the course of the book.
- Hair color changes after an event.
- Disguise / alias used only in certain scenes.

To add an override, open the character editor, scroll to **Chapter overrides**, and click **Add override**. Choose the scope (act, chapter, optionally a specific scene) and which fields to override.

The override is applied automatically wherever the character is referenced in that scope: the editor's focus peek, the manuscript view, exports.

## Locations

A location has all the shared fields plus:

- **Type** — e.g. "City", "Forest", "Building", "Continent".
- **Parent** — points to another location, allowing nesting (a city inside a country inside a continent).
- **Description** — free text.

Hierarchy is editable; the Codex Hub displays locations as a tree when a parent is set.

## Items

An item has all the shared fields plus:

- **Type** — e.g. "Weapon", "Artifact", "Vehicle".
- **Description** — free text.
- **Origin** — short note on where the item came from.

## Lore

A lore entry has the shared fields, no extra structure. Use it for anything that doesn't fit Character / Location / Item: magic systems, religions, calendars, language notes, political histories, in-world books.

## Custom entity types

Beyond the four built-ins you can define your own entity types: Factions, Spells, Vehicles, Races, Currencies — whatever the project needs.

### Defining a custom type

Open the **Entity Type Manager** (Codex Hub → **Manage types** button, or `Settings → Templates → Custom Types`):

1. Click **Add type**.
2. Give it a **type key** (used internally, e.g. `faction`), a **display name** (e.g. "Faction"), a **plural** (e.g. "Factions"), and an **icon** (an SVG path string).
3. Optionally point it at a **folder name** under the book directory (defaults to the plural).
4. Define its **fields** — each is a key + type + display label + default. Field types are the same set as custom properties (String / Int / Bool / Date / Enum / Timespan / EntityRef).
5. Save.

After saving, the new type:

- Appears as a tab in the **Codex Hub**.
- Appears as a section in the **Entities** sidebar.
- Can be created via **+New entity → \<your type\>**.
- Can be referenced by other entities through `EntityRef` custom properties.

### Custom-entity templates

Each custom type can have multiple templates with different defaults. The active template ID is stored per type on the book.

### Extensions and custom types

Extensions can contribute custom entity types via the SDK's `IEntityTypeContributor` interface. Types contributed by extensions appear alongside user-defined ones and behave identically — they can have templates, fields, relationships.

See [Extensions](24-extensions.md).

## The World Bible (shared entities)

By default an entity belongs to the active book. For series authors who want a shared cast across multiple books, the **World Bible** holds project-wide entities.

To move an entity into the World Bible:

1. Open the entity editor.
2. Toggle the **Is World Bible** flag (or use the "Move to World Bible" action in the right-click menu).

World Bible entities are visible from every book in the project. The on-disk location is `<Project>/WorldBible/<type>/<entity>.json`.

Useful for:

- A returning cast across a trilogy.
- A shared magic system or pantheon.
- A continent or city that recurs in multiple books.

If you keep all your work in a single book, ignore the World Bible — it adds nothing for single-book projects.

## Browsing entities

### The Entities sidebar tab

Click the **Entities** tab at the top of the left sidebar. You'll see a flat list grouped by type:

- Characters
- Locations
- Items
- Lore
- Each custom type (alphabetically by display name)

Each entry shows the entity's name, optional thumbnail, and an indicator if it's in the World Bible. Click to open in a tab.

The sidebar has:

- A **search box** that filters by name.
- A **+New entity** button (pick the type).
- Per-group **collapse/expand**.

Right-click an entry for:

- **Rename**
- **Duplicate**
- **Delete**
- **Move to World Bible** / **Move to active book**
- **Open in split pane**
- **Take snapshot** (if applicable)

### The Codex Hub (full view)

Click the **Codex Hub** activity bar icon (the open-book glyph). This is the bigger, richer browser:

- **Tabs** along the top: All / Characters / Locations / Items / Lore / each custom type.
- **Search field** — filters across all visible entities.
- **Per-entity card** with name, thumbnail, role / group / type, and a short snippet of the first section.
- **Counts per type** in each tab header.
- **Sort options** — by name, by recently modified.

This is the best view for getting a sense of your cast and world at a glance, or for cleaning up after a long writing session.

## Editing an entity

Click an entity to open it in a tab. The entity editor is split into:

- **Header** — name (and surname for characters), template indicator, primary image, "is world bible" toggle.
- **Built-in fields** — laid out as a form.
- **Custom properties** — key-value editor with type-aware controls (date pickers for dates, dropdowns for enums, entity selectors for EntityRef, etc.).
- **Sections** — markdown-edited blocks you can add, rename, reorder, delete.
- **Relationships** — list of incoming and outgoing relationships, with inline edit. When you add a relationship Novalist remembers the role pair (e.g. "Father" ↔ "Son") and prompts to set the inverse on the target. See **Inverse relationships** below.
- **Images** — gallery with reorder, captions, set-primary.
- **Chapter overrides** (characters only).

### Inverse relationships

When you add a relationship from character A to character B with role "Father", Novalist learns the pair and asks via the **Inverse Relationship Dialog** whether you also want to add the inverse "Son/Daughter" on character B. After a few uses Novalist will remember which role goes with which inverse for your project.

The mapping is stored in `AppSettings.RelationshipPairs` and grows as you use it.

### Adding images

In the entity editor's image gallery, click **Add image**. The **Add Image Source Dialog** asks where the image comes from:

- **From file** — open a system file picker.
- **From clipboard** — paste an image from the clipboard.
- **From the project gallery** — pick from existing project images via the **Project Image Picker**.
- **From URL** — paste a URL; Novalist downloads it.

The image is copied (or referenced) into the book's `Images/` folder. Captions are editable. Reorder by drag.

The first image in the gallery is the entity's **primary image**, shown in the focus peek and in card views.

## Where to go next

- [Relationships graph](14-relationships.md) — visualize relationships across the cast.
- [Templates](07-templates.md) — speed up entity creation with templates.
- [Image Gallery](19-image-gallery.md) — full image browser.
- [Plot Grid](08-plot-grid.md) — track which characters appear in which scenes via plotlines.
