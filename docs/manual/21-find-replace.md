# Find & Replace

Find & Replace lets you search across one scene, the current book, or every book in the project. It supports plain text, whole-word, case-sensitive, and regular-expression matching, and can replace matches in bulk.

## Opening Find & Replace

- **Edit → Find & Replace**, or
- the hotkey `Ctrl+H` (default), or
- the command palette → "Find & Replace".

The Find & Replace dialog opens in-window.

## Fields

- **Find** — the pattern to search for. If **Regex** is on, this is a .NET regular expression; otherwise it's a literal string.
- **Replace with** — the replacement string. If regex is on, you can use capture-group back-references (`$1`, `$2`, etc.).
- **Match case** — case-sensitive search when on.
- **Whole word** — only match whole words. Word boundaries follow Unicode rules.
- **Regex** — treat the find pattern as a regex.

## Scopes

A radio selector at the top picks the search scope:

- **Current scene** — only the scene currently open in the editor.
- **Current chapter** — every scene in the chapter the active scene belongs to.
- **Active book** — every scene in the active book.
- **All books** — every scene in every book in the project.

The default is **Active book**.

## Performing a search

Click **Find** or press `Enter`. The results panel lists every match:

- **Book** (if scope is All books).
- **Chapter** and **Scene** the match is in.
- A **snippet** of surrounding text with the match highlighted.

Click a result to jump to that scene in the editor with the match selected.

## Replacing

After running Find, you can replace every match in the current scope with **Replace all**. You are asked to confirm if there are many matches.

Replacements are saved on the next auto-save (or by `Ctrl+S`). Snapshots cover replacement operations, so you can undo a bad Replace All by restoring a snapshot.

## Regex notes

- The engine is .NET regex.
- Use `(?i)` inline for case-insensitive within the pattern.
- Use `\b` for word boundaries (or just toggle **Whole word**).
- Multi-line patterns: set `(?s)` to make `.` match newlines.
- Back-references in the replacement use `$1`, `$2`, etc.

Common patterns:

- Convert straight quotes around dialogue to curly: `"([^"]+)"` → `"$1"` (with smart quotes pre-applied via your auto-replacement preset).
- Add a comma before "but" / "and" between clauses (style-dependent, audit replacements): `([a-z]) (but|and) ` → `$1, $2 `.
- Find any "very + adjective" cliché: `\bvery (\w+)`.

## Limitations

- Find searches **scene HTML content**. HTML tags themselves are not searched (you can't search for `<em>` directly via this dialog).
- Find does not search entity fields, sections, custom properties, research notes, or comments. Those are accessible via their own views.
- Replace All within scenes triggers snapshots per scene, so a project-wide replace can produce many snapshots.

## Tips

- **Preview before Replace All.** Use Find first, scroll the result list, then Replace All only if the matches all look right.
- **Use whole-word for character renames.** Renaming a character "Jon" to "John" without whole-word will mangle "Jonathan". Always toggle Whole word for name changes.
- **Use regex for stylistic sweeps.** "Find every two consecutive `was` or `were` in a sentence" is a regex job. Replacement is usually manual.
- **Commit before a project-wide replace.** Snapshots cover individual scenes; Git covers the whole project. Both are good before a bulk operation.

## Where to go next

- [Editor](05-editor.md) — for live editing of the matches.
- [Snapshots](17-snapshots.md) — recover if a replace went wrong.
- [Smart Lists](16-smart-lists.md) — find scenes by metadata rather than content.
