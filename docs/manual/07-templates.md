# Templates

Templates are pre-filled blueprints that speed up creating new things. Novalist has templates at three levels:

- **Project templates** — full project scaffolds (chapters, acts, sample entities). Picked when creating a new project.
- **Story-structure templates** — pre-defined act/chapter structures (three-act, Save the Cat, hero's journey). A subset of project templates.
- **Entity templates** — per-type defaults for new characters, locations, items, lore, and custom entities.

## Project templates

When you create a new project from the Welcome screen, the **Template** picker lists every available project template. Each template is a packaged set of:

- An initial **book** with chapters, acts, and a story-date setup.
- Optional sample **entities** to demonstrate fields.
- Optional default **templates** for entity types.

The bundled templates include:

- **Blank** — no content. Use this if you want to design everything yourself.
- **Three-act structure** — three acts with placeholder chapters per act.
- **Save the Cat beat sheet** — chapters aligned to the 15 beats.
- **Hero's Journey** — chapters aligned to the 12 stages.

Extensions can contribute additional templates via the SDK.

Templates are read-only at project-creation time — once your project is created, you can rename, reorder, delete, or replace anything from the template without restrictions.

## Entity templates

For each entity type (Character, Location, Item, Lore, and each custom type) a book can have **multiple templates**. When you create a new entity, Novalist pre-fills it from the template for that type.

### Why templates

Templates remove repetitive setup. For a fantasy novel a character template might pre-define:

- A **Backstory** section (empty).
- A **Voice** section (empty).
- A **Goals** section (empty).
- Custom properties: `Allegiance` (Enum: Light / Dark / Neutral), `Magic affinity` (Enum: Fire / Water / Earth / Air / None), `Hometown` (EntityRef → Location).
- Defaults: `Group = "Order of the Dawn"`.

Every character you create from this template starts with all of that already in place.

### Creating an entity template

1. Open **Settings → Templates**, or use the **Template editor** dialog from the Codex Hub.
2. Pick an entity type tab.
3. Click **+New template**.
4. Give it a name.
5. Fill in the default values:
   - **Sections** — list of sections that should exist on every new entity, optionally with default content.
   - **Custom properties** — typed properties with default values (or empty defaults).
   - **Built-in field defaults** — pre-filled values for the type's built-in fields (e.g. a default Group for characters).
6. Save.

### Re-applying a template to an existing entity

The entity editor's template selector lets you change which template an entity uses. Re-applying:

- Adds any missing sections that the template defines.
- Adds any missing custom properties.
- Does **not** overwrite existing values. Your data is safe.

## Story-structure templates

A **story-structure template** is a project template that pre-seeds the book's **acts** and the chapters within them. They sit alongside ordinary project templates in the Welcome screen picker.

A story-structure template typically includes:

- A named act for each major story division.
- One placeholder chapter per beat / stage / turning point.
- A short description in each chapter's notes saying what the beat is for.
- No prose (empty scenes).

The result: you start with a structural outline laid out as chapters, and you fill in the prose. Useful both for plotter-first writers and for retro-fitting an existing manuscript to a known structure.

## Custom entity-type templates

Custom entity types (see [Codex](06-codex.md)) can also have multiple templates. Templates for custom types live in `BookData.customEntityTemplates`, keyed by the type's `entityTypeKey`.

The template editor's tabs include one per custom type, exposing the same UI as the built-in types.

## The template editor dialog

The template editor is reachable from:

- **Settings → Templates** (per type).
- The Codex Hub's **Templates** button.
- The **Entity Type Manager** (for custom type field definitions).

It lets you:

- List, create, rename, delete templates.
- Edit a template's sections (add / rename / reorder / delete; set default content per section).
- Edit a template's custom properties (key, type, default, enum values).
- Edit a template's built-in field defaults.

The template editor saves changes immediately to the book.

## Where to go next

- [Codex](06-codex.md) — entities use templates.
- [Settings](23-settings.md) — Templates section is a sub-page.
- [Extensions](24-extensions.md) — extensions can ship project and entity templates.
