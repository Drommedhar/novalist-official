# Maps

Novalist has an interactive map view for hand-built world maps, city plans, building layouts, and any other spatial reference you need at writing time. Maps live next to the rest of your project on disk and can hold multiple layered images, pinned references to entities, and zoom-dependent detail.

Open **Maps** from the **World** group in the activity bar (the icon rail on the far left). The view is per-book: each book carries its own list of maps.

## The map view at a glance

The Maps view is a full authoring surface, laid out as:

- A **top toolbar** with the map list and the view/edit controls (below).
- A **tool rail** down the left edge for adding elements — images, pins, labels, roads and rivers, terrain, and buildings.
- The **map stage** in the middle.
- A **layer panel** on the right, with a properties section for the selected layer and element.

## Managing maps

The top toolbar lists every map in the current book:

- **New map** creates a new empty map (one default layer, no pins) — you are asked for a name.
- Click a map's name (tab) in the toolbar to switch to it.
- **Rename current map** and **Delete current map** act on the active map; deleting asks for confirmation.

Changes to the map are saved automatically a moment after you make them — there is no save button.

### View controls

Also in the top toolbar:

- **Toggle Edit / View** — switch between editing and read-only viewing (see below).
- **Zoom to fit** — frame all visible images.
- **Reset view** — recentre the map at 1x zoom.
- **3D** — flip between the flat 2D map and the 3D view (see below).
- **Map border**, **Edit clip mask** (enabled when an image is selected), and **Edit spline** (enabled when a road/river is selected) toggle those editing modes.
- **Delete selected** removes the currently selected element.

## What a map is

A map is a **tree of layers**. Every layer is the same kind of thing — a layer becomes a *group* simply by having other layers nested under it, exactly like Affinity Photo or Photoshop. Any layer (group or not) can hold one or more images positioned in a shared world coordinate space. On top of the whole layer tree sits a collection of pins that mark named locations and optionally link to Codex entities.

Each map has its own JSON file under `Books/<book>/Drafts/<draftId>/Maps/<mapId>.json`. The `BookData.Maps` array carries a lightweight reference (id, name, file name) so the book manifest stays small. Maps saved by older versions (flat group/layer format) are migrated automatically the first time they are opened.

## Editing vs viewing

The map has two modes: **Edit** (default) and **View**. The *Toggle Edit / View* button in the top toolbar swaps between them.

- **Edit** mode enables the left tool rail (add image, pin, label, spline, terrain, building), shows resize handles on the selected image, lets you drag images and pins, and exposes context menus.
- **View** mode is for reading. The tool rail is disabled, images are not draggable, and clicking a pin with a linked entity opens a summary card for it (see [Pins](#pins)).

## Adding images

Click *Add image* in the tool rail and pick an image from the project's [image gallery](19-image-gallery.md). The image lands at the centre of the current viewport, sized to its natural dimensions, and is added to the currently active layer. (To bring a new picture into the project first, add it in the Image Gallery.)

## Navigating

- **Pan** — middle mouse button drag.
- **Zoom** — mouse wheel. The HUD top-left shows the current zoom factor.

Pan and zoom are saved per-map so the next time you open it you return to the same view.

Left-click on empty space simply clears any selection.

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

Right-click → *Edit clip mask* (or the **Edit clip mask** button in the top toolbar, enabled while an image is selected) enters clip-edit mode for the image. You see an orange polygon overlay with one draggable handle per vertex.

- **Drag** any vertex handle to reshape.
- **Double-click** anywhere on the image to add a new vertex (appended to the polygon).
- **Right-click** a vertex to remove it. A minimum of three vertices is enforced.
- **Clear** strips the polygon back to nothing so you can rebuild from scratch.
- **Esc** cancels without saving. **Enter** or **Done** commits and the clip mask is saved with the image.

The clip polygon is stored in the image's natural-pixel coordinates, so it survives resize and rotation.

## Pins

Pins are screen-space markers that stay the same size regardless of zoom. Click *Add pin* in the tool rail, then click on the map where you want the pin. Once placed, its settings appear in a properties panel at the bottom of the layer panel:

- **Label** — text shown above the pin in the map.
- **Link to entity** — search across characters, locations, items, lore, and custom entity types. Linking is optional.
- **Opens map** — point the pin at another map in this project, shown once the project has more than one. A world map marks a city and the city has its own map; that is the one relationship maps actually have, and it is the one Novalist could not express. In view mode, clicking such a pin switches to the map it names.
- **Color** — full colour picker. Default is the theme accent.

Move pins by dragging them. Right-click a pin for a menu: *Edit…* (re-edit label/link/colour), *Move to layer…*, *Delete*.

A pin belongs to a **layer** — the one that was active when it was placed. It follows that layer's visibility, opacity, zoom-range and floor settings, and *Move to layer…* reassigns it.

In view mode, clicking a pin that has a linked entity opens a small **focus-peek card** with that entity's image, name, and detail line — a quick look at what the pin marks without leaving the map. Close it with its `×`.

## Scale, measuring and a grid

The map has always drawn in its own units with a zoom readout beside them, which cannot answer "how many days' ride to the coast" — the one question a map is for. **Scale** in the top-right of the stage fixes that:

- **Ground distance per map unit** — what one unit on this map is worth on the ground.
- **Called** — what that distance is: `km`, `miles`, `leagues`. Free text, because a world does not have to measure in kilometres.
- **Grid spacing, in map units** — draws a grid at that spacing, or nothing at zero. Separate from the scale because a grid is a drawing aid and a scale is a fact about the world.

Once a scale is set, a **scale bar** appears in the bottom-left showing a round ground distance — 50 km, not 47.3 — at the current zoom.

**Measure** takes two clicks and reports the distance between them in your declared unit. Without a scale it still answers, in map units, and says so: a number with no unit behind it is the problem the scale exists to solve.

The grid stops drawing below about four pixels a cell. A grid that fine is a grey wash, which is worse than no grid.

Both are available in view mode as well as edit mode: how far something is is a question about the world, not an edit to it.

## Labels

Labels are free-standing pieces of text placed directly on the map — region names, area notes, "Here be dragons". Unlike pins, a label has no marker and no entity link; it is just text.

Click the **label tool** in the tool rail (the *T* icon), then click anywhere on the map. A label appears right there, already in edit mode — just start typing. The tool is one-shot: it drops one label and returns to the select tool.

While typing:

- **Enter** commits the label.
- **Ctrl+Enter** (or **Shift+Enter**) inserts a line break — labels can be multi-line.
- **Esc** cancels the edit. A label left empty is discarded.

Labels are anchored in world space and their font size is in world units, so a label **scales with the map** — it grows and shrinks as you zoom and stays glued to the area it names. A dark halo keeps the text readable over any background.

- **Move** — drag the label.
- **Edit** — double-click the label to type into it again.
- **Select** — single-click a label to select it; a **Label** properties panel appears at the bottom of the layer panel with **Font size**, **Font** (Default / Serif / Sans-serif / Monospace), **Align** (Left / Center / Right), **Colour**, and **Delete label**.
- **Right-click** a label for a quick menu: *Edit text*, *Move to layer…*, *Delete*.

Like pins, a label belongs to the layer that was active when it was placed and follows that layer's visibility / opacity / zoom-range / floor settings.

## Terrain shapes

Terrain shapes are closed-polygon areas painted on the map — grass, forest, concrete, sand, hills, mountain, water. They are flat-colour fills with a soft, feathered edge, so you can lay down several shapes (say, two different greens of grass) and have their edges blend naturally into one another.

Click the **terrain tool** in the tool rail and pick a type from the menu. Then click on the map to drop polygon points. Press **Enter** to close and commit the shape (needs at least 3 points), **Esc** to cancel. The tool is one-shot — it commits one shape and returns to the select tool. New shapes are added to the active layer.

The type only seeds the fill colour; you can recolour any shape afterwards.

To edit a committed shape, click it (in edit mode, with the terrain tool off). Vertex handles appear:

- Drag a **vertex** to move it.
- **Double-click** the shape body to insert a vertex on the nearest edge.
- **Right-click** a vertex to remove it (minimum three).
- **Esc** or **Done** finishes editing.

Right-click a shape body for *Move to layer…* and *Delete shape*.

When a shape is selected, a **Terrain shape** properties panel appears at the bottom of the layer panel:

- **Type** — switch terrain type (re-seeds the colour).
- **Colour** — full colour picker.
- **Smoothed edges** — on = the outline curves through the points; off = straight polygon edges. Set per shape.
- **Edge blend** — feather width. `0` = a crisp edge; higher values soften the edge so it cross-fades with whatever is next to it.
- **Bring forward / Send back** — move the shape up or down the z-order within its layer, so you control which overlapping shape sits on top.

Like every other map element, a terrain shape belongs to a layer and follows that layer's visibility / opacity / zoom-range / floor settings, has its own per-element zoom range and Isolate toggle in the layer properties panel, and can be moved between layers. Shapes render in **layer-tree order**, so a shape can sit above or below an image depending on where its layer is in the tree.

## Map border

The **map border** is a single clip boundary for the whole map: a freeform polygon you draw, where **everything outside it is hidden** and the polygon itself is stroked as a visible frame. Use it to give a map a clean edge — an island coastline, a torn-parchment outline, a rectangular frame.

Click the **Map border** button in the top toolbar. If the map has no border yet, click on the map to drop polygon points, then **Enter** to commit (needs at least 3 points), **Esc** to cancel. A map has at most one border — committing replaces any previous one.

Click the tool again when a border already exists and it enters **edit mode**: vertex handles appear.

- Drag a **vertex** to move it.
- **Double-click** the outline to insert a vertex on the nearest edge.
- **Right-click** a vertex to remove it (minimum three).
- **Esc** or **Done** finishes editing.

While editing, a **Map border** properties panel appears at the bottom of the layer panel:

- **Outline colour** — colour of the visible frame stroke.
- **Outline width** — stroke width in world units (scales with zoom).
- **Remove border** — deletes the border; the whole map becomes visible again.

## Buildings

Place typed **buildings** on the map — row homes, single family homes, schools, police / fire stations, halls, playgrounds, train stations.

Click the **building tool** in the tool rail and pick a type from the menu. The same menu has a **Building scale** slider (0.25x to 4x) that sets how large newly placed buildings are relative to the map, so a town of houses and a single cathedral can be sized appropriately. A preview of the building follows the cursor:

- Press **R** to re-roll — each building type generates a random fitting footprint, so no two are identical.
- Move near a road and the building **snaps** parallel and offset to the road edge (any layer's roads). Hold **Shift** to place freely.
- **Rotate before placing:** hold the **right mouse button** (or just hold **Shift**) and scroll the **mouse wheel** — the preview rotates in 5° steps, or 15° steps with **Shift** held during the wheel. The rotation adds on top of the road-snap angle when snapped, or replaces it when free-placing.
- **Left-click** drops the building; the tool stays active so you can keep placing. **Esc** exits.

New buildings land on the active layer. Select a building (edit mode, building tool off) → a **Building** properties panel appears at the bottom of the layer panel:

- **Type** — switch building type (re-generates nothing; just recolours/relabels).
- **Roof** — gable / hip / flat, plus a **roof pitch** slider. The roof view renders **shaded slope planes** so the kind reads at a glance: a **gable** shows two eave planes meeting at a full-length ridge, a **hip** shows two eave planes plus hip lines to the corners, and a **flat** roof stays a single plane. The **pitch** slider drives the slope-shading contrast — a shallow roof looks almost flat, a steep roof shows strong light/dark faces. Pitch also drives how much area upper floors lose to the slope once floor plans are added — a gable squeezes upper floors only on the two eave sides, a hip insets on every side, a flat roof not at all.
- **Floors** — number of floors.
- **Plan zoom** — the zoom level at/above which the building's floor plan will be shown instead of the roof.
- **Bring forward / Send back** — z-order within the layer.
- **Delete building**.

A selected building shows an **orange round rotation handle** above its top corner — drag it to rotate the whole building (footprint, roof, and every floor's walls, doors, windows, stairs, labels and pins rotate together). Hold `Shift` to snap to 15-degree steps.

Right-click a building for *Move to layer…* and *Delete building*. Like every other map element, a building is layer-bound (visibility / opacity / zoom-range / floor cascade), has its own per-element zoom range + Isolate in the layer panel, and can be moved between layers.

### Floor plans

A building can carry a multi-floor interior plan. Set **Floors** in the building properties panel, then **Plan zoom** — the zoom level at/above which the building stops showing its roof and shows the active floor's plan instead.

Select a building and click **Edit floor plan** to draw the interior in place:

- A small toolbar appears with **Wall**, **Door**, **Window** and **Stairs** tools, plus **Done**. While editing, every other layer is hidden so nothing gets in the way.
  - **Wall** — click points on the floor; consecutive points chain into walls. **Enter** or **Esc** ends the current chain.
  - **Door** / **Window** — click on an interior wall *or* on the outer outline to anchor an opening there (a door draws a swing arc, a window a thin pane).
  - **Stairs** — click the start point then the end point; the run between them sets the staircase length and direction. While editing a floor, the stairs on the floors directly above and below are shown as faint dashed ghosts so you can line staircases up vertically.
  - **Label** / **Pin** — drop a text label or a pin onto the current floor. These are *floor-scoped* — they show only while that floor is the active floor. Labels and pins are edited exactly like map-wide ones (inline text editing, drag, the label/pin property panels).
  - **Right-click** a door for *Flip swing side* / *Delete*; right-click any wall, window, stair, floor label or floor pin to remove it.
- Upper floors automatically lose usable area to the **roof slope**: a gable roof squeezes the outline only on the eave sides, a hip roof insets it on every side, a flat roof not at all — scaled by the roof pitch. The full footprint is shown ghosted behind an inset upper floor for reference.

Every building showing its floor plan gets a small **floor selector** at its top corner — up / down arrows with the current floor number. It works in **view mode** too, so a reader can flip through a building's floors.

## Roads & rivers (splines)

The spline tool in the tool rail draws roads and rivers as smoothed lines. Click it to open a type menu:

- **Road** — Motorway / Highway, Primary / Trunk, Secondary, Residential / Local, Service / Alley, Pedestrian / Plaza, Footpath / Trail (a dotted/dashed path line), Train track (ballast strip with cross-ties and two rails).
- **River** — Brook / Creek, Stream, River, Canal, Estuary.

Pick a type, then click on the map to lay down knot points. The line curves smoothly through the knots (canals stay straight). Press **Enter** to finish the spline, **Esc** to cancel it. New splines are added to the active layer.

Each type has a built-in **profile**: a darker *casing* drawn underneath, a coloured fill on top, and — for roads — lane markings (centre line, edge lines). Because every casing is drawn first in one pass and every fill on top in a second pass, overlapping roads and rivers visually merge into one network with no seams.

To edit an existing spline, click it (in edit mode, with the spline tool off). Knot handles appear:

- Drag a **knot** to move it. Dragging an **end** knot snaps it onto the nearest knot of any other spline when it gets close, so T-junctions and crossings line up exactly — hold `Shift` while dragging to place it freely.
- Drag the small **outer dot** above a knot to set the road/river width at that point — the width tapers smoothly between knots, so you can widen a road into an avenue or bulge a river into a pool.
- Drag the **orange round handle** on the focused knot to rotate it — this overrides the spline's direction through that knot (works on endpoints too). Hold `Shift` to snap to 15°. Right-click the knot → *Clear direction override* to go back to automatic.
- **Add knots** — double-click anywhere on the spline body to insert a knot there, or right-click a knot and pick *Add knot before* / *Add knot after*. *Add … after* on the last knot (or *… before* on the first) extends the spline outward.
- **Right-click** a knot to remove it (minimum two knots).
- **Esc** or **Done** finishes editing.

Each knot has a **Corner sharpness** slider in the focused-knot card (0 = a fully smooth curve through the knot, 1 = a hard corner — straight in, straight out). Use it for sharp 90° street corners or angular coastlines.

Right-click the spline body for *Move to layer…* and *Delete spline*. A spline belongs to a layer and follows that layer's visibility / opacity / zoom-range / floor settings.

Open splines get **rounded ends** — a road or river stub terminates in a round cap rather than a hard square edge, so overlapping stubs read as one network.

### Closed loops

The spline properties panel has a **Closed loop (roundabout / lake)** checkbox. Tick it and the spline wraps end-to-end into a continuous loop. Closed splines have no end caps (there are no ends).

- A closed **road** stays a ring — use it for roundabouts and ring roads.
- A closed **river** fills the enclosed area with its water colour — use it for lakes and ponds. The river type's casing reads as the shoreline around the water body.

Each knot can also carry a **type override** — right-click a knot and pick a different road/river type from the menu (or *Clear type override*). The spline then cross-fades its casing and fill colour from one type to the next along that stretch, so a stream can widen into a river or a residential street can ramp up to a primary road. The selected-knot card in the properties panel also has a **blend** slider controlling how softly that knot's type change cross-fades (0 = hard cut, 1 = full blend). Per-knot width, type override, and blend are saved with the spline.

The spline properties panel has a **Centerline** picker to override a road's centre line: preset default, none, single solid, dashed, double solid, or solid + dashed (asymmetric passing). Edge lines always come from the road type. The focused-knot card has its own Centerline picker that overrides the spline style for just the segment leaving that knot — so a road can switch from a dashed (passing) to a double-solid (no-passing) centre line at a specific point. Centerline style cross-fades between knots using the knot blend slider, same as the type blend.

**Part colours** — the properties panel has three colour pickers (Casing, Fill, Markings) to recolour any part of the road/river. *Reset colours to preset* clears the overrides.

**Bring forward / Send back** — the properties panel also has buttons to move the spline up or down the z-order within its layer, so you control which overlapping road or river draws on top.

### Custom profiles

Beyond the built-in types you can author your own **cross-section profiles**. In either spline preset menu pick *Manage profiles…* to open the profile editor. A profile has:

- **Name**, **Kind** (road or river), **Default width**.
- **Casing** — colour and how far it extends beyond the road edge.
- **Fill bands** — stacked colour bands, each with a `from`/`to` half-width fraction (`0` = centreline, `±1` = road edge). Stack bands to build a real cross-section: sidewalk | curb | lanes | median | lanes | curb | sidewalk.
- **Lane markings** — each with an offset, colour, width, and optional dash pattern.

Custom profiles are saved with the map and appear under a *Custom* submenu in both preset pickers (the spline tool and the spline properties panel). Per-knot type cross-fading works between custom profiles too.

## Layer panel

The **layer panel** sits on the right of the map. It is an Affinity-style layer tree: each row is a single layer, indented to show how deeply it is nested.

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
- **Splines / Pins / Labels on this layer** — every road/river, pin and text label belonging to the selected layer is listed under its own heading, each with the **same controls** as an image row: an independent `From`/`To` zoom range and an **Isolate** toggle.

The **Isolate** toggle is a view-only convenience: while on, the map shows *only* that one element (image, spline, pin or label) so you can clearly see what you are editing. It is never saved — turning it off (or isolating another element) restores the normal view.

### Floor mode (connected layer sets)

In the Properties section of a *group* layer, tick **Floor mode (one layer at a time)**. Enable it when the group represents a multi-storey building or any place where only one child should ever be visible at once.

With floor mode on, the Properties section exposes an **Active floor** dropdown listing the group's child layers. Whichever child you pick is the only one rendered; the other children stay hidden until you select them. Internally this sets the group's `isConnectedSet` flag — the map data still contains every floor, but the renderer only shows the chosen one.

## 3D view

The **3D** toggle in the map toolbar flips the map between the flat 2D view and a GPU-rendered 3D view of the same map. It renders whatever the map already holds — no extra setup.

- **Buildings** stand up: walls extruded to `floor count x floor height`, with a real sloped roof on top (gable, hip, or flat) shaded by the roof pitch. Fly inside to see the interior — per-floor slabs, interior walls, doors and windows, and stairs (with stairwell holes cut through the ceilings). Floor labels and pins show as billboards.
- **Terrain** shapes lie flat on the ground in their colours; **roads** render as flat ribbons following their curves; **base map images** become textured ground under everything.
- **Grass** terrain shapes get a procedural sprinkling of instanced blades on top of the flat fill. Blades pick their colour from the shape's fill, vary in yaw, scale and tint, and sway in a continuous wind shader — so a grass region reads as a living meadow without any per-blade authoring.
- **Rivers and closed water bodies** use a live water shader — flowing ripples that reflect and refract the surrounding scene, tinted by the spline's fill colour. The bed is faked in the shader: the water darkens and the refracted scene shifts with a depth-based parallax toward the interior, so it reads as a recessed basin (deepest along a river's centreline, toward the middle of a lake) with no carved terrain.
- **Pins** show as upright markers with their label; **labels** as camera-facing text.
- Hidden layers and Isolate are respected; per-layer opacity and zoom ranges are ignored in 3D.

The 3D view needs **WebGPU**, which the app's bundled browser engine provides on current systems. If WebGPU is unavailable on your machine, the 3D view shows a short message instead of rendering — keep working in the 2D map.

When you switch to 3D the map first shows a **loading overlay** (progress bar + status text) while the renderer warms up, tree models and canopy textures load, and the grass/tree instance buffers are filled. The scene appears only when it is ready, so you never see a half-built frame.

**Camera** — click the view to capture the mouse, then fly freely: `W` / `A` / `S` / `D` to move, `Q` / `E` down / up, mouse to look, `Shift` to move faster, `Esc` to release the mouse. There is no collision — fly straight through walls to inspect interiors.

The 3D view is **read-only** in this version: it is for looking, not editing. Toggle back to 2D to make changes, then flip to 3D again to see them.

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

## Exporting a map as an image

**Export image** in the map toolbar saves the map as a PNG, exactly as it looks on screen — layers, pins, labels, roads, terrain and the clip border all included. The 3D view exports the same way once you are in it.

The drop-down beside the button sets the scale:

- **1x** — what is on screen.
- **2x** — the default. Comfortable for a full-page map inside an EPUB.
- **4x** — for print, endpapers, or a poster.

The image captures the map area as it is currently framed, so pan and zoom to the view you want first. To export the whole map rather than the visible part, zoom out until it all fits.

Novalist writes a plain PNG with no metadata attached, so it can be dropped into any layout tool, sent to a cover designer, or added to the project's own [image gallery](19-image-gallery.md) for use as a chapter illustration.

## Where to go next

- [Image Gallery](19-image-gallery.md) — see every image across the project, including those used by maps.
- [Codex (Characters, Locations, Items, Lore)](06-codex.md) — pin targets come from your Codex.
- [Git integration](18-git.md) — map JSON files are versioned like any other project file when you commit through Git.
