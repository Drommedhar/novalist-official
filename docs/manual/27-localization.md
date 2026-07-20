# Localization

Novalist's interface is fully localizable. Three languages ship in the box:

- **English** (`en`) — the fallback language.
- **German** (`de`).
- **Chinese, Simplified** (`zh-CN`).

On first launch, Novalist picks the language matching your operating system where possible, and English otherwise.

## Choosing a language

**Settings → Appearance → Language**. The dropdown lists every bundled language by its native name. Switching applies **immediately** — every label in the app changes on the spot, no restart needed.

By default the language is global. If you want a specific project to use a different interface language, open that project and tick the **project override** switch at the top of the Appearance section — the language (and the rest of Appearance) is then stored with the project and applied whenever you open it. See [Settings](23-settings.md) for how global vs project scope works.

## How localization works

All UI strings come from one JSON file per language, bundled with the app. Each file organizes **dot-notation keys** by area:

```json
{
  "language.name": "English",
  "language.code": "en",
  "shell.groupWrite": "Write",
  "dialog.ok": "OK",
  "dialog.cancel": "Cancel",
  ...
}
```

Keys are grouped hierarchically: `shell.*`, `settings.*`, `explorer.*`, `dialog.*`, `map.*`, `welcome.*`, and so on. If a key is missing in a translation, Novalist falls back to the English value, so a partial translation is still usable.

## Tokens

Some strings contain placeholder tokens like `{{version}}` or `{{count}}`. Keep the tokens in your translation — the names must match exactly.

Example:

```json
"shell.backendConnected": "Core connected ({{version}})"
```

Translated:

```json
"shell.backendConnected": "Kern verbunden ({{version}})"
```

## Relationship role keywords

The [Relationships graph](14-relationships.md) classifies family roles (father, mother, sibling, and so on) via keyword matching. The keyword lists live in each locale file under a top-level `relationships` object:

```json
"relationships": {
  "parent":  ["father", "mother", "parent", "dad", "mom"],
  "child":   ["child", "daughter", "son"],
  "partner": ["spouse", "husband", "wife", "partner"],
  "sibling": ["brother", "sister", "sibling", "twin"],
  "pseudo":  ["cousin", "uncle", "aunt", "nephew", ...]
}
```

The matcher merges these arrays from **every** bundled language, so the graph keeps recognising English roles when the UI is in German (and vice versa).

Buckets:

- `parent` / `child` / `partner` / `sibling` drive family clustering and edge typing in the graph.
- `pseudo` covers extended family (cousin, uncle, in-laws, grandparents) used to anchor non-immediate family characters next to the right node.

## Contributing translations

Locale files are compiled into the app, so adding a language means adding a file to the source tree and building. If you'd like your translation included in a future Novalist release:

1. Copy the English locale file (`app/src/renderer/src/locales/en.json` in the repository) to `<code>.json` for your language code (e.g. `fr`, `es`, `pt-BR`).
2. Translate every value. **Do not change the keys.**
3. Set `language.name` to the language's own name ("Français", "Español") and `language.code` to the code.
4. Add a `relationships` keyword section for your language.
5. Open a pull request against the project repo. Translations are welcome.

## Extensions and localization

Extensions ship their own locale files in their `Locales/` folder. The active app language is exposed to extensions through the SDK, and extensions load their own translations independently — translating the core app does not translate extensions, and vice versa.

## Where to go next

- [Settings](23-settings.md) — pick a language here.
- [Extensions](24-extensions.md) — extensions have their own locale files.
