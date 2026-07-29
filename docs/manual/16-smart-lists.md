# Smart Lists

A **Smart List** is a saved query over your scenes. Instead of manually keeping a folder of "scenes to revise" or "scenes with Alice POV that aren't drafted yet", you define the criteria once and re-run the list whenever you need it.

## Where Smart Lists live

Smart Lists have their own tab in the **binder's tab strip** — click **Smart Lists** next to **Chapters** at the top of the left pane.

Each saved list appears as a row. Click the chevron to expand it: the list is evaluated and matching scenes appear underneath as `Chapter - Scene` rows. Click a scene to open it in the Editor. While a list is expanded, a **refresh** button re-runs the query so the results reflect your latest edits.

## Creating a Smart List

Click **+ New smart list** at the top of the panel. The Smart List editor opens with a **name**, a **match** setting, and as many **rules** as you want to add.

**Match** decides how the rules combine:

- **All of these rules** — a scene has to satisfy every one of them.
- **Any of these rules** — one is enough. This is what answers "either of these two POVs", which no number of ANDed filters can express.

Each rule is a field, a comparison, and a value.

**Fields** are:

- **Chapter status**, **Act** — from the scene's chapter.
- **POV** — the POV you set, or the one Novalist detects from the prose.
- **Tag**, **Plotline**, **Scene stage**, **Structure beat**.
- **Scene title**, **Synopsis**, **Notes**.
- **Words**, **Word target**.
- Any [scene field of your own](10-manuscript.md#your-own-scene-and-chapter-fields), listed under its label.

**Comparisons** depend on the field: text fields offer **contains** and **is**, number fields offer **is**, **is more than** and **is less than**, and every field offers **is set** and **is not set** — which is how you find the scenes still missing a synopsis, a target or a POV.

Fields whose values are known — tags, stages, plotlines, chapter statuses, and your own choice fields — offer a drop-down of what this book actually uses rather than asking you to type it exactly.

A list with no rules matches every scene in the book. **Save** and it appears in the panel.

Lists saved by an older version of Novalist keep working: their four filters are read as four **All** rules, and re-saving stores them as rules.

## Editing and deleting

Right-click a list in the panel:

- **Rename** — opens the same editor to change the name or any filter.
- **Delete** — removes the list after confirmation. Deleting a list never touches your scenes; a Smart List is purely a view.

## Use cases

- **Revision queues.** "Draft chapters with Alice POV" — your weekly revision list.
- **Gaps.** "Synopsis is not set" — every scene you have not summarised yet.
- **Either / or.** Match **any**, with one rule per POV — read two threads side by side.
- **Your own fields.** "Tension is more than 7" — if you track tension as a scene field, the list can ask about it.
- **Tag-based reading.** "Scenes tagged combat" — beta-read all the action without the connective tissue.
- **Status audit.** "Chapters in Outline status" — find what still needs writing.
- **Thread check.** "Scenes on the Betrayal plotline" — read one subplot end to end to check its setup and payoff.

## Persistence

Smart Lists are stored in the project file and travel with the project. Anyone who opens the project sees the same lists.

## Who is in the scene

Two rule fields ask about people and places rather than words:

- **cast** — matches the [cast and locations](04-chapters-and-scenes.md) you recorded on the scene. "Every scene Mira is in" is a cast rule, and it finds the scenes where she is present but never named — which a search of the prose cannot.
- **focus** — matches the one entry the scene is *about*, rather than everyone in it.

Both match on entity id rather than name, so renaming a character does not quietly stop the list matching.

## Narrowing the Manuscript view to a list

The Manuscript view's toolbar has a **saved-list** drop-down beside the mode buttons. Pick a list and every mode narrows to its scenes: the continuous prose, the corkboard, the outliner and the board. Pick **The whole book** to clear it.

A saved list that only the binder could apply is a question you can ask in one place and nowhere else, which is most of the reason to save it.

## Where to go next

- [Chapters & Scenes](04-chapters-and-scenes.md) — status lives on chapters; scenes carry POV and tags.
- [Manuscript view](10-manuscript.md) — edit POV and synopses in bulk in the outliner.
- [Plot Grid](08-plot-grid.md) — track scenes across plotlines.
