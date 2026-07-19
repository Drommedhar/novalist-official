# UI parity gaps — honest re-audit (old Avalonia vs new React)

Triggered 2026-07-19 after the user correctly rejected the "feature complete"
claim. The backend RPC facades exist and are tested, but the React UI is a
shallow reimplementation of the Avalonia UI. This file tracks the real gaps
and the plan to close them.

## CONFIRMED BUG — entity/gallery/map images do not load

Root cause: image paths are stored **book-root-relative**
(`Images/Characters/x.png`, real file at `<projectRoot>/<bookFolder>/Images/...`),
and world-bible images are **WorldBible-root-relative**. The
`novalist-project://` protocol (app/src/main/protocols.ts) resolves against the
**project root**, so the book folder is missing from the path → 404 → no image.

- Stored path base: `ActiveBookRoot = ProjectRoot/<bookFolder>` (EntityService
  ImportImageAsync stores `Combine(Book.ImageFolder, file)` = book-relative).
  WB entities: `WorldBibleRoot`.
- Protocol base: `projectRoot` (state.projectPath = Projects.ProjectRoot).

Fix (planned): backend returns a **project-root-relative** display path
(resolve `Combine(isWorldBible ? WorldBibleRoot : ActiveBookRoot, stored)` then
`GetRelativePath(ProjectRoot, …)`), kept SEPARATE from the stored path so
add/remove still match on the stored value. Touches EntitiesRpc (summaries +
`get` images + addImage return), LibraryRpc (gallery/list), MapsRpc (map image
base), and the renderer img src builders (CodexView, EntityImages, GalleryView,
EditorFrame hover card, MapsView base url).

## Context sidebar / Inspector — "not even a quarter" (accurate)

OLD: two right-side surfaces + in-editor annotation. `ContextSidebarViewModel`
(1918 lines) drove a tabbed analysis panel; `SceneNotesViewModel` a bottom
synopsis/notes/comments dock; `FootnotesPanelViewModel` a footnote list.

NEW `Inspector.tsx`: synopsis + notes + word count + snapshots. That's it.

Missing (severity):
- CRITICAL: characters-in-scene section — regex match on scene text (name +
  aliases, overrides applied), cards with role/group/gender/computed-age,
  click-to-open.
- HIGH: locations / items / lore in-scene sections (parent, type, category,
  description, click-to-open); cross-chapter mention matrix + "last seen N
  chapters ago"; Scene Analysis POV (auto-detect + editable), Emotion (16
  keyword profiles), Intensity (−10..+10 bipolar bar).
- MEDIUM: story date + weekday header; position subtitle; conflict snippet;
  auto scene tags; dialogue %; avg sentence length; intensity sparkline across
  chapter; standalone footnotes list.
- LOW: collapsible persisted sections; comments list in sidebar; extension
  context tabs; legend/empty states.

Backend support: NONE of the analysis engine exists in the new backend
(~1400 lines to port from ContextSidebarViewModel). `SceneDto` lacks date,
POV, analysis fields. Must build `context/analyze` (or similar) RPC.

## (pending) Codex / entities — user: "no character/location sidebar"
Audit running.

## (pending) Editor + manuscript chrome
Audit running.

## (pending) Planning views depth (dashboard/timeline/calendar/relationships/plotgrid/research/maps/gallery)
Audit running.

## (pending) Settings / dialogs / shell chrome / smart lists / export / git
Audit running.
