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
- **Synopses, notes and comments** — search each scene's synopsis, its notes, and the text of its comments alongside the prose. Off by default.
- **Codex entries** — search Codex entry names and the prose in their sections. Off by default.

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
- A **label** — Synopsis, Notes, Comment or Codex — when the match is not in the prose.
- A **snippet** of surrounding text with the match highlighted.

Click a result to close the dialog and open that scene in the editor. A Codex result reports where the entry is rather than opening a scene — open the entry from the Codex or the Wiki.

## Replacing

Click **Replace All** to replace every match in the selected scope. With **Synopses, notes and comments** on, a replace also rewrites synopses and notes. Comments are left alone — a comment is a conversation, and rewriting someone's words in it is not a search-and-replace decision. Before any scene is modified, an **automatic snapshot** of that scene is taken, so a bad Replace All can always be undone by restoring the snapshot from the toolbar Snapshots dialog. After the run, the dialog reports how many occurrences were replaced, and the open scene reloads with the new content.

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

- Find searches **scene content**, and — when the matching options are on — synopses, notes, comments, and Codex entries. Markup tags themselves are not searched.
- Find does not search custom properties or research notes. Those are accessible via their own views, and by Quick Open.
- **Codex matches are reported, never replaced.** Renaming a Codex entry has its own command, which carries the change through every reference to it; a blind replace here would not.
- Replace All takes one snapshot per modified scene, so a project-wide replace can produce many snapshots.
- **Whole project** opens each book in turn to read it, and puts you back in the book you started in when it finishes. On a large multi-book project that takes a moment; a search of the active book does not.

## Tips

- **Preview before Replace All.** Run Find first, scroll the result list, then Replace All only if the matches all look right.
- **Use whole-word for character renames.** Renaming a character "Jon" to "John" without whole-word will mangle "Jonathan". Always toggle Whole word for name changes.
- **Use regex for stylistic sweeps.** "Find every `was` followed by an -ing verb" is a regex job. Replacement is usually manual.
- **Commit before a project-wide replace.** Snapshots cover individual scenes; Git covers the whole project. Both are good before a bulk operation.

## Cleaning up a whole manuscript

Novalist's auto-replacements fire while you type, and they deliberately skip pasted text — a paste is somebody else's formatting, and rewriting it as it lands would be a surprise. The consequence is that a chapter written in another program and pasted in keeps its straight quotes, its double hyphens and its double spaces permanently. Find and Replace can be pointed at each of those, one pattern at a time, if you know what to look for.

**Clean up the manuscript** does the whole set in one pass. Open it from the command palette. It has no default hotkey: it rewrites the prose in every scene it touches, and a pass that big should be reached on purpose rather than by a mistyped chord.

Six rules, each independent and all on by default:

- **Curl straight quotes and apostrophes.** Quotes alternate open and closed, in the pair your book's writing language actually uses — a German manuscript gets low-9 quotes, not English ones. An apostrophe is never treated as a closing quote: "don't" and "the boys' coats" are ordinary prose. A quote that is neither clearly an elision nor clearly a quotation — `'73`, say — is left exactly as you typed it, because guessing wrong is worse than doing nothing.
- **Turn double hyphens into dashes and three dots into an ellipsis.** The same table the typing-time replacements use, so the two cannot disagree.
- **Collapse repeated spaces**, including the double space after a full stop. A non-breaking space is left alone — those are authored, not stray.
- **Trim spaces** left hanging at the start and end of a paragraph.
- **Drop paragraphs that hold nothing.** A paragraph holding only an image or a horizontal rule is kept: it carries no text and is still the point of the paragraph it sits in.
- **Make every scene break the same.** A paragraph that is only asterisks, hyphens, hashes or bullets becomes the one canonical break.

Markup is never touched. A straight quote inside `class="..."` is not a quotation mark, and the spaces inside a style attribute are not prose.

If you have unticked **Replace quotes and dashes as I type** in Settings → Writing assistance, the first two rules are greyed out and cannot be run. They are the same substitutions the switch turned off, and a cleanup pass would apply them to the whole book in one go. The other four are unaffected.

**Show me what would change** reports how many scenes the pass would rewrite and names them, without changing anything. Use it first — this is not something to find out about afterwards.

**Clean up** runs the pass and takes a snapshot of every scene before it changes it, the same as Replace All. If a rule did something you did not want, the previous version is in [Snapshots](17-snapshots.md).

By default the pass covers the whole book. With a chapter open you can narrow it to that chapter instead.

## Where to go next

- [Editor](05-editor.md) — for live editing of the matches.
- [Snapshots](17-snapshots.md) — recover if a replace went wrong.
- [Smart Lists](16-smart-lists.md) — find scenes by metadata rather than content.
- [Quick Open](31-quick-open.md) — search Codex entries, notes, comments, and research too, not just scene prose.
