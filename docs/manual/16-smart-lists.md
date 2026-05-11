# Smart Lists

A **Smart List** is a saved query over your scenes. Instead of manually keeping a folder of "scenes to revise" or "scenes with Alice POV that aren't first-draft yet", you define the criteria once and the list updates itself as your project changes.

## Where Smart Lists appear

Smart Lists are in the **Smart Lists** tab of the left sidebar. Click it (or use the sidebar tab hotkey) to see all saved lists. Each list shows:

- **Name** — your label.
- **Color** — optional swatch.
- **Result count** — how many scenes currently match.

Click a list to expand it: matching scenes appear underneath, grouped just like in the chapter Explorer. Click a scene to open it.

## Creating a Smart List

Click **+New smart list** at the top of the panel. The Smart List Editor dialog opens.

Fields:

- **Name** — required.
- **Color** — optional. Used as a small swatch in the panel.
- **Chapter status filter** — match scenes whose chapter is at a specific status (Outline / First Draft / Revised / Edited / Final). Leave unset to match all statuses.
- **POV contains** — substring match against the scene's analyzed (or overridden) POV. Use this for "scenes from Alice's POV" if your Context sidebar is detecting POV correctly, or set the analysis override manually for reliability.
- **Tag filter** — match scenes whose analysis tags include a specific tag. Tags live in `SceneData.AnalysisOverrides.Tags`.
- **Plotline filter** — match scenes that include a specific plotline. Pick from the dropdown of plotlines.

Multiple filters combine as **AND**. Leave a filter unset to ignore it.

Save and the list appears in the panel.

## Editing a Smart List

Right-click a list in the panel → **Edit**. The same dialog opens for editing.

## Deleting a Smart List

Right-click → **Delete**.

## Use cases

- **Revision queues.** "First Draft chapters with Alice POV" — your weekly revision list.
- **Plotline focus.** "Mystery plotline scenes" — read the mystery as a continuous thread.
- **Tag-based reading.** "Scenes tagged combat" — beta-read all the action without the connective tissue.
- **Status audit.** "Scenes in Outline status" — find what still needs writing.

## How matching works

Smart lists are computed live whenever the panel is visible or refreshed. There is no manual rebuild step.

The filters are queried in this order: chapter status → POV substring → tag → plotline. A scene that matches every set filter passes.

The list **does not** edit your scenes — it is purely a view.

## Sharing and persistence

Smart Lists are stored in the project's `project.json` and travel with the project. Anyone who opens the project sees the same lists.

## Where to go next

- [Chapters & Scenes](04-chapters-and-scenes.md) — status, POV, plotlines, tags live here.
- [Plot Grid](08-plot-grid.md) — Plotline filters get their values from the grid.
- [Manuscript view](10-manuscript.md) — combine status filter with Smart Lists for a focused read.
