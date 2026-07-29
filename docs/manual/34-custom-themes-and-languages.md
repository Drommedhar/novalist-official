# Custom themes and language packs

Novalist ships three themes and three interface languages, but neither set is closed. You can add your own by dropping a file into a folder, and extensions can contribute themes too. Everything you add appears in the normal **Settings, Appearance** dropdowns alongside what ships in the box.

Nothing here executes code. A theme is a colour table or a stylesheet, and a language is a JSON file of strings — a file you download from someone else can change how Novalist looks and reads, but it cannot run anything.

## Where the folders are

The three folders sit next to your settings file and your extensions. Novalist creates them empty on first launch, so they are always there waiting.

| Platform | Location |
| --- | --- |
| Windows | `%APPDATA%\Novalist\` |
| macOS | `~/Library/Application Support/Novalist/` |
| Linux | `~/.config/Novalist/` |

Inside you will find:

- `Themes/` — your own colour schemes.
- `Locales/` — your own interface languages.
- `Analysis/` — word lists that teach the Inspector to analyse prose in a new writing language.

**Settings, Appearance** has a **Themes folder** and a **Languages folder** button that open the first two directly, so you never have to remember the path.

Novalist reads all three folders when it starts, and again whenever you press **Rescan** in Settings, Language packs. After you add, edit, or delete a file, press Rescan: the theme list, the interface-language list, and the analysis lexicons are all rebuilt in place, with no restart.

## Themes

A theme restates Novalist's colour palette. Type, spacing, and corner radii belong to the Novalist identity and are not part of what a theme changes — so a theme can look completely different without drifting into a different application.

Two file formats are accepted, and both live in `Themes/`. The file name (minus its extension) is what identifies the theme internally, so keep it unique.

### The token map (`.json`)

The usual choice. List the design tokens you want to change and leave out the rest — anything you omit keeps its default value, so a theme can restate the whole palette or just recolour the accent.

```json
{
  "name": "Nord",
  "tokens": {
    "--nl-surface-window": "#2e3440",
    "--nl-surface-sidebar": "#3b4252",
    "--nl-surface-card": "#3b4252",
    "--nl-text": "#eceff4",
    "--nl-text-dim": "#d8dee9",
    "--nl-accent": "#88c0d0",
    "--nl-accent-hover": "#8fbcbb",
    "--nl-border": "#434c5e"
  }
}
```

`name` is what the dropdown shows. Leave it out and the file name is used.

Only tokens in the `--nl-` family are honoured. Two other things are deliberately ignored:

- **`--nv-` tokens.** That is the brand layer — the Novalist colophon itself. It is not overridable.
- **Values carrying CSS punctuation** (`;`, `{`, `}`, comment markers). A token map is written into a rule Novalist builds, and a value that could close that rule early is dropped rather than escaped. If you need something a single declaration cannot hold, use a stylesheet instead.

Colours can be written any way CSS accepts them: `#2e3440`, `rgb(46 52 64)`, `rgb(136 192 208 / 0.6)`.

### The stylesheet (`.css`)

For themes that need rules, not just values. The file is injected while the theme is selected and removed the moment you pick another one, so its rules can never leak into a different theme.

```css
:root {
  --nl-surface-window: #2e3440;
  --nl-text: #eceff4;
  --nl-accent: #88c0d0;
}

/* The reason to use a stylesheet: this is a rule, not a value. */
body {
  background-image: radial-gradient(120% 90% at 50% 0%, #3b4252 0%, transparent 60%);
  background-attachment: fixed;
}
```

Write your declarations under `:root` — Novalist only injects the file while your theme is active, so you do not need a selector that scopes it yourself.

A stylesheet is unrestricted, which is its point and its risk: a rule that hides a panel really will hide that panel. If a theme leaves the app unusable, delete the file and press Rescan (or restart, if the theme hid the button).

### The token names

The full, current list lives in `app/src/renderer/src/styles/tokens.css` in the repository. The ones worth knowing:

| Token | What it colours |
| --- | --- |
| `--nl-surface-window` | The page behind everything. |
| `--nl-surface-sidebar` / `--nl-surface-toolbar` / `--nl-surface-inspector` | The chrome around the writing surface. |
| `--nl-surface-editor` | The page you write on. |
| `--nl-surface-card` | Panels and cards — the one raised surface. |
| `--nl-surface-input` | Text fields and dropdowns. |
| `--nl-surface-hover` / `--nl-surface-selected` | Row hover and selection washes. |
| `--nl-text` / `--nl-text-dim` / `--nl-text-subtle` | Primary, secondary, and meta text. |
| `--nl-accent` / `--nl-accent-hover` / `--nl-accent-ink` | The accent, its hover shade, and the label colour that sits on it. |
| `--nl-border` / `--nl-border-subtle` / `--nl-border-firm` | Panel edges, hairline dividers, chip outlines. |
| `--nl-success` / `--nl-warning` / `--nl-danger` | Status colours. |
| `--nl-scrollbar-thumb` (and `-hover` / `-active`) | Scrollbars, which the browser paints itself. |
| `--nl-base` | The window ground as a raw `r g b` triple (`46 52 64`), used by the translucent macOS material layer. |

Set `--nl-base` if you set `--nl-surface-window`, or the frosted chrome on macOS will tint toward the old colour.

### Picking a theme

**Settings, Appearance, Theme** lists the built-in themes first, then yours, then any contributed by extensions. Selecting one applies it immediately. The **Accent Color** picker still overrides the accent on top of whichever theme is active.

If you delete a theme file that was selected, Novalist falls back to the default palette on the next launch rather than starting unstyled.

## Interface languages

Drop `<code>.json` into `Locales/` — `fr.json`, `es.json`, `pt-BR.json` — and the language joins **Settings, Appearance, Interface Language**.

The fastest way to start is to copy the English locale (`app/src/renderer/src/locales/en.json` in the repository) and translate it. The file uses nested keys grouped by area:

```json
{
  "language": { "name": "Français", "code": "fr" },
  "settings": { "theme": "Thème" },
  "shell": { "binder": "Classeur" }
}
```

Two rules:

- **`language.name` is what the dropdown shows.** Write it in the language itself ("Français", "Español"). Leave it out and the file name is used instead.
- **Placeholders must survive translation.** Tokens like `{{version}}` and `{{count}}` are substituted at runtime; keep the names exactly as they appear in English.

A translation does not have to be complete. Any key you leave out falls back to English, so a file containing five strings is perfectly usable — you get your five, and the rest of the interface stays readable.

You can also use this to **patch a bundled language**. A file whose code matches one Novalist already ships (`de.json`, say) is merged over the built-in one key by key, so you can correct a term you dislike without maintaining a whole translation.

## Writing-language word lists

The Inspector's automatic emotion, intensity, conflict, and tag detection is keyword-driven, so it needs a word list for the language you are *writing* in — which need not be the language of your interface. Novalist ships lists for English, German, and Simplified Chinese.

Drop `analysis.<tag>.json` into `Analysis/` to add another, or to override a shipped one. Copy `Novalist.Core/Resources/Analysis/analysis.en.json` from the repository as your starting point, then:

- Translate the `positive`, `negative`, and `conflict` word lists. Entries are matched as stems, so `kaempf` also catches `kaempfen` and `kaempfte`.
- Translate the `words` of every `emotions` entry, but **keep the `key` values and their order exactly as in the English file**. Keys are stable identifiers that scenes store, so an existing scene's emotion stays valid when you change writing language.
- Translate `firstPerson` into your language's first-person pronouns; these drive first-person POV detection.
- Fill in `speechVerbs`, `pronounsMale`, `pronounsFemale`, `genderMale`, and `genderFemale` so the [Dialogue](33-dialogue.md) view can work out who is speaking.
- Set `wordBoundaries` to `false` for languages not written with spaces between words (as `analysis.zh-CN.json` does); leave it `true` otherwise.

A regional tag falls back to its base language, so `de-AT` uses `analysis.de.json`. A writing language with no list simply leaves those Inspector fields blank rather than guessing with another language's words.

## Themes from extensions

An extension can contribute themes as well, and they appear in the same dropdown. See [Extensions](24-extensions.md) for how to write one — the format is the same token map or stylesheet described above, declared through `IThemeContributor` instead of dropped in a folder.

## Sharing what you made

A theme or locale file is a single self-contained file: send it to someone, and they drop it into the same folder and press Rescan. If you would like a translation shipped with Novalist rather than passed around, see [Localization](27-localization.md) for how to contribute it upstream.

## Where to go next

- [Settings](23-settings.md) — where themes and languages are picked.
- [Localization](27-localization.md) — the bundled languages, and contributing a translation upstream.
- [Extensions](24-extensions.md) — contributing a theme from an extension.
- [Inspector](22-context-sidebar.md) — what the writing-language word lists drive.
