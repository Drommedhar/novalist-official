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

## What Novalist speaks: the Language packs panel

**Settings → Language packs** lists every language Novalist knows about and, for each one, two separate answers:

- **Interface** — whether the menus are available in it. `Bundled` for the three that ship (English, German, Chinese), `Yours` for a file you dropped in, `Not available` otherwise.
- **Scene analysis** — whether a [lexicon](#the-scene-analysis-lexicon) exists so the Inspector can detect POV, emotion, intensity, conflict and tags in prose written in it.

The two are deliberately separate, because reading the menus in English while writing a French novel is a normal thing to do and one combined "supported" flag would be wrong for half the people asking. The list includes every language the Quote Style picker offers, so a writing language with no lexicon is stated rather than left to be discovered.

The panel has buttons to open both folders, and a **Rescan** that picks up a file you just dropped in without restarting Novalist. Beside any language with no lexicon there is a button that writes a starting one into the analysis folder, seeded with the English word lists so the work is translating a real list rather than guessing which keys exist.

Novalist bundles three interface languages and three lexicons. Everything past that is a file you or a contributor supplies — that has always been true, and the panel is there so it is visible rather than something you find out by wondering why your scenes have no detected emotion.

## Adding a language yourself

You do not have to wait for a release to use Novalist in another language. Drop a `<code>.json` locale file into the `Locales/` folder beside your settings, press **Rescan** in Settings → Language packs, and it joins the language dropdown — including a partial translation, which falls back to English for anything it leaves out, and including a file that patches a language Novalist already ships. See [Custom themes & language packs](34-custom-themes-and-languages.md) for the format and the folder location.

## Contributing translations

The bundled locale files are compiled into the app, so getting a language *shipped* means adding a file to the source tree and building. If you'd like your translation included in a future Novalist release:

1. Copy the English locale file (`app/src/renderer/src/locales/en.json` in the repository) to `<code>.json` for your language code (e.g. `fr`, `es`, `pt-BR`).
2. Translate every value. **Do not change the keys.**
3. Set `language.name` to the language's own name ("Français", "Español") and `language.code` to the code.
4. Add a `relationships` keyword section for your language.
5. Add a **scene-analysis lexicon** (see below) so the Inspector can analyse prose in your language.
6. Open a pull request against the project repo. Translations are welcome.

## The scene-analysis lexicon

The [Inspector's](22-context-sidebar.md) automatic emotion, intensity, conflict, and tags are keyword-driven, so they need a word list per writing language. Each bundled language ships one JSON file:

```
Novalist.Core/Resources/Analysis/analysis.<code>.json
```

The presence of that file is what makes a language supported — no code change is needed. To use one without rebuilding, drop the same file into the `Analysis/` folder beside your settings and press **Rescan** in Settings → Language packs; a file there also overrides a bundled list of the same code. The quickest start is the button beside a language in that panel, which writes a seeded `analysis.<code>.json` for you; otherwise copy `analysis.en.json`. Either way:

- Translate `positive`, `negative`, and `conflict` into words (or stems — matching is a substring test, so the German stem `kämpf` also catches `kämpfen`).
- Translate the `words` of every `emotions` entry, but **keep the `key` values and their order exactly as in the English file**. Keys are stable identifiers that the interface localizes and that scenes store, so a scene's emotion stays valid if you change writing language.
- Translate `firstPerson` into your language's first-person pronouns; these drive first-person POV detection.
- Set `wordBoundaries` to `false` for languages that are not written with spaces between words (as `analysis.zh-CN.json` does); leave it `true` otherwise.

A regional tag falls back to its base language, so `de-AT` uses `analysis.de.json`. A language with no lexicon simply leaves those fields blank in the Inspector rather than guessing with another language's words — writing and exporting are unaffected, and you can set POV, emotion, intensity, conflict and tags by hand on any scene.

## Extensions and localization

Extensions ship their own locale files in their `Locales/` folder. The active app language is exposed to extensions through the SDK, and extensions load their own translations independently — translating the core app does not translate extensions, and vice versa.

## Counting words in Chinese, Japanese, Korean and Thai

Novalist counts words by script rather than by spaces.

Chinese, Japanese and Korean count **one character, one word** — the convention their publishers use. Counting runs of letters instead made a five-hundred-character Chinese scene come out as a handful of words, which put the word count, the daily goal and every target wrong for a language Novalist ships an interface for.

Thai has neither spaces nor a per-character convention, so its runs are divided by an average word length. That is an approximation and is meant as one; it is a great deal closer than counting a whole Thai sentence as a single word.

**Reading time** follows the same split: Chinese, Japanese and Korean are timed in characters a minute, everything else in words a minute. A Chinese chapter timed at a words-a-minute rate reads as several times longer than it takes.

Mixed text is counted correctly by construction: a Chinese sentence with an English name in it counts the name once and each Chinese character once.

## Where to go next

- [Settings](23-settings.md) — pick a language here.
- [Custom themes & language packs](34-custom-themes-and-languages.md) — add a language without rebuilding the app.
- [Extensions](24-extensions.md) — extensions have their own locale files.
