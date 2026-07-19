# Codex (Characters, Locations, Items, Lore, Custom types)

The Codex is Novalist's worldbuilding database. Every named thing in your story can live here: people, places, objects, organizations, magic systems, mythology — whatever you need.

Open it via **World → Codex** in the binder's view rail. This page covers the four built-in entity types, custom entity types, creating entries (including the guided wizard and the character interview), the detail pane, and the World Bible.

For the visual relationship graph see [Relationships](14-relationships.md). For templates that pre-fill new entries see [Templates](07-templates.md).

## The Codex view

Across the top is a **tab strip**: **Characters**, **Locations**, **Items**, **Lore**, one tab per custom entity type, and a **Manage types** button (see below).

Below it, the view is split in two:

- **The list** (left) — every entity of the active type, with a thumbnail (or initial), name, a short detail line, and a **WB** badge for World Bible entries. Click an entry to select it. At the bottom, **New entry** creates a new entity.
- **The detail pane** (right) — the editors for the selected entity.

## The built-in entity types

| Type | What it's for |
| --- | --- |
| **Character** | People. Name and surname, gender, age, role, group, physical traits, relationships, chapter overrides. |
| **Location** | Places. Type (city, forest, ...), parent location, description. |
| **Item** | Objects. Type, description, origin. |
| **Lore** | Abstract worldbuilding entries (magic, religion, history). Category and description. |

All types share images, aliases, sections, custom properties, and templates.

## Creating an entry

Click **New entry** at the bottom of the list. The dialog asks for:

- **Name** — the entity's primary name.
- **Template** — optional; pick one of the type's templates to pre-fill fields, properties, and sections. See [Templates](07-templates.md).
- **Use guided wizard** — when checked, a step-by-step wizard opens after creation and walks you through the type's remaining key fields one question at a time (for a character: surname, gender, age, role, group, and a short description that is saved as a "Description" section — the name comes from the dialog). Each step shows a short help text; steps can be skipped, and a review step lets you check everything before finishing.

Confirm with **OK** (or `Enter`).

## The character interview

With a character selected, click **Run character interview...** at the top of the detail pane. The interview walks the seven psychology pillars — **Wound**, **Fear**, **Lie they believe**, **Want**, **Need**, **Secret**, and **Voice** — with a help text for each. Your answers are saved as sections on the character (existing sections with the same titles are updated, not duplicated), so they stay editable afterwards like any other section.

## The detail pane

From top to bottom:

- **Actions** — **Run character interview...** (characters only) and **Delete** (asks to confirm).
- **Fields** — the type's scalar fields (name, gender, role, description, ...) as a simple form; changes are saved when you leave a field.
- **Images** — the entity's image strip. **From gallery** picks an existing project image; **Import file** copies a new file into the project. The first image is the thumbnail used in the list and in the editor's hover cards. Remove an image with its close button.
- **Custom properties** — typed key-value pairs. Each property renders with a type-aware control: checkbox for Bool, number input for Int, date picker for Date, dropdown for Enum. Property types and defaults come from the entity's template or type definition; you can also add ad-hoc properties.
- **Chapter overrides** (characters only) — see below.
- **Aliases** — alternative names, entered as chips. Aliases count as mentions of the entity in the editor's hover cards and analysis.
- **Relationships** (characters only) — rows of **Role** (e.g. "Father") and **Names** (comma-separated targets). Powering the [Relationships graph](14-relationships.md), which clusters families from parent/child/partner/sibling roles.
- **Sections** — free-form titled text blocks ("Background", "Motivation", "Voice", ...). Add, retitle, edit, and remove; this is where long-form prose about an entity lives.

## Chapter overrides (characters)

A character can override any of its core fields (name, surname, role, age, eye color, hair color, height, build) from a specific chapter — optionally a specific scene — onward. Example uses:

- A character travels under a false name for a few chapters.
- Hair color changes after an event.
- Age changes between story parts.

In the **Chapter overrides** section, click **Add override**, pick the chapter (and optionally scene), and fill in only the fields that change — blank fields keep inheriting the character's base value. Overrides are applied wherever the character is referenced in that scope, including the editor's hover cards. Remove an override with its close button.

## Custom entity types

Beyond the four built-ins you can define your own types: Factions, Spells, Vehicles, Races — whatever the project needs.

Click **Manage types** in the tab strip:

1. Click **New Entity Type**.
2. Enter a **Display Name** (e.g. "Faction") and optionally a **Plural Name** ("Factions" — auto-generated if empty).
3. Define its **Fields** — each has a name, a type (String, Int, Bool, Date, Enum, Timespan, or EntityRef — a link to another entity), an optional default value, for Enum a comma-separated option list, and a **Required** flag.
4. Choose its **Features**: **Include Images**, **Include Relationships**, **Include Sections**.
5. Confirm with **OK**.

The new type appears as a tab in the Codex, gets its own folder in the book, can have its own [templates](07-templates.md), and can be referenced by other entities through EntityRef fields. Edit or delete user-defined types from the same dialog — deleting a type deletes its entities, and you are asked to confirm.

Extensions can also contribute entity types; those appear alongside user-defined ones but are managed by the extension, not the type manager. See [Extensions](24-extensions.md).

## The World Bible (shared entities)

By default an entity belongs to the active book. Entities stored in the project's **World Bible** are visible from every book and carry a **WB** badge in the list — useful for a returning cast across a trilogy or a shared magic system.

On disk, World Bible entities live in `<Project>/WorldBible/<type>/` instead of the book folder; see [Projects & Books](03-projects-and-books.md#the-folder-layout). If you keep all your work in a single book, ignore the World Bible — it adds nothing for single-book projects.

## Where to go next

- [Templates](07-templates.md) — speed up entity creation with per-type templates.
- [Relationships graph](14-relationships.md) — visualize the cast's connections.
- [Image Gallery](19-image-gallery.md) — browse all project images.
- [Editor](05-editor.md#entity-hover-cards) — hover cards for codex entities in your prose.
