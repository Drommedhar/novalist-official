# Plot Grid & Plotlines

A **plotline** is a thread that runs through your story. The romance, the mystery, the political subplot, the protagonist's internal arc — each can be a plotline.

The **Plot Grid** is a spreadsheet-like view that shows every plotline as a row and every scene as a column. Cells let you mark which scenes belong to which plotline. Use it to see structure at a glance, spot threads that have been dropped, or check that subplots have setup and payoff.

## Opening the Plot Grid

Open it from:

- **View → Plot Grid**.
- The command palette (`Ctrl+Shift+P` → "Plot Grid").
- Its hotkey (see [Hotkeys](26-hotkeys.md)).

The grid replaces the editor in the main content area.

## Anatomy of the grid

- **Rows** — one per plotline in the active book. Each row shows the plotline's **color swatch**, **name**, and **description**.
- **Columns** — one per scene, in story order. Columns are grouped by chapter; group headers show the chapter title. Column labels look like `Ch1 - Sc1`, `Ch1 - Sc2`, `Ch2 - Sc1`, etc.
- **Cells** — a tick marks that the scene contributes to the plotline. Click to toggle.

Color-coded backgrounds reflect each plotline's color, making it visually obvious which threads are dense vs sparse.

## Plotlines

A plotline has:

- **Name** — short label.
- **Color** — pick from a swatch.
- **Description** — optional longer notes.
- **Order** — its row position in the grid; drag to reorder.

### Adding a plotline

Click **+New plotline** in the grid toolbar. An input dialog asks for the name. The plotline appears as a new row at the bottom of the grid, with a default color.

### Renaming, recoloring, deleting

Click the plotline name in the grid header → context menu:

- **Rename**
- **Change color**
- **Edit description**
- **Delete**

Deletion removes the plotline and clears it from any scenes that referenced it.

### Reordering

Drag plotline rows up and down to change their order. Order affects display only; nothing semantic.

## Marking scenes

Click a cell at the intersection of a plotline and a scene to toggle that scene's membership in the plotline. The cell highlights with the plotline color.

A scene can belong to any number of plotlines. The list of plotline IDs is stored on `SceneData.plotlineIds`.

You can also edit a scene's plotline memberships from the scene's right-click menu in the Explorer (**Plotlines →** ...) without opening the grid.

## Filtering and reading

Once your scenes are tagged you can:

- See plotline coverage at a glance — a row of mostly-empty cells is a thread that has gone quiet.
- Use a **Smart List** to filter scenes by plotline (e.g. "all scenes in the Mystery plotline"). See [Smart Lists](16-smart-lists.md).
- Filter the **Manuscript** view to one plotline (if implemented in your installed version).
- Group **Timeline** events by plotline (when the chapter or scene has a plotline tag).

## Tips

- **Have a thin plotline for everything.** Even the protagonist's internal arc benefits from being a plotline row — you can spot stretches where you forgot to advance it.
- **Use color consistently.** Reserve hot colors (red, orange) for high-tension threads and cool colors (blue, green) for quieter ones.
- **Re-plot after a draft.** After finishing a draft, walk the grid scene by scene and tick what each scene actually did. The resulting grid is the truth of the manuscript, not your outline's prediction.

## Where to go next

- [Chapters & Scenes](04-chapters-and-scenes.md) — scenes hold the plotline references.
- [Manuscript view](10-manuscript.md) — read scenes filtered by status; combine with Smart Lists for plotline-filtered reads.
- [Timeline](12-timeline.md) — chronological view including plotline color cues.
