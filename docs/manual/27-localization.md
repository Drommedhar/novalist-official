# Localization

Novalist's interface is fully localizable. The two languages shipping in the box are **English** and **German**. Adding another language is as simple as dropping a translated JSON file into the locales folder — no rebuild required.

## Choosing a language

**Settings → Appearance → Language**. The dropdown lists every locale found in the locales folder. Switching is immediate; no restart needed.

The chosen language is stored in `AppSettings.Language` and applies to:

- Every label, menu, button, tooltip, and dialog in the UI.
- Date pickers, where the language affects month and weekday names (for Gregorian calendars).
- The default auto-replacement language preset.
- The default grammar-check language passed to LanguageTool.

## How localization works

All UI strings come from JSON files under the app's `Assets/Locales/` folder. There is one file per language:

- `en.json` — English (fallback).
- `de.json` — German.
- `<your-language>.json` — your contribution.

Each file is a flat dictionary of **dot-notation keys** mapped to strings:

```json
{
  "language.name": "English",
  "language.code": "en",
  "menu.edit": "Edit",
  "menu.view": "View",
  "ribbon.chapterTooltip": "Create new chapter",
  "ribbon.sceneTooltip": "Create new scene",
  "dialog.ok": "OK",
  "dialog.cancel": "Cancel",
  ...
}
```

Keys are organized hierarchically by area: `menu.*`, `ribbon.*`, `dialog.*`, `settings.*`, `hotkeys.*`, `welcome.*`, etc.

## Adding a new language

1. Copy `Assets/Locales/en.json` to `Assets/Locales/<code>.json` where `<code>` is the language code (e.g. `fr` for French, `es` for Spanish, `pt-br` for Brazilian Portuguese).
2. Translate every value in the JSON file. **Do not change the keys.**
3. Update the special keys `language.name` (used in the language picker, written in the language itself: "Français", "Español") and `language.code`.
4. Drop the file into the locales folder, restart Novalist (or reload via the language picker).
5. The new language appears in **Settings → Language**.

If a key is missing in your translation, Novalist falls back to the English value, so a partial translation is still usable.

## Tokens and pluralization

Some strings contain placeholder tokens like `{0}` or `{0:N0}` (used by the .NET formatter). Keep the tokens in your translation — the number of tokens and their order must match.

Example:

```json
"statusBar.dailyPercent": "Today: {0}%"
```

Translated:

```json
"statusBar.dailyPercent": "Heute: {0}%"
```

A handful of strings are stitched together at runtime with conjunctions ("X and Y", "X, Y, and Z"). Where possible the entire fragment is in the JSON; if you spot an awkward fragment that the JSON can't fully express, file an issue.

## Relationship role keywords

The [Relationships graph](14-relationships.md) classifies family roles (father, mother, sibling, …) via keyword matching. The keyword lists live in each locale file under a top-level `relationships` object:

```json
"relationships": {
  "parent":  ["father", "mother", "parent", "dad", "mom", "papa", "mama"],
  "child":   ["child", "daughter", "son"],
  "partner": ["spouse", "husband", "wife", "partner"],
  "sibling": ["brother", "sister", "sibling", "twin"],
  "pseudo":  ["cousin", "uncle", "aunt", "nephew", "grandfather", ...]
}
```

The matcher merges these arrays from **every** locale file on disk, so the graph keeps recognising English roles when the UI is in German (and vice versa). To add language coverage, add a `relationships` section to your `<code>.json` — no rebuild required.

Buckets:

- `parent` / `child` / `partner` / `sibling` drive family clustering and edge typing in the graph.
- `pseudo` covers extended family (cousin, uncle, in-laws, grandparents…) used to anchor non-immediate family characters next to the right node.

## Contributing translations back

If you'd like your translation included in a future Novalist release, open a pull request against the project repo with your `<code>.json` added under `Novalist.Desktop/Assets/Locales/`. Translations are welcome.

## Extensions and localization

Extensions ship their own locale files in their `Locales/` folder. The active app language is exposed to extensions via `host.CurrentLanguage`; extensions can load their own translations via `host.GetLocalization`. Extension localization is independent of core localization — translating the core app does not translate extensions, and vice versa.

## Right-to-left support

Right-to-left scripts (Arabic, Hebrew, Persian) are not yet fully supported by the Avalonia layout used in Novalist. Translations into RTL languages are still useful but the visual flow remains left-to-right.

## Where to go next

- [Settings](23-settings.md) — pick a language here.
- [Extensions](24-extensions.md) — extensions have their own locale files.
