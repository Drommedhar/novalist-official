# Quick Open (search everything)

**Quick Open** is one search box over everything you have written. Press `Ctrl+P` (`Cmd+P` on macOS), type a couple of characters, and Novalist searches your whole project at once — then takes you straight to whatever you pick.

It exists to answer the question that used to have no answer: *"I wrote something about the frost oath ritual — where is it?"* Before, you had to guess whether it lived in a scene, a lore entry, a scene note, a comment, or a research note, and hunt through each surface separately.

## What it searches

Everything below, in one query:

- **Scene titles**
- **Scene prose** — the text of every scene in the project
- **Synopses and notes** — the scene-notes dock fields
- **Comments and footnotes** — including the passage a comment is anchored to
- **Codex entries** — names, aliases, field values (role, description, type, origin, category, and so on), custom properties, and the text of free-form sections, across characters, locations, items, lore, and every custom entity type
- **Research items** — title, body, and tags
- **Timeline events** — the title and description of manual events

Matching is case-insensitive substring matching; there is no special syntax to learn. For regular expressions, whole-word matching, and bulk replacement over scene prose, use [Find & Replace](21-find-replace.md) instead — the two tools are complementary.

## Reading the results

Results are grouped by where they came from, in this order: Scenes, Codex, In scene text, Synopses and notes, Comments and footnotes, Research, Timeline events. Each group is capped so that one noisy source cannot crowd out the rest.

Every result shows its title, a context line (the chapter for a scene, the entity type for a Codex entry, the item type for research), and — where the match was inside a longer text — a short **snippet** with the matching words in context.

## Opening a result

Click a result, or move through them with the arrow keys and press `Enter`. What happens depends on the kind:

- A **scene**, **scene text**, **note**, **comment**, or **footnote** hit opens that scene in the editor.
- A **Codex** hit opens that entity's article in the [Wiki](30-wiki.md) (from there, **Edit in Codex** reaches the editable record).
- A **research** hit opens the [Research](15-research.md) view with that item selected.
- A **timeline** hit opens the [Timeline](12-timeline.md).

`Esc` closes the box without navigating.

## Quick Open vs. the Command Palette

They look similar and are deliberately separate:

- **Quick Open** (`Ctrl+P`) finds **content** — your scenes, characters, notes, research.
- The [Command Palette](25-command-palette.md) (`Ctrl+Shift+P`) runs **commands** — switching views, toggling panes, and every other action in the app.

## Tips

- **Two characters minimum.** Searching scans every scene file, so Quick Open waits until you have typed at least two characters and briefly pauses after your last keystroke before running.
- **Search for a name to reach a character fast.** Codex hits rank near the top, so `Ctrl+P` and a few letters of a name is usually the quickest route to a character's article.
- **Search your own notes, not just your prose.** Notes, comments, and footnotes are where "fix this later" lives — Quick Open is the only surface that reads them all at once.

## Narrowing the search

A query is more than a word. The syntax is Scrivener's and Obsidian's, so if you know either you already know this:

| Typed | Finds |
| --- | --- |
| `bell` | Anything with "bell" in its title, prose or notes. |
| `bell tower` | Only things with **both** words. |
| `"the bell tolled"` | The phrase, in that order. |
| `title:bell` | Only where "bell" is in the title. |
| `text:bell` | Only in the prose. `body:` works too. |
| `notes:bell` | Only in a synopsis, notes, comment or footnote. `synopsis:` works too. |
| `tag:night` | Scenes carrying that tag. |
| `kind:scene` | Only scenes. Also `entity`, `research`, `timeline`. |
| `-draft` | Excludes anything with "draft" in it. |
| `-title:draft` | Excludes it from the title only, leaving the prose alone. |

Anything that is not one of those is searched for as written, so a stray colon looks for itself rather than failing. `chapter:one` finds the literal text "chapter:one".

Results are **ranked**: a title match outranks a match in the prose, an exact title outranks one that merely contains the word, matching every term outranks matching one, and an earlier match outranks a later one. What you were most likely looking for is at the top.

## Where to go next

- [Find & Replace](21-find-replace.md) — regex, whole-word, scoped search, and replace-all over scene prose.
- [Command palette](25-command-palette.md) — the command-running counterpart.
- [Wiki](30-wiki.md) — where Codex results open.
- [Hotkeys](26-hotkeys.md) — the full keybinding list.
