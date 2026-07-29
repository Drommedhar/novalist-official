# Planning board

The **Planning board** is an infinite surface for ideas that are not yet scenes: loose cards you can drag anywhere, and lines you draw between them with your own labels. Open it from the activity bar (Plan group).

Everything else in Novalist that shows relationships works it out for you — the [Relationships graph](14-relationships.md) lays characters out automatically, the [Timeline](12-timeline.md) orders real chapters and scenes, [Maps](29-maps.md) place pins on geography. The board is the opposite: nothing on it is derived, and nothing on it is part of your manuscript. That is what makes it usable for a half-formed thought.

## Boards

A project can hold as many boards as you like — one per act, one per subplot, one for the thing you have not worked out yet. Pick one from the drop-down at the top, or press **New board**. **Delete board** removes the board in view along with its cards and connectors, after asking; scenes you already promoted from it are unaffected, because they are real scenes in the binder by then.

Boards are stored one JSON file each in a `Canvases/` folder beside `Maps/` in the active draft, so they travel with the project, sync with it, and are covered by [backups](35-backups.md) and Git like everything else.

## Cards

**Add card** drops a new card near the top left. Each card has a title and a body, and both are free text — a card can be a scene you might write, a question you cannot answer yet, a line of dialogue you do not want to lose, or a note to yourself.

Drag a card by its body to move it. Click into the title or the body to type; typing does not move the card.

Changes save automatically two seconds after you stop, the same as the editor.

## Connectors

Select a card, press **Connect...**, then click a second card. A line is drawn between them.

The label on a connector is the point. "Because of", "three weeks later", "but only if she lied" — the line records *why* two ideas are related, which is the part you forget. An unlabelled connector is allowed; it just says less.

Deleting a card also removes every connector attached to it, so the board never draws a line to nothing.

## Turning a card into a scene

**Make this a scene** promotes the selected card into a real scene in the first chapter:

- The card's **title** becomes the scene's title. A card with no title gets "Untitled".
- The card's **body** becomes the scene's **synopsis**, not its prose. A planning note describes a scene; it is not the scene itself, and dropping it into the manuscript as text would be a lie about your word count.
- The card **stays on the board**, marked as being in the manuscript, and keeps pointing at the scene it became. The board remains the map of your thinking; promoting does not consume the card.

Promoting the same card twice does nothing the second time — it already has a scene.

This is the only point at which a board touches your book. Until then, nothing on the board counts towards a word goal, appears in the binder, or shows up in an export.

## Where to go next

- [Chapters & Scenes](04-chapters-and-scenes.md) — where a promoted card ends up.
- [Plot Grid & Plotlines](08-plot-grid.md) — for tracking threads once the scenes exist.
- [Relationships graph](14-relationships.md) — the automatic view of how characters connect.
- [Timeline](12-timeline.md) — chronological order once ideas have become scenes.
