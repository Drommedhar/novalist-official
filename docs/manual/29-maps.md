# Maps

Novalist has an interactive map view for hand-built world maps, city plans, building layouts, and any other spatial reference you need at writing time. Maps live next to the rest of your project on disk and can hold multiple layered images, pinned references to entities, and zoom-dependent detail.

Open the map view from the activity bar (the map icon). The view is per-book: each book carries its own list of maps.

## What a map is

A map is a **tree of layers**. Every layer is the same kind of thing — a layer becomes a *group* simply by having other layers nested under it, exactly like Affinity Photo or Photoshop. Any layer (group or not) can hold one or more images positioned in a shared world coordinate space. On top of the whole layer tree sits a collection of pins that mark named locations and optionally link to Codex entities.

Each map has its own JSON file under `Books/<book>/Drafts/<draftId>/Maps/<mapId>.json`. The `BookData.Maps` array carries a lightweight reference (id, name, file name) so the book manifest stays small. Maps saved by older versions (flat group/layer format) are migrated automatically the first time they are opened.

## Editing vs viewing

The map has two modes: **Edit** (default) and **View**. The toolbar's *Toggle Edit / View* button swaps between them.

- **Edit** mode shows resize handles on the selected image, allows dragging images and pins, exposes the layer panel, and shows context menus.
- **View** mode is for reading. Images are not draggable; clicking a pin opens its linked entity in a focus peek.

## Adding images

Click *Add image* in the toolbar. The same image-source dialog used in entity images appears, with four options:

- **From library** — pick from images already in the project's `Images/` folder.
- **Import file** — copy an image from disk into the project.
- **Paste from clipboard** — useful for screenshot-driven workflows.
- **From URL** — download into the project.

The image lands at the centre of the current viewport, sized to its natural dimensions, and is added to the currently active layer.

## Navigating

- **Pan** — middle mouse button drag.
- **Zoom** — mouse wheel. The HUD top-left shows the current zoom factor.

Pan and zoom are saved per-map so the next time you open it you return to the same view.

The cursor only switches to a grab indicator while panning. Left-click on empty space simply clears any selection.

## Working with images

Click an image to select it. The selected image shows a dashed outline plus eight resize handles (four corners, four edges) and a circular rotation handle above the top edge.

- **Move** — drag the image body.
- **Resize** — drag any corner or edge handle. Hold `Shift` to preserve aspect ratio.
- **Rotate** — drag the round handle above the image. Hold `Shift` to snap to 15-degree increments.
- **Delete** — *Delete selected* in the toolbar, or right-click → *Delete*.
- **Right-click** any image to get a small menu: *Move to layer…*, *Edit clip mask*, *Delete*.

### Move to a different layer

Right-click → *Move to layer…* opens a dialog listing all layers in the map. Pick a target and the image is moved (preserving its position, size, rotation, and clip mask). New images by default land on the active layer — click any layer row in the layer panel to make it active.

### Clip mask (polygon clip)

Right-click → *Edit clip mask* enters clip-edit mode for the image. You see an orange polygon overlay with one draggable handle per vertex.

- **Drag** any vertex handle to reshape.
- **Double-click** anywhere on the image to add a new vertex (appended to the polygon).
- **Right-click** a vertex to remove it. A minimum of three vertices is enforced.
- **Clear** strips the polygon back to nothing so you can rebuild from scratch.
- **Esc** cancels without saving. **Enter** or **Done** commits and the clip mask is saved with the image.

The clip polygon is stored in the image's natural-pixel coordinates, so it survives resize and rotation.

## Pins

Pins are screen-space markers that stay the same size regardless of zoom. Click *Add pin* in the toolbar to drop one at the centre of the viewport. A pin dialog opens with three fields:

- **Label** — text shown above the pin in the map.
- **Link to entity** — type-ahead search across characters, locations, items, lore, and custom entity types. Linking is optional.
- **Color** — full colour picker. Default is the theme accent.

Move pins by dragging them. Right-click a pin to re-edit its label/link/colour or delete it.

In view mode, clicking a pin that has a linked entity opens the entity's focus peek.

## Layer panel

Toggle the layer panel from the *Layers* button in the toolbar (it is open by default). The panel is an Affinity-style layer tree: each row is a single layer, indented to show how deeply it is nested.

Each row carries, left to right:

- **Chevron** — only shown on layers that have children (i.e. groups). Click to expand or collapse the group. The expand state is saved with the map.
- **Lock toggle** — a padlock icon (closed = locked). Locked layers cannot have their images dragged or selected with the left mouse button. Use this for static backgrounds.
- **Name** — the layer label. **Double-click** the name to rename it inline; press Enter to commit or Esc to cancel.
- **+** — add a new child layer nested under this one (turns the layer into a group if it was not already).
- **Eye toggle** — an eye icon (open = visible, struck = hidden). Toggles visibility of the layer and everything nested under it.
- **×** — delete the layer and everything nested under it (asks for confirmation).

Click anywhere on a row to make it the **selected** layer — it highlights with the accent colour. The selected layer is also the *active* layer: new images you add land here, and the Properties section below the panel edits it.

Use *+ Layer* at the top of the panel to add a new top-level layer.

### Reordering and nesting (drag and drop)

Drag any layer row to reorder it or move it between groups:

- Drop on the **top third** of a row → placed *before* that row, at the same level.
- Drop on the **bottom third** → placed *after* that row, at the same level.
- Drop on the **middle** → nested *inside* that row as a child.

You cannot drop a layer into its own subtree (that would create a cycle); such drops are ignored.

### Properties section

When a layer is selected, a **Properties** panel appears at the bottom of the layer panel. It edits the selected layer:

- **Opacity** — 0 to 1 slider, snapped in 5% steps. Opacity multiplies down the tree, so dimming a group dims everything inside it.
- **Visible zoom range** — `From` and `To` numeric inputs. The layer is hidden when the current map zoom is below `From` or above `To`. `0` on either side means no limit. Example: `From=5` keeps the layer hidden until you zoom past 5×.
- **Floor mode** — shown only for groups (layers with children). See below.
- **Images on this layer** — every image directly on the selected layer is listed. Each image has its own `From`/`To` zoom range (independent of the layer's) and an **Isolate** toggle.

The **Isolate** toggle is a view-only convenience: while on, the map shows *only* that one image so you can clearly see what you are editing. It is never saved — turning it off (or selecting another image to isolate) restores the normal view.

### Floor mode (connected layer sets)

In the Properties section of a *group* layer, tick **Floor mode (one layer at a time)**. Enable it when the group represents a multi-storey building or any place where only one child should ever be visible at once.

With floor mode on, the Properties section exposes an **Active floor** dropdown listing the group's child layers. Whichever child you pick is the only one rendered; the other children stay hidden until you select them. Internally this sets the group's `isConnectedSet` flag — the map data still contains every floor, but the renderer only shows the chosen one.

## Managing maps

Maps are managed from the **File** menu in the toolbar at the top of the map view:

- **New map…** prompts for a name and creates a new empty map (one default layer, no pins).
- **Open map** is a submenu listing every map in the current book — pick one to switch to it. Switching saves the active map's view (pan/zoom) before loading the new one.
- **Rename current map…** / **Delete current map…** act on the map currently open.

## Where the files live

For a book at `Books/MyBook/`:

```
Books/MyBook/Drafts/<draftId>/Maps/
    <mapId-1>.json
    <mapId-2>.json
Books/MyBook/Images/
    <any-image-referenced-by-a-map>.png
```

Map images live in the book's regular `Images/` folder — the same place that entity images use. The `path` field on each map image is book-root-relative (for example `Images/town-overview.png`), so backups and Git work the same as any other image.

## Where to go next

- [Image Gallery](19-image-gallery.md) — see every image across the project, including those used by maps.
- [Codex (Characters, Locations, Items, Lore)](06-codex.md) — pin targets come from your Codex.
- [Snapshots](17-snapshots.md) — map JSON files are versioned like any other project file when you commit through Git.
