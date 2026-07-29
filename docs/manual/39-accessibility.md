# Accessibility

This page states plainly what Novalist does for readers and writers with access needs, and what it does not do yet. It is written to be checked against the app rather than to reassure.

## Reading comfort

Everything here is in **Settings → Editor**, and every one of them applies to the scene editor, to [Manuscript mode](10-manuscript.md), and to the [Exposé](32-expose.md).

- **Font Family** — any typeface installed on the machine, including an accessible face such as Atkinson Hyperlegible or OpenDyslexic once you install it. Novalist bundles Newsreader, Fraunces and Courier Prime; anything else has to be present on the system, because a font is a licensed binary and Novalist ships only the three it has licences for.
- **Font Size** — 8 to 36 pixels.
- **Line Height** — 1 to 2.5. Opening the lines up is usually the single most effective change for text that is hard to track.
- **Letter Spacing** — -1 to 4 pixels.
- **Paragraph Spacing** — 0 to 3 ems, used when **Book Paragraph Spacing** is on.

All five can be set globally or [pinned to one project](23-settings.md#global-vs-per-project-settings), so a book you find hard to read can carry its own settings.

## High Contrast theme

**Settings → Appearance → Theme → High Contrast** is pure white text on a pure black ground, with borders that are actually visible rather than hairlines. Every text-on-background pair in it clears WCAG AAA; the other bundled palettes are pigment-based and do not attempt that.

Themes in Novalist restate colour only — type, spacing and corner radii belong to the application's identity. That is why line height and letter spacing are settings rather than something a theme could carry: a theme cannot reach them, so they had to be reachable somewhere else.

You can also write your own palette; see [Custom themes & language packs](34-custom-themes-and-languages.md).

## Read aloud

The speaker button at the right-hand end of the editor toolbar reads the open scene back to you, starting from the paragraph your caret is in. The sentence being spoken is highlighted, and the editor scrolls to keep it in view.

- Click the button again, press `Escape`, or start typing to stop.
- **Settings → Editor → Read-aloud speed** sets the pace (0.5 to 2).
- **Settings → Editor → Read-aloud voice** picks from the voices your operating system has installed. Left on **Match the writing language**, Novalist asks for a voice in the language the scene is written in, so a German scene is read in German without being told twice.

Read-aloud uses the voices already on your machine and sends nothing anywhere. If the list is empty, install voices through your operating system's speech settings — Windows and macOS both ship a voice manager.

The highlight is painted without touching the document, so listening to a chapter never marks it as edited.

Dictation is not implemented. Use your operating system's own dictation into the editor in the meantime.

## Motion

Novalist honours `prefers-reduced-motion`. With reduced motion set at the operating-system level, transitions and animated reveals are suppressed.

## Keyboard

Every shell command has a keyboard route: the [command palette](25-command-palette.md) (`Ctrl+Shift+P`) reaches all of them by name, and the [hotkeys](26-hotkeys.md) are all rebindable in **Settings → Hotkeys**. The editor itself is a standard text surface with the usual caret and selection keys.

## Screen readers

Novalist does not yet claim screen-reader support, and we would rather say so than imply it.

- Dialogs and overlays carry `role` and `aria-label`.
- The shell chrome — activity bar, binder, context menus — is only partially labelled.
- The editor is a `contenteditable` surface inside an iframe with no ARIA annotation, so a screen reader will read the text but not the structure around it.

If you use a screen reader with Novalist, please open an issue describing what broke; that is the fastest route to it being fixed.

## Describing images

Every picture gets two pieces of text, and they are not the same thing:

- **Name** — which image this is, for you. "Mira Vance".
- **What this image shows** — what a reader who cannot see it gets instead. "A woman in a soaked coat on a harbour wall."

Only the second is any use read aloud, and only the second reaches an export. Codex images have both fields; an [image in the prose](05-editor.md#images-in-the-prose) is asked for its description when you insert it.

Before an export runs, the [Export view](20-export.md#what-this-export-will-contain) reports how many pictures have no description, so an undescribed image is something you decide about rather than something you discover afterwards.

## Accessible EPUB metadata

An exported EPUB declares what it actually contains: `schema:accessMode`, `accessModeSufficient`, `accessibilityFeature`, `accessibilityHazard` and a plain-language `accessibilitySummary`. A book with no images says it is text only; a book with images says its pictures carry the descriptions the author wrote.

The declaration is built from the file rather than asserted, because claiming alt text that is not there is worse than claiming nothing — and under the European Accessibility Act it is a claim a distributor relies on.

## What is missing

Stated so nobody has to discover it the hard way:

- No screen-reader support statement beyond the section above, and no accessibility conformance report.
- No dictation.
- No EPUBCheck or DAISY Ace validation of an exported file, and no tagged PDF/UA output.

## Where to go next

- [Settings](23-settings.md) — where every control on this page lives.
- [Editor](05-editor.md) — the writing surface itself.
- [Custom themes & language packs](34-custom-themes-and-languages.md) — write a palette that suits your eyes exactly.
