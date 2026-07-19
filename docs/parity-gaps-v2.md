# UI parity plan v2 — old Avalonia vs new React (honest re-audit)

Triggered 2026-07-19 after the "feature complete" claim was correctly rejected.
The backend RPC facades exist and are tested, but the **React UI is a thin
skeleton** over them. Five parallel audits (codex, inspector, editor/manuscript,
planning views, settings/dialogs/shell) measured the real depth. This is the
build plan to reach genuine parity.

## Current parity by area (audited)

| Area | Parity | Worst gaps |
|---|---|---|
| Smart Lists | ~100% | done |
| Plot Grid | ~90% | inline rename, chapter title in column header |
| Timeline | ~75% | pan/today/jump-to-date nav, source label pill |
| Dashboard | ~60% | cover image, deadline detail, recent activity, pacing summary |
| Editor/Manuscript chrome | ~50% | **scene tabs**, **footnotes panel**, inline actions, @-mentions, auto-replace, editor hotkeys, focus-peek depth, readability |
| Calendar | ~55% | week hour-grid, year per-month scene lists |
| Relationships | ~55% | group/role filters, genealogy T-connectors, auto-fit |
| Research | ~45% | file import, open-external, search, image preview |
| Gallery | ~45% | lightbox, context actions, list view |
| Codex/entities | ~35% | **no entity sidebar**, generic string-dumper detail, move-to-WB, inverse relationships, rich rel editor |
| Settings | ~40% | hotkey rebinding, grammar config, book-width, updates/integrations, goals detail |
| Inspector (context sidebar) | ~15% | **entire scene-analysis engine** (chars/locs/items/lore in scene, mentions, POV/emotion/intensity, footnotes/comments lists) |
| Maps | ~15% | **entire authoring chrome** (tools, layers, properties, 3D) |
| Dialogs | ~half | snapshot compare, map profile, inverse rel, chapter/scene, update, story-date-range |

## DONE this session
- Image loading fixed (book-relative path resolved to project-relative +
  sentinel-host URL). e2e proves real portraits decode.
- Create-project UI + dev "connecting to core" bug.

## Phased plan (priority order)

### Phase 1 — Codex depth (user's #1 & #2 complaints) [DONE — commits f21fada, 15fccbc, a578cf8]
Shipped: persistent entity nav (search, counts, character Role/Group grouping,
location parent/child tree, gender badge, right-click move-to-WB); typed grouped
detail pane (built-in sections + custom-entity typed fields, no leaked internals,
lore category dropdown, parent-location autocomplete); relationship editor with
role/target autocomplete + inverse auto-sync + learned pairs; move-to-WB RPCs.
Deferred (minor): image name edit / swap / clipboard+URL sources.

1. **Persistent entity sidebar** — a left panel (like the binder) listing
   characters/locations/items/lore, always available, not a full-screen view.
   - Character **grouping** (Role / Family Group toggle, section headers).
   - Location **hierarchy tree** (parent/child, expand/collapse, reparent).
   - Cross-entity **search**, per-type **counts**, sort.
2. **Typed detail pane** — replace the generic string `<dl>` dumper with
   labelled, grouped, typed fields per type (Basic/Physical for characters),
   localized labels; stop leaking internal fields (templateId, ageMode,
   entityTypeKey); date-age mode + birthdate picker + computed age; lore
   category dropdown; parent-location autocomplete; **custom-entity typed
   fields** (currently uneditable beyond name — blocker).
3. **Relationships editor** — role autocomplete + target chips + suggestions;
   **inverse-relationship auto-sync** (dialog + learned pairs; backend
   `entities/setRelationshipWithInverse`).
4. **Move to World Bible / back** (backend RPC + context action).
5. Image extras: editable name, swap image, clipboard/URL sources.

### Phase 2 — Inspector / context-analysis engine [DONE — commits d3b1d38, 82dc07e]
Shipped: backend context/analyze (verbatim port of ContextSidebarViewModel:
entities-in-scene matching, mention matrix + last-seen, POV/16-emotion/intensity/
conflict/tags/dialogue%/avg-sentence, overrides merged); Inspector ContextPanel
(entity cards click-to-open, mention matrix, analysis with editable POV) +
AnnotationsPanel (footnotes + comments lists, edit/delete). 201 backend tests,
100%. Deferred (low/medium): story-date+weekday header, position subtitle,
collapsible persisted sections, comment resolved toggle, editable
emotion/intensity/conflict/tags (needs a setAnalysisOverrides RPC).

Port `ContextSidebarViewModel` (~1400 lines) to a backend `context/analyze`
RPC + a sectioned Inspector:
1. Characters/locations/items/lore **present in the scene** (text match w/
   aliases + overrides), cards with role/group/gender/computed-age, click→open.
2. **Cross-chapter mention matrix** + "last seen N chapters ago".
3. **Scene analysis**: POV (auto-detect + edit), emotion (16 profiles),
   intensity (bipolar bar + chapter sparkline), conflict, auto-tags, dialogue %,
   avg sentence length.
4. Standalone **footnotes list** + **comments list** (jump/edit/delete).
5. Story-date + weekday header, position subtitle, collapsible persisted sections.

### Phase 3 — Editor/manuscript chrome
1. **Scene tab strip per pane** (open set, dirty dot, close, move-to-pane).
2. **Footnotes panel** wiring (edit/jump/delete; currently only a stub row).
3. Wire the already-present editor.html hooks the host never calls:
   `setMentionCandidates`, `setAutoReplacements`, `setDialogueCorrectionConfig`,
   `setContextMenuLabels`, `setInlineActions` (+ `inlineActionRequested`).
4. **Hotkey forwarding from the editor iframe** (global shortcuts dead while
   typing) + ctrl-wheel zoom, comment click-to-scroll.
5. **Focus-peek** rich card (relationships, properties, images, sections,
   open-entity, pin).
6. Status bar: readability badge, char counts, reading time; manuscript header
   stats; outliner POV auto-detect.

### Phase 4 — Planning/world views
1. **Maps** authoring chrome (blocker): tool palette (image/pin/label/spline+
   presets/terrain/building/scale), layer panel (tree, lock/visibility/rename/
   reorder/nest), properties panel (opacity/zoom-range/floor/isolate), edit/view
   toggle, zoom-fit/reset, 3D toggle+overlay, focus-peek, rename/delete (RPCs
   exist), wire `placePinAt`/`imageSelected`/`pinClick`/etc.
2. **Calendar**: week hour-grid w/ timed+overlap layout; year per-month scene
   lists; Today; weekday headers; month-cell jump.
3. **Relationships**: group/role filters; genealogy T-connectors + role-group
   boxes; auto-fit.
4. **Research**: file import (+PDF/image, backend RPC), open-external, search,
   image preview.
5. **Dashboard**: cover image, author/deadline detail, recent activity, pacing
   summary.
6. **Gallery**: lightbox + context actions (copy path/markdown, reveal, open —
   needs RPCs). **Timeline**: pan/today/jump nav, source pill.

### Phase 5 — Settings / shell / dialogs / export / git
1. **Hotkey rebinding** system (44 actions, capture, conflict detect, reset).
2. **Grammar-check config** UI (endpoint/creds/picky/mother-tongue/validate).
3. **Book-width** settings block; **Updates & Integrations**; writing-goals
   detail (deadline/author/watch-fs); extension settings pages; settings search.
4. Status bar: goals progress, git indicator, project overview, counts.
5. Dialogs: snapshot compare, map profile, inverse rel, chapter/scene, update
   (release notes/progress), story-date-range; start-menu overlay.
6. Git staging model (stage/unstage, commit-staged, not-installed guidance).
7. Export: presets/SMF toggle, extension formats, select-all/none/count.

## NO DEFERRALS (user directive 2026-07-19)

"Full parity" = every item in this file, including all previously "deferred"
minors. Complete checklist still to land (in-flight or queued):

- Phase 1 leftovers: entity image name-edit, swap-image, clipboard + URL image
  sources.
- Phase 2 leftovers: editable emotion/intensity/conflict/tags (needs a
  scenes/setAnalysisOverride RPC), reset-override buttons, story-date + weekday
  header, position subtitle, collapsible persisted sections, comment resolved
  toggle.
- Phase 3 editor chrome (in-flight): scene tabs, footnotes panel wiring, dead
  editor.html hooks, editor-iframe hotkeys, focus-peek rich card, readability +
  char counts + reading time in status bar.
- Phase 4 (in-flight: calendar/dashboard/relationships/timeline; queued:
  research file-import/open-external/search/preview, gallery lightbox +
  context actions + list view, **maps full authoring chrome** — tools/layers/
  properties/3D/rename-delete, dashboard cover image, timeline all items).
- Phase 5: hotkey rebinding UI, grammar-check config, book-width block,
  updates/integrations, writing-goals detail (deadline/author/watch-fs),
  extension settings pages, settings search; status bar (goals/git/overview/
  counts); ALL missing dialogs (snapshot compare, map profile, chapter/scene,
  update release-notes/progress, story-date-range, start-menu overlay); git
  staging model + not-installed guidance; export presets/SMF/extension formats/
  select-all-none-count; draft delete; diagnostics log buttons.

Each ships backend RPCs (Backend gated at 100%) + React UI + e2e. Detailed
per-item findings with file:line refs are in the five audit reports in this
session's task outputs.
