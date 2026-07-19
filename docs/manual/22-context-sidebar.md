# Inspector

The **Inspector** is the right-hand pane of the Novalist window. For the scene open in the editor it shows scene context and analysis, the synopsis, notes, footnotes and comments, and the scene's snapshot history. (This pane was called the context sidebar in earlier versions.)

## Toggling the Inspector

- The Inspector toggle at the far right of the toolbar, or
- `Ctrl+Shift+B` (`Cmd+Shift+B` on macOS).

**Focus mode** (`Alt+F`) hides the Inspector together with the binder, leaving only the editor.

When no scene is open, the Inspector shows a placeholder; open a scene from the binder and its details appear.

## Scene header

The header shows the open scene's title, a **Chapter · Scene N of M** subtitle placing it within its chapter, the scene's **story date** with the weekday it falls on (when the scene has a date), and the current **word count**.

## Scene context and analysis

Directly below the header, Novalist analyses the open scene and shows what it finds. Every part is a collapsible section, and your collapse choices are remembered between scenes.

### Entities in the scene

The **characters**, **locations**, **items**, and **lore** detected in the scene, each shown as a card with its thumbnail and name. Click a card to open that entity in the [Codex](06-codex.md).

### Mention matrix

A cross-chapter grid: one row per tracked character showing, chapter by chapter, where they appear. The current chapter is marked, and a character who has been off-page for a while gets a **"last seen N chapters ago"** note — an easy way to spot a cast member who has quietly dropped out of the story.

### Scene analysis

Each value is auto-computed but fully editable, and every field carries a **reset-to-auto** button that drops your override and restores the detected value:

- **POV** — the point-of-view character, chosen from a dropdown.
- **Emotion** — the scene's dominant emotion, chosen from a dropdown.
- **Intensity** — a bipolar bar from -10 to +10 (the fill runs one way for negative values, the other for positive) with a number box to set it exactly.
- **Conflict** — a short free-text description.
- **Tags** — a comma-separated list; the current tags also appear as chips below the field.

Beneath the analysis, a stats line reports the scene's **word count**, **dialogue percentage**, and **average sentence length**.

## Synopsis

A short summary of the scene — two or three sentences is the usual scale. Edit it directly in the text box; it saves when you click away. The synopsis appears:

- On scene cards in the Manuscript **corkboard**.
- In the Manuscript **outliner** table.

## Scene notes

A longer freeform note field, saved the same way. Use it for:

- Research links specific to the scene.
- Outline / draft notes during writing.
- Reminders ("fix the timing of Alice arriving" / "double-check the magic-system rule applies here").

Notes are never exported. They're for you.

## Footnotes and comments

When the open scene has footnotes or inline comments, the Inspector lists them:

- **Footnotes** — each with its number and its text, editable inline; remove one with its close button.
- **Comments** — each shows the anchored text it attaches to, an editable comment body, a close button to delete it, and a **resolved** toggle to mark it done (resolved comments are dimmed).

You create footnotes and comments inside the editor (see [Editor](05-editor.md)); this panel is where you read, edit, resolve, and clear them.

## Scene snapshots

The snapshot list for the open scene. Click **Take snapshot** to save the scene's current state, with an optional label. Each entry offers:

- **Restore** — bring that older version back.
- **Delete** — discard the snapshot.
- **Compare** — pick one snapshot, then a second, to see the two side by side.

See [Snapshots](17-snapshots.md) for the full picture, including the auto-snapshots taken before Replace All and before restores.

## Where to go next

- [Editor](05-editor.md) — the writing surface the Inspector describes.
- [Snapshots](17-snapshots.md) — per-scene version history.
- [Codex](06-codex.md) — where the entity cards open.
- [Manuscript view](10-manuscript.md) — where synopses show up on cards and in the outliner.
