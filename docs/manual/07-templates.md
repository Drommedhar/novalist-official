# Templates

Entity templates are pre-filled blueprints that speed up creating new codex entries. A template exists per **entity type** — Character, Location, Item, Lore, and every [custom entity type](06-codex.md#custom-entity-types) — and a type can have multiple templates.

Templates are edited in **Settings**, in the **TEMPLATES** section; new entities are created from a template in the **Codex**.

## Why templates

Templates remove repetitive setup. For a fantasy novel a character template might pre-define:

- Sections: **Backstory**, **Voice**, **Goals** (empty, ready to fill).
- Custom properties: `Allegiance` (Enum: Light, Dark, Neutral), `Magic affinity` (Enum: Fire, Water, Earth, Air, None).
- Field defaults: `Group = "Order of the Dawn"`.

Every character you create from this template starts with all of that already in place.

## Managing templates (Settings → TEMPLATES)

Open **Settings** from the activity bar (the bottom block, beside Extensions). With a project loaded, the **TEMPLATES** section lists one group per entity type — **Character Templates**, **Location Templates**, **Item Templates**, **Lore Templates**, and one group per custom type.

Each group shows its templates with edit and delete buttons, plus **Add template** to create a new one. Editing or adding opens the template editor as an overlay.

## The template editor

From top to bottom:

### Template Name

The name shown in the template dropdown of the codex's new-entry dialog.

### Fields

The type's known fields (for characters: Gender, Age, Role, eye/hair/height/build and the other physical traits; for locations/items/lore: Type, Description, Origin, Category as applicable). Each field has:

- A **checkbox** — whether entities created from this template include the field.
- A **default value** — optional pre-filled text.

The character **Age** field is special. Instead of a default value it has an **Age Mode**:

- **Number** — age is a plain value you type on the character.
- **Date (Birthdate)** — the character stores a birth date and the age is computed from the story date, using the selected **Interval Unit** (**Years**, **Months**, or **Days** — days and months suit short-lived species or short time spans).

### Custom Fields

Extra scalar fields beyond the known ones — each is a field name plus an optional default. Use **+ Add Custom Field** to add rows and the delete button to remove them.

### Default Custom Properties

Typed properties every new entity should carry. Each row has:

- **Property name**.
- **Type** — String, Integer, Boolean, Date, Enum, or Timespan.
- **Default value** — a text/number/date default; for Boolean a True/False dropdown.
- For **Enum** — a comma-separated option list (e.g. `Red, Green, Blue`); the options become the dropdown in the entity's property editor.
- For **Timespan** — an **Interval Unit** (Years, Months, Days).

### Sections

Sections that should exist on every new entity, each with a title and optional default content.

### Options

Feature toggles for entities created from this template:

- **Include Images** — all types.
- **Include Relationships** — characters and custom types.
- **Include Chapter Overrides** — characters only.

Click **Save** to store the template on the book; **Cancel** discards.

## Using a template

In the [Codex](06-codex.md), click **New entry**. When the active type has templates, the dialog shows a **Template** dropdown — pick one and the new entity is created with the template's fields, defaults, properties, and sections already in place. The template can be combined with the **Use guided wizard** checkbox: the wizard fills values on top of the template's scaffold.

## Templates for custom entity types

Custom entity types get their own group in the TEMPLATES section automatically, with the same editor. The known fields offered are the fields you defined in the type manager (see [Codex](06-codex.md#custom-entity-types)), so a "Faction" type with `Motto` and `Alignment` fields can have templates that pre-fill those.

## Story structures

Earlier Novalist versions offered story-structure project templates at creation time. In the current interface, story structures live in the [Timeline](12-timeline.md): the **Add structure...** dropdown appends the beats of a known structure (Three-Act, Save the Cat, Hero's Journey, 7-Point) as timeline events you can plot your chapters against.

## Where to go next

- [Codex](06-codex.md) — entities are created from templates there.
- [Settings](23-settings.md) — the TEMPLATES section lives in Settings.
- [Extensions](24-extensions.md) — extensions can contribute entity types with their own templates.
