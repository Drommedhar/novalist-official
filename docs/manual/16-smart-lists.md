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
- **Scene goal**, **Scene outcome** — the two halves of the scene diagnostic. "Scene outcome is not set" is the list worth saving: every scene nothing has come of yet.
- **Out of the book** — scenes you have [taken out of the manuscript](04-chapters-and-scenes.md#taking-a-scene-out-of-the-book) but kept in the plan.
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

## Collections

A tab beside **Chapters** and **Smart Lists** in the binder.

A Smart List is a query: it recomputes every time you open it, and a scene is in it because it matches. A **collection** is a set you gathered by hand — the eight scenes to fix before Tuesday, the run you are reading to your writing group, the ones a beta reader stumbled on. Nothing they have in common is expressible as a filter, which is exactly why they had to be picked one at a time.

**Making one.** Select the scenes in the binder (Ctrl-click or Shift-click), type a name in the Collections tab and press the plus. Whatever is selected goes straight in — the panel tells you how many. A collection can also start empty and be filled later.

**Filling one.** Select scenes and press the plus on a collection's row. A scene already in it is skipped rather than doubled.

**The order is yours.** Scenes stay in the order you added them, not reading order. A revision run is often deliberately out of sequence, and re-sorting it would throw away the only thing you said about the set.

**Removing.** The × on a scene takes it out of the collection. The bin on a collection deletes the set. Neither touches the scenes themselves — a scene can be in five collections and none of them changes the manuscript. A scene you later delete drops out of every collection that held it, because a row that opens nothing is worse than a shorter list.

Collections are stored with the book, so they travel with the project and survive a restart.

## Bookmarks

A tab beside **Chapters**, **Smart Lists** and **Collections** in the binder.

A saved list answers "which scenes match this query". A bookmark answers a different question — the paragraph where she finds out, the entry you keep re-reading, the day the siege starts — and until now had nowhere to be recorded, so people kept them in a scene called Notes.

Right-click a scene in the binder → **Bookmark this scene**. Bookmarks can also point at a chapter, a Codex entry, a research item, a story date or a place on a map; clicking one goes there.

Bookmarks can carry a **group** name, and the panel shows named sets first with the loose ones last — a named set is a deliberate act, and the loose ones are the pile it was made from.

A bookmark on a passage inside a scene stores **the text itself**, not a position. Prose above a mark is edited constantly, and a stored position would drift silently into the middle of an unrelated sentence; text that no longer appears simply opens the scene.

**Seeing what a bookmark points at.** The chevron beside a bookmark opens a few lines of whatever it marks — the passage in the scene, the opening of a bookmarked chapter, or what a Codex entry is. A bookmark that only navigates makes you go and look to remember why you kept it, and for a list of thirty that is thirty trips.

The preview reads the scene only when you open it, so a long list costs nothing until you ask. If the sentence a bookmark named has since been rewritten, you get the opening of the scene instead of nothing — the scene is still worth recognising, and an empty preview reads as a broken bookmark.

## Where to go next

- [Chapters & Scenes](04-chapters-and-scenes.md) — status lives on chapters; scenes carry POV and tags.
- [Manuscript view](10-manuscript.md) — edit POV and synopses in bulk in the outliner.
- [Plot Grid](08-plot-grid.md) — track scenes across plotlines.
