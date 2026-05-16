# Novalist — an offline, open-source desktop app for writing novels

**Download (latest release):** https://github.com/Drommedhar/novalist-official/releases/latest
**Source:** https://github.com/Drommedhar/novalist-official

Hi r/writers,

I'm a hobby novelist and a software developer. I started writing a book a couple of months ago and kept bouncing between a word processor, a spreadsheet for the cast, a folder of text files for worldbuilding, and a separate timeline tool. Every revision pass meant re-syncing the same information across four places. So I built the tool I wished existed, and over time it grew into something other writers might find useful too. It's free, open-source (MIT). Windows and macOS builds are available now; a Linux build is planned but not yet shipped.

This post is the first proper announcement. I'd love feedback from people who actually write.

## What it is

Novalist is a single desktop app that holds the whole novel project — manuscript, worldbuilding, plotting, timeline, research, exports — in one folder of plain files on your disk. No cloud account. No subscription. No proprietary container. The folder is yours: back it up, sync it with whatever you already use, inspect the files (HTML scenes, JSON manifests) with standard tools.

## What you can do with it

**Writing**
- WYSIWYG editor with paragraph styles (heading, subheading, blockquote, poetry), inline comments, numbered footnotes.
- Per-scene snapshot history with side-by-side compare — revert a single scene without touching the rest of the project.
- Focus mode, split editor (two scenes side by side), auto-replacements with language presets (EN, DE, FR, ES, IT, PT, RU, PL, CZ, SK), dialogue punctuation correction, grammar/spell check via LanguageTool.
- Live word count, reading time, Flesch readability in the status bar; per-chapter readability in the project overview.

**Project structure**
- Multi-book projects with a shared World Bible for entities used across books.
- Chapters with status (Outline → First Draft → Revised → Edited → Final), acts, in-world date ranges, label colors, favourites.
- Scenes with synopsis, notes, POV / emotion / intensity / conflict / tags (auto-detected, manually overridable).
- Smart Lists — saved scene queries by status, POV, tag, or plotline.

**Worldbuilding (the Codex)**
- Characters, Locations, Items, Lore — plus custom entity types you define yourself (Factions, Spells, Races, whatever you need) with custom field schemas and templates.
- Per-act / per-chapter / per-scene overrides for any field, so a character's hair length, allegiance, or marital status can change as the story progresses without losing the earlier state.
- Relationships with auto-learned inverse roles (mark A as "father of" B, the inverse "child of" is offered automatically).
- Focus peek — hover an entity name in the editor, get a card.

**Planning & visualisation**
- Plot Grid — spreadsheet of plotlines (rows) × scenes (columns); one click toggles a scene's membership in a thread.
- Timeline — chronological view of acts, chapters, scenes, and manual events with zoom from day to year.
- In-world Calendar — Gregorian calendar view with Week, Month, and Year layouts; scenes and events show up on their in-world dates.
- Relationships graph — auto-clustered force-directed graph of your cast, with family detection.
- Manuscript view, Corkboard (index cards), Outliner (sortable table).
- Maps — layered interactive map: drag-and-drop layer tree, polygon clip masks, entity-linked pins, road/river splines with typed profiles, terrain with feathered blending, procedurally-generated buildings that snap to roads, optional multi-floor interiors, and a one-click 3D walkthrough mode. (3D is GPU-bound — runs smooth on a mid-range discrete GPU; on a base M4 MacBook Air it sits around 30–40 fps. Usable, not buttery.)
- Dashboard — totals, status breakdown, chapter pacing, echo phrases, daily and project word goals with deadlines.

**Output**
- Export to EPUB, DOCX, PDF, Markdown, Final Draft / Fountain, LaTeX, and Codex Markdown.
- Built-in Shunn Modern Manuscript Format preset for submissions.

**Other**
- Built-in Git client (stage, commit, push, pull from the app) — complementary to the per-scene snapshot history.
- Find & Replace with regex, scoped to scene / selection / book / whole project.
- Command palette (Ctrl+Shift+P), fully rebindable hotkeys, light/dark + accent colour, system or custom theme.
- Plugin SDK — .NET 8 extensions can add ribbon items, sidebar tabs, full views, export formats, editor hooks, custom entity types, themes.

## How it works, briefly

Open the app, hit New Project, point it at an empty folder. Inside that folder you'll see a `Books/` directory (your chapters and scenes as HTML files, entity JSON next to them) and a `.novalist/` directory (project manifests). That's the whole format. You can commit the folder to Git, sync it through any file-sync service, copy it to a USB stick. Novalist just edits the files.

## Why you might want it

- **Your files are yours.** HTML scenes and JSON manifests on your disk — no proprietary database, no vendor lock-in. Export to EPUB / DOCX / PDF / Markdown anytime, so the manuscript is never trapped in the app.
- **Offline-first.** No account, no telemetry. Grammar check goes to LanguageTool (public endpoint by default, self-hostable, or turn it off entirely); nothing else phones home that we know of.
- **One app for the whole project.** Manuscript, worldbuilding, plotting, timeline, calendar, maps, exports — same window, same shortcut, same search box.
- **Per-act / per-chapter / per-scene character state.** Most outliners treat a character as one snapshot. Novalist lets a character's traits *evolve* across the book at whatever granularity you need — act, chapter, even individual scene — without losing prior values. Useful for long arcs, time-skips, mid-book reveals.
- **Custom entity types.** If you're writing secondary-world fantasy or sci-fi with its own taxonomy of magic, factions, ships, or species, define your own entity types with custom field schemas instead of bending a Character into shape.
- **Extensible.** Real plugin SDK, not just a colour theme system. If a feature doesn't exist you can write it.
- **Free and open source (MIT).** Fork it, modify it, never pay for it.

## Honest caveats

It was built originally to help me write my own book and is shared as-is. There's no guaranteed release cadence — updates land when I have time. There's no commercial support. Bugs exist. Pull requests are welcome and I do read issues.

## Get it

- **Download (latest release):** https://github.com/Drommedhar/novalist-official/releases/latest
- **Source:** https://github.com/Drommedhar/novalist-official
- **User manual:** [docs/manual/README.md](https://github.com/Drommedhar/novalist-official/blob/main/docs/manual/README.md) — one page per feature.
- **Extension guide:** [docs/extension-guide.md](https://github.com/Drommedhar/novalist-official/blob/main/docs/extension-guide.md)

If you try it, I'd genuinely like to hear what feels missing, what gets in your way, and which parts you'd actually use. Happy to answer questions in the comments.
