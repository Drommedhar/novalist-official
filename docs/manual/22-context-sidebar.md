# Inspector

The **Inspector** is the right-hand context sidebar of the Novalist window. For the scene open in the editor it has two tabs:

- **Context** — scene context and analysis (entities, mention matrix, editable POV/emotion/intensity/conflict/tags).
- **Footnotes** — the footnotes and comments anchored in the open scene.

The scene's **synopsis** and freeform **notes** live in the [scene-notes dock](02-interface-overview.md) beneath the editor (`Ctrl+Shift+N`), and the scene's **snapshot history** is opened from the toolbar Snapshots button. (This pane was called the context sidebar in earlier versions.)

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

The **characters**, **locations**, **items**, and **lore** detected in the scene, each shown as a card with its thumbnail and name. **Hover** a card to raise the same rich **focus-peek** card the editor shows when you hover an entity's name in the prose — image, attribute pills, relationships (which you can click to peek through to related entities), appearance, custom properties, description, map pins and sections, with pin/open/close buttons in its header. The peek resolves any [chapter or scene overrides](06-codex.md) for the open scene, so it matches who the entity is at this point in the story. **Click** a card to open that entity's article in the [Wiki](30-wiki.md) (from there, **Edit in Codex** reaches the editable record). Character cards also carry small **Gender** and **Age** pills. A character that has a [chapter or scene override](06-codex.md) for the open scene is shown with its overridden name, role, gender and age, so the card matches who the character is at this point in the story. When age is stored as a birth date, the pill shows the character's age **at the open scene**, computed from the birth date against the scene's story date (falling back to the chapter's date, then today).

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

## Footnotes and comments (Footnotes tab)

The **Footnotes** tab lists the footnotes and inline comments in the open scene:

- **Footnotes** — each with its number and its text, editable inline; remove one with its close button.
- **Comments** — each shows the anchored text it attaches to, an editable comment body, a close button to delete it, and a **resolved** toggle to mark it done (resolved comments are dimmed).

You create footnotes and comments inside the editor (see [Editor](05-editor.md)); this tab is where you read, edit, resolve, and clear them.

## Synopsis and notes (bottom dock)

The scene's **synopsis** (a short summary) and freeform **notes** are no longer in the Inspector — they live in the **scene-notes dock** beneath the editor, toggled from the toolbar or `Ctrl+Shift+N`. Both save when you click away. The synopsis also appears on Manuscript corkboard cards and in the outliner table; notes are never exported.

## Scene snapshots (toolbar dialog)

Per-scene snapshots are taken and managed from the toolbar **Snapshots** button, which opens a dialog with **Take snapshot**, **Restore**, **Delete**, and **Compare**. See [Snapshots](17-snapshots.md) for the full picture, including the auto-snapshots taken before Replace All and before restores.

## Where to go next

- [Editor](05-editor.md) — the writing surface the Inspector describes.
- [Snapshots](17-snapshots.md) — per-scene version history.
- [Codex](06-codex.md) — where the entity cards open.
- [Manuscript view](10-manuscript.md) — where synopses show up on cards and in the outliner.
