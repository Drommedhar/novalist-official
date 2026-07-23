# Find & Replace

Find & Replace lets you search across one scene, one chapter, the active book, or the whole project. It supports plain text, whole-word, case-sensitive, and regular-expression matching, and can replace matches in bulk — with automatic snapshots taken first.

## Opening Find & Replace

- The **Search** button (magnifier) on the toolbar, or
- `Ctrl+Shift+F` (`Cmd+Shift+F` on macOS), or
- the command palette (`Ctrl+Shift+P`) → "Find and Replace".

The Find and Replace dialog opens in-window.

## Fields

- **Find** — the pattern to search for. If **Regex** is on, this is a regular expression; otherwise it's a literal string.
- **Replace** — the replacement string. If regex is on, you can use capture-group back-references (`$1`, `$2`, etc.).
- **Match case** — case-sensitive search when on.
- **Whole word** — only match whole words.
- **Regex** — treat the find pattern as a regex.

## Scopes

A scope selector next to the options picks the search range:

- **Current scene** — only the scene currently open in the editor.
- **Current chapter** — every scene in the chapter the open scene belongs to.
- **Active book** — every scene in the active book.
- **Whole project** — every scene in every book.

The default is **Active book**.

## Performing a search

Click **Find** or press `Enter`. The result list shows every match with:

- The **chapter and scene** the match is in.
- A **snippet** of surrounding text with the match highlighted.

Click a result to close the dialog and open that scene in the editor.

## Replacing

Click **Replace All** to replace every match in the selected scope. Before any scene is modified, an **automatic snapshot** of that scene is taken, so a bad Replace All can always be undone by restoring the snapshot from the toolbar Snapshots dialog. After the run, the dialog reports how many occurrences were replaced, and the open scene reloads with the new content.

## Regex notes

- The engine is .NET regular expressions, so .NET syntax (including inline flags) applies.
- Use `(?i)` inline for case-insensitive within the pattern.
- Use `\b` for word boundaries (or just toggle **Whole word**).
- Multi-line patterns: set `(?s)` to make `.` match newlines.
- Back-references in the replacement use `$1`, `$2`, etc.

Common patterns:

- Find any "very + adjective" cliche: `\bvery (\w+)`.
- Add a comma before "but" / "and" between clauses (style-dependent, audit replacements): `([a-z]) (but|and) ` → `$1, $2 `.

## Limitations

- Find searches **scene content**. Markup tags themselves are not searched.
- Find does not search entity fields, sections, custom properties, research notes, or comments. Those are accessible via their own views.
- Replace All takes one snapshot per modified scene, so a project-wide replace can produce many snapshots.

## Tips

- **Preview before Replace All.** Run Find first, scroll the result list, then Replace All only if the matches all look right.
- **Use whole-word for character renames.** Renaming a character "Jon" to "John" without whole-word will mangle "Jonathan". Always toggle Whole word for name changes.
- **Use regex for stylistic sweeps.** "Find every `was` followed by an -ing verb" is a regex job. Replacement is usually manual.
- **Commit before a project-wide replace.** Snapshots cover individual scenes; Git covers the whole project. Both are good before a bulk operation.

## Where to go next

- [Editor](05-editor.md) — for live editing of the matches.
- [Snapshots](17-snapshots.md) — recover if a replace went wrong.
- [Smart Lists](16-smart-lists.md) — find scenes by metadata rather than content.
- [Quick Open](31-quick-open.md) — search Codex entries, notes, comments, and research too, not just scene prose.
