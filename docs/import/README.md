# Preparing files for Novalist import

Use [novalist-import.schema.json](novalist-import.schema.json) with any external AI agent or conversion script. It describes the built-in targets: characters, locations, items, lore, scenes, and research notes. Complete, importable examples are in [examples/](examples/).

For **your book's custom fields, templates, or custom Codex types**, open **File → Import a folder… → Export import schema…**. The exported JSON Schema includes those definitions, field prompts, allowed enum values, active template IDs, and an example for each target. Export again after changing a type definition or active template. It contains configuration, rather than existing story entries or their values.

For direct imports, use **Local import API → Copy agent instructions** in the same dialog. Your external agent can request types and their current schemas, upload images, validate records, and send batches directly. See the [local REST API guide](rest-api.md).

Give the schema and your source files to your chosen agent. The conversion can run entirely outside Novalist. A prompt to copy:

```text
Transform the supplied source material into Novalist import files using the
attached novalist-import.schema.json. Follow its exact field names, types,
allowed values, active template IDs, and examples.

Produce one UTF-8 JSON object per file, with novalistImport: 1, target, and data.
Group the output files in separate folders by target. Include Markdown content
for scenes and research notes. For entities, use descriptions, named sections,
and optional content to preserve the original writing.

Map facts to built-in fields, custom entity fields in data.fields, and template
properties in data.customProperties. For scenes/research use data.properties.
Custom field/property values are strings, including integers, booleans, dates,
and enums. Match the schema's definitions. Preserve additional scalar facts
as extra customProperties/properties and additional prose in sections/content.

Keep the source language, names, facts, and writing. Do not invent missing
facts or copy the examples' illustrative values into my data. Omit unknown
optional fields. If a required custom field cannot be determined, report it
for me to resolve. Do not invent entity IDs, storage paths, or template IDs.
Do not combine the whole dataset into one JSON array.

Validate each output file against the schema. Keep the original source files
and write converted files into a new output folder. Report files that need
my attention, including ambiguous names or facts, without guessing.
```

Select the output folder in Novalist and assign each subfolder to its matching target. The `target` inside each JSON file must match that folder's choice. Folder choices control where files go. Each file becomes one entry; processing stays in bounded batches even for a large dataset. JSON files with an invalid version, wrong target, duplicate keys, unsupported fields, or invalid values are reported and left out while other files continue. Schema files ending in `.schema.json` are excluded from discovery.

## Format

```json
{
  "novalistImport": 1,
  "target": "character",
  "data": {
    "name": "Ada",
    "surname": "Lovelace",
    "age": "32",
    "eyeColor": "brown",
    "aliases": ["Enchantress"],
    "customProperties": { "Occupation": "Mathematician" },
    "sections": [
      { "title": "Biography", "content": "Preserved **Markdown** writing." }
    ],
    "relationships": [
      { "role": "collaborator", "target": "Charles Babbage", "category": "ally" }
    ]
  }
}
```

| Target | Entry name | Main writing | Custom values |
| --- | --- | --- | --- |
| `character` | `data.name`, optional `data.surname` | `data.sections`, optional `content` | `data.customProperties` |
| `location` | `data.name` | `data.description`, `data.sections`, optional `content` | `data.customProperties` |
| `item` | `data.name` | `data.description`, `data.sections`, optional `content` | `data.customProperties` |
| `lore` | `data.name` | `data.description`, `data.sections`, optional `content` | `data.customProperties` |
| `scene` | `data.title` | Required `content`, as Markdown | `data.properties` |
| `research` | `data.title` | Required `content`, as Markdown | `data.properties` |
| Custom type key, such as `faction` | `data.name` | `data.sections`, optional `content` | `data.fields` for type fields; `data.customProperties` for template/additional properties |

The schema lists every supported import field. Built-in booleans and numeric fields use JSON booleans/numbers; character age and measurements stay strings. An explicit `birthDate` uses date-based age unless an `ageMode` is supplied. All values in `fields`, `customProperties`, and `properties` use strings: `"42"`, `"true"`, or a calendar date such as `"2024-02-29"`. Custom dates use `YYYY-MM-DD`, and enum values must match the schema. Additional scalar properties are allowed in `customProperties`/`properties`; `importedFrom` and `importedMetadata` are reserved for Novalist's source tracking. Custom type `fields` accept the keys in that type's definition. Required type fields must be supplied.

Template properties are tied to the active template in the exported schema. Omitting `templateId` uses that template automatically. To prepare files for another template, activate it in Novalist and export again. Extension property types keep their text representation; the export names the extension type but does not run its validator.

Names in `relationships[].target` are entry names, rather than generated IDs. Named sections can include `aiHidden` and `readerHidden` booleans. Entity `ai` is `WhenMentioned`, `Always`, or `Never`. Codex `data.images` accepts up to 100 objects containing an uploaded `imageId`, a source `name`, and optional source `alt` text. Upload the images through the [local API](rest-api.md#include-images) before using their references, including in JSON file imports; references must exist in the destination book. Novalist stores portable copies and generates their paths. Other attachments, generated entity IDs, and scope/history overrides are outside this portable format. Scenes become chapters according to the selected source subfolders, and research files become notes.

## Existing Markdown and text files

Conversion is optional when files already have explicit structure. Novalist also reads YAML front matter, labelled lines (including bold labels), two-column Markdown tables, and Markdown headings. Matching ignores case, spaces, hyphens, and underscores; common English, German, and Chinese built-in labels are recognised. Template property keys, custom type field keys, custom field display names, and scene/research property labels also match.

```markdown
---
name: Ada
surname: Lovelace
age: 32
aliases: [Enchantress]
customProperties:
  Occupation: Mathematician
---
**Eye colour:** brown

| Field | Value |
| --- | --- |
| Hair color | dark |

## Biography
Preserved Markdown writing.
```

The importer uses the active entity template. An explicit `template` name or `templateId` in Markdown/text can select another known template. If labels collide, use a namespace such as `customProperties.Role: scout`, `fields.strength: 42`, or `properties.checked: yes`; headings such as `## Custom properties` group labelled lines too. A first-level Markdown heading supplies the entry name when no explicit name/title is present. Plain text uses the file name as its fallback title. Names are kept whole unless a surname is explicitly supplied.

Valid custom values are normalised: `yes` becomes `"true"`, `007` becomes `"7"`, and enum spelling matches its defined option. The first valid explicit value wins when a field is repeated. Unknown, invalid, or ambiguous fields are preserved in the original entity source under **Imported content**, along with the rest of the file. Other headings become named entity sections. For scenes and research, original front matter is preserved in the `importedMetadata` property. Code fences and unlabelled prose do not produce inferred facts. Novalist's importer does not contact an AI service.

See [the import manual](../manual/38-manuscript-import.md#importing-a-folder-of-files) for folder selection, inheritance, progress, and duplicate handling.
