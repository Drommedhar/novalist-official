# Relationships graph

The Relationships view draws every character in the active book as a node and every relationship between them as a labeled edge. It auto-clusters families and shared groups, so big casts remain legible.

## Opening the Relationships view

Click the **user** icon in the activity bar, or use the Relationships hotkey.

## What you see

- **Nodes** — one per character. Each node shows the character's primary image (or initials) and their display name.
- **Edges** — directed lines for each relationship, labeled with the relationship role (e.g. "Father", "Mentor", "Owes a debt to").
- **Group boxes** — characters in the same family or `Group` are visually clustered with a soft outline.

The layout is force-directed: well-connected characters pull together, weakly-connected ones drift apart. Drag any node to pin it.

## How clustering works

Novalist detects family roles automatically via keyword matching:

- English roles: father, mother, parent, child, son, daughter, sibling, brother, sister, spouse, husband, wife, partner.
- German roles: Vater, Mutter, Eltern, Kind, Sohn, Tochter, Geschwister, Bruder, Schwester, Partner, Ehefrau, Ehemann.

Characters with these connections are grouped into family clusters. Characters with the same `Group` field (see [Codex](06-codex.md)) are clustered together too.

Other relationship roles (mentor, enemy, business partner, owes a debt to) appear as labeled edges without grouping.

## Filtering

The toolbar offers:

- **Search** — filter by name.
- **Group filter** — show only characters in a given Group.
- **Role filter** — show only characters in a given Role (Protagonist, Antagonist, etc.).
- **Hide world-bible characters** — toggle to hide entities marked as world-bible from the current book's view.

## Interacting with nodes

- **Click a node** — opens the character editor in a new tab.
- **Hover** — preview the character's focus-peek card.
- **Drag a node** — repositions it; the rest of the graph re-layouts.
- **Double-click** — pin / unpin (some builds).

## Adding and editing relationships

The graph reflects what's stored on each character entity. To add a relationship:

1. Open the source character editor.
2. Go to the **Relationships** section.
3. Click **Add relationship**.
4. Type the role description (e.g. "Father", "Best friend", "Owes 5000 gold to") in the **description** field.
5. Pick the target character(s) — the **target** field accepts a comma-separated list of names with autocomplete.
6. Save.

When you save, Novalist may prompt the **Inverse Relationship Dialog** asking whether to add the inverse on the target character. After a few rounds it learns your project's role pairs and prompts less.

## Tips

- **Use Group field for non-family clusters.** Houses, factions, ships' crews — set a consistent Group value and the graph groups them automatically.
- **Use ALL CAPS for roles when noise is high.** A directed "OWES MONEY TO" edge reads at a glance.
- **For very large casts, filter by Group.** Force-directed graphs become spaghetti past ~40 nodes; filtering is more useful than zooming.

## Where to go next

- [Codex](06-codex.md) — where character relationships are edited.
- [Settings](23-settings.md) — Novalist remembers relationship-pair inverses you confirm; you can also seed them.
