# Relationships graph

The Relationships view draws your characters as nodes and the relationships between them as labeled edges. It clusters families automatically, so big casts remain legible.

## Opening the Relationships view

Open it from the **Plan** group in the binder's view rail (**Relationships**), from the command palette, or with `Ctrl+7` (macOS uses Cmd).

## What you see

- **Nodes** — one per character that has at least one relationship. Characters with no relationships are hidden; if nothing is connected yet, the view tells you to add relationships in the Codex.
- **Edges** — lines between related characters, labeled with the relationship role (e.g. "Father", "Mentor", "Owes a debt to"). When two characters are linked by several roles, the labels combine into one edge ("Father / Mentor"). Family edges inside a cluster are drawn thin and unlabeled — the layout already says what they mean.
- **Family boxes** — related family members are enclosed in a soft box labeled `Family <surname>` (using the family's most common surname). Inside a box, generations are layered top to bottom: parents above children, partners on the same row.
- Characters connected only by non-family roles are arranged in a loose ring below the family clusters.

## How family clustering works

Novalist detects family roles by keyword: relationship roles containing words like "father", "mother", "son", "daughter", "spouse", "wife", "husband", "partner", "brother", "sister", "twin" (and their equivalents in every bundled language) are treated as family links. The keyword vocabularies live in the locale files under the `relationships` section and are merged across all bundled languages, so role matching works regardless of the UI language you write in.

Other roles — mentor, enemy, business partner, owes a debt to — appear as labeled edges without clustering.

## Toolbar

- **Search** — type to filter the graph to characters whose name matches.
- **Hide world-bible entities** — hide characters marked as world-bible, leaving only this book's cast.
- **Zoom readout** — shows the current zoom percentage.

**Zoom** with the mouse wheel (20% to 400%); **pan** by dragging the background.

## Adding and editing relationships

The graph reflects what is stored on each character entity. To add a relationship, open the character in the [Codex](06-codex.md), go to its **Relationships** section, and add a role plus one or more target characters. When you add a relationship, Novalist offers to add the matching inverse on the target character so both sides stay in sync.

Return to the Relationships view and the graph updates.

## Tips

- **Name roles with family words when you want clustering.** "Father", "wife", "half-sister" cluster; "patriarch of" does not. Keep an eye on which edges end up inside boxes.
- **Use ALL CAPS for high-signal roles.** A directed "OWES MONEY TO" edge reads at a glance.
- **For very large casts, search.** Filtering to a name and its connections is more useful than zooming out over a hundred nodes.

## Where to go next

- [Codex](06-codex.md) — where character relationships are edited.
- [Timeline](12-timeline.md) — track where those characters appear over story time.
