# Codex (Characters, Locations, Items, Lore, Custom types)

The Codex is Novalist's worldbuilding database. Every named thing in your story can live here: people, places, objects, organizations, magic systems, mythology — whatever you need.

Open it via **World → Codex** in the binder's view rail. This page covers the four built-in entity types, custom entity types, creating entries (including the guided wizard and the character interview), the detail pane, and the World Bible.

For the visual relationship graph see [Relationships](14-relationships.md). For templates that pre-fill new entries see [Templates](07-templates.md).

## The Codex view

Across the top is a **tab strip**: **Characters**, **Locations**, **Items**, **Lore**, one tab per custom entity type (shown by its plural name), and a **Manage types** button (see below).

Below it, the view is split in two: the **navigation column** on the left and the **detail pane** on the right.

### The navigation column

The left column lists every entity of the active type. At the top is a **search box** — filter the list by name as you type — next to a **count** of how many entries currently match. **New entry** at the bottom creates a new entity.

How the list is arranged depends on the type:

- **Characters** are **grouped**, with a **By Role / By Group** toggle above the list. Each group is a collapsible section with a heading and a member count; click a heading to fold or unfold it. Characters with no role/group fall under an "Ungrouped" heading.
- **Locations** are shown as a **parent/child hierarchy tree** — a location whose parent is another location is nested (indented) beneath it, so a city can sit under its region.
- **Items**, **Lore**, and custom types are shown as a flat list.

Each row shows a thumbnail (or the entity's initial), its name, and a short detail line. Characters also show a one-letter **gender badge**, and World Bible entries carry a **WB** badge.

**Right-click** any row for a context menu to **Move to World Bible** / **Move to Book** (see [The World Bible](#the-world-bible-shared-entities)) or **Delete** it (with confirmation).

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

The right pane holds the editors for the selected entity, from top to bottom:

- **Actions** — **Run character interview...** (characters only) and **Delete** (asks to confirm).
- **Fields** — the type's own fields, laid out as a typed, grouped form with the right control for each field. Changes save when you leave a field.
  - **Characters** get two groups: **Basic Info** (name, surname, gender, age, role, group) and **Physical Attributes** (eye color, hair color, hair length, height, build, skin tone, and a multi-line distinguishing features box).
  - **Locations** have name, type, a **parent location** field with autocomplete over the project's other locations (this drives the hierarchy tree in the navigation column), and a description.
  - **Items** have name, type, origin, and description.
  - **Lore** has name, a **category** dropdown (Organization, Culture, History, Other), and description.
  - **Custom types** render the typed fields you declared for them: a text box for String, a number box for Int, a date picker for Date, a dropdown for Bool and Enum, and a picker for EntityRef.
- **Images** — the entity's image strip. **From gallery** picks an existing project image; **Import file** copies a new file into the project; **Paste from clipboard** pastes a copied image; **From URL** downloads an image from a web address. Each image has an editable **name**, a **swap** button (replace it with another gallery image), and a remove button. The first image is the thumbnail used in the list and in the editor's hover cards.
- **Custom properties** — typed key-value pairs. Each renders with a type-aware control: checkbox for Bool, number input for Int, date picker for Date, dropdown for Enum, text for String. Types and defaults come from the entity's template or type definition; you can also add ad-hoc properties and delete any of them.
- **Chapter overrides** (characters only) — see below.
- **Aliases** — alternative names, entered as chips. Aliases count as mentions of the entity in the editor's hover cards and analysis.
- **Relationships** (characters only) — rows of **Role** (e.g. "Father"), a **target** name, and an **inverse role**. Role and target both autocomplete against the existing cast and the roles already in use. When you set a role, Novalist suggests its inverse and, on save, writes the reciprocal relationship back onto the target character automatically — and learns the role/inverse pair so it can suggest it next time. This powers the [Relationships graph](14-relationships.md), which clusters families from parent/child/partner/sibling roles.
- **Sections** — free-form titled text blocks ("Background", "Motivation", "Voice", ...). Add, retitle, edit, and remove; this is where long-form prose about an entity lives.

## Chapter overrides (characters)

A character can restate its identity or appearance for a specific chapter — optionally a single scene within it. Example uses:

- A character travels under a false name for a few chapters.
- Hair color changes after an event.
- Age changes between story parts.

In the **Chapter overrides** section, click **Add override**, pick the chapter (and optionally a scene of that chapter), and fill in only the fields that change — blank fields keep inheriting the character's base value. Editing happens **inline** in the detail pane: the override form expands in place beneath its scope row (there is no pop-up dialog). The full field set is available, grouped the same way as the base editor:

- **Basic Info** — name, surname, role, gender, age.
- **Physical Attributes** — eye color, hair color, hair length, height, build, skin tone, distinguishing features.
- **Custom Properties** — any custom property the character carries can be restated for the scope; blank inherits the base value.

Once a scope is saved, the same inline editor also lets you override the character's **images**, **relationships**, and **sections** for that scope — each edited in place, exactly like the base editor:

- **Images** — add from the gallery, import a file, paste from the clipboard, or download from a URL, and remove or rename per image. The scope keeps its own image set (which may be empty, meaning "no images here"), independent of the base.
- **Relationships** — restate the character's relationships (role and target) for the scope.
- **Sections** — restate the free-text sections for the scope.

Each of these three starts out **inheriting** the base list (labelled "Inheriting base"); the first edit makes the scope own the list, and a **Reset to inherit** button drops the override and falls back to the base again. Overriding images, relationships, or sections requires the scope to exist first, so save the scope's fields once before editing them.

Each saved override shows its **scope** (chapter, or "chapter → scene") and a summary of which fields it changes (including whether it overrides images, relationships, or sections). Click the row (or the pencil button) to expand its inline editor, or the close button to remove it. A **scene-specific** override wins over a **chapter-wide** one for the same chapter, and each overridden non-blank field wins over the base value. Images, relationships, and sections replace the base list wholesale when the scope owns them.

If a character's age is stored as a **birth date**, the override does not need an explicit age at all — the displayed age is computed from the birth date against the scope's story date automatically (see below).

Resolved override values appear everywhere the character surfaces for that scope:

- The editor's **focus-peek** hover card shows the overridden name, role, age, appearance, custom properties, relationships, images and sections for the scene you are editing, with a subtle banner naming the scope it is showing ("Overridden for ...").
- The [Inspector's](22-context-sidebar.md) scene context list shows the character's overridden name, role, gender and age for the open scene.

When a character's age is kept as a **birth date**, the age shown on the focus-peek card and in the Inspector's character cards is the age **at that scene** — computed from the birth date against the open scene's story date (falling back to the chapter's date, then to today). The interval unit (years, months, or days) follows the character's age setting.

## Custom entity types

Beyond the four built-ins you can define your own types: Factions, Spells, Vehicles, Races — whatever the project needs.

Click **Manage types** in the tab strip:

1. Click **New Entity Type**.
2. Enter a **Display Name** (e.g. "Faction") and optionally a **Plural Name** ("Factions" — auto-generated if empty).
3. Define its **Fields** — each has a name, a type (String, Int, Bool, Date, Enum, Timespan, or EntityRef — a link to another entity), an optional default value, for Enum a comma-separated option list, for **EntityRef** a target-type picker choosing which entity type it links to, and a **Required** flag.
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
