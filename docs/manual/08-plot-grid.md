# Plot Grid & Plotlines

A **plotline** is a thread that runs through your story. The romance, the mystery, the political subplot, the protagonist's internal arc — each can be a plotline.

The **Plot Grid** is a spreadsheet-like view that shows every plotline as a row and every scene as a column. Cells mark which scenes belong to which plotline. Use it to see structure at a glance, spot threads that have been dropped, or check that subplots have setup and payoff.

![The Plot Grid](images/plot-grid.png)

## Opening the Plot Grid

Open it from:

- The **Plan** group in the activity bar — click **Plot Grid**.
- The command palette (`Ctrl+Shift+P` → "Plot Grid").
- The hotkey `Ctrl+8` (macOS uses Cmd; see [Hotkeys](26-hotkeys.md)).

The grid fills the main area.

## Anatomy of the grid

- **Rows** — one per plotline in the active book. Each row header shows the plotline's **color swatch** and **name**.
- **Columns** — one per scene, in story order. The column header shows the scene title; hover it to see the full `Chapter - Scene` label, and click it to open that scene in the editor.
- **Cells** — click a cell to toggle whether that scene belongs to that plotline. Assigned cells fill with the plotline's color. **Right-click an assigned cell** to attach a short **note** — what this scene actually does for that thread ("sets up the betrayal", "pays off the ring"). A cell with a note carries a small corner marker, and hovering it shows the note in the tooltip. Submit an empty note to clear it.

Because assigned cells take the plotline color, it is visually obvious which threads are dense and which are sparse.

## Managing plotlines

### Adding a plotline

Click **Add plotline** in the grid toolbar and enter a name. The plotline appears as a new row with an automatically assigned color.

### Renaming and deleting

Right-click the plotline's row header:

- **Rename**
- **Delete**

Deleting a plotline removes its row and clears the assignment from every scene that referenced it (a confirmation dialog asks first).

## Marking scenes

Click a cell at the intersection of a plotline and a scene to toggle that scene's membership. A scene can belong to any number of plotlines. Assignments are stored on the scene and travel with the project.

A tick alone tells you a scene touches a thread, but not *how*. Right-click an assigned cell to add a one-line note saying what it contributes — reading a row of notes left to right is the fastest way to check that a subplot has setup, escalation, and payoff rather than three scenes that merely mention it.

## Reading the grid

Once scenes are tagged:

- A row of mostly empty cells is a thread that has gone quiet — check whether that is intentional.
- A column with no marks is a scene that advances nothing you are tracking.
- Clusters of one color show where a subplot takes over; long gaps between marks show where it disappears.

## Tips

- **Have a thin plotline for everything.** Even the protagonist's internal arc benefits from being a row — you can spot stretches where you forgot to advance it.
- **Re-plot after a draft.** After finishing a draft, walk the grid scene by scene and tick what each scene actually did. The resulting grid is the truth of the manuscript, not your outline's prediction.

## Where to go next

- [Chapters & Scenes](04-chapters-and-scenes.md) — scenes hold the plotline assignments.
- [Manuscript view](10-manuscript.md) — read the book continuously, filtered by chapter status.
- [Timeline](12-timeline.md) — the chronological view of the same story.
