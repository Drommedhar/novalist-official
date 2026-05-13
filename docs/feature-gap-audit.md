# Novalist competitive feature-gap audit

Audit date: 2026-05-13. Scope: compare Novalist's shipping feature set (per `README.md` and `docs/manual/`) against the major novel-writing and worldbuilding applications. The goal is to surface user-visible features that competitors expose but Novalist does not, ranked by how often they show up across the market and how impactful they appear for a working novelist.

Where the gap can be plugged by the existing SDK (custom entity types, ribbon items, editor hooks, export formats, AI hooks) it is flagged with **SDK-pluggable**. Those are not "missing forever" — they are missing from the core box.

## Apps surveyed

| App | Type | Notes |
| --- | --- | --- |
| Scrivener | Desktop, paid | Industry default. Binder, Scrivenings, Compile, Collections, Snapshots. |
| yWriter 7 | Desktop, free | Scene-structure focused: goal/conflict/outcome per scene, storyboard. |
| Manuskript | Desktop, FOSS | Snowflake-method novel assistant, frequency analyzer, outliner. |
| Plottr | Desktop + cloud, paid | Visual timeline, 30+ plot templates, series-level timeline. |
| Dabble | Web/desktop, subscription | Plot Grid, sticky-note revisions, co-authoring, beta-reader workflow. |
| LivingWriter | Web, subscription | 14 outline templates, Story Elements autofill, AI outline. |
| Novelcrafter | Web, subscription | Codex with mention heatmap / progressions, AI prose, chat-with-scene. |
| Sudowrite | Web, subscription | AI prose: Write/Rewrite/Expand/Describe/Brainstorm/Canvas/Visualize. |
| Atticus | Web, paid | Print/EPUB formatter with themes, ornamental breaks, device preview. |
| Campfire Write | Web, modular | 17 modules including Magic, Species, Cultures, Languages, Religions. |
| World Anvil | Web, subscription | Interactive maps, family trees, chronicles, statblocks, embeds. |
| Obsidian + StoryLine plugin | Plugin, FOSS | Beat-sheet templates, setup/payoff tracking, plot-hole detection, story graph. |
| Obsidian + Longform plugin | Plugin, FOSS | Reorderable nested scenes, multiple drafts per project, workflow-based compile pipeline, session goals. |
| Aeon Timeline 3 | Desktop, paid | Timeline specialist. Arcs, entities (people/places/things), observers and participants per event, age tracker, custom calendars, two-way Scrivener / Ulysses sync. |
| Vellum | macOS, paid | Print + EPUB formatter. 24 trim sizes, box-set bundles, validated EPUB 2/3, PDF/X-1a, ACE-accessible output, real-time device preview while editing, automatic widow / spread handling. |
| Bibisco | Desktop, FOSS + paid | Character-interview questionnaire, explicit premise / fabula / narrative-strands fields, mind maps, in-depth analysis (length, character distribution, POV balance, location frequency). |
| One Stop for Writers | Web, subscription | 23-thesaurus reference library (emotions, traits, tropes, relationships, setting, weather, occupations). Character builder with wound / fear / lie / secrets. Formal and Informal Scene Maps with inner + outer motivation / conflict / stakes. Storyteller's Roadmap step-by-step. |
| NovelPad | Web, subscription | Insights Board cross-reference matrix. Plot cards that group scene cards. Character tracker driven by per-character nickname / alias lists. |
| Reedsy Studio | Web, free + paid | Real-time co-authoring, online beta-reader e-reader share, Reedsy Marketplace integration with paid editors. |
| Obsidian + Novel Word Count plugin | Plugin, FOSS | Per-file / per-folder word, page, reading-time, character-count badges in the file explorer; per-note targets surfaced as progress percentages. |
| Obsidian + Excalidraw / Outliner plugins | Plugin, FOSS | In-vault freeform sketch canvas and structured nested-list outliner — auxiliary writer toolchain pieces that ship inside Obsidian. |

## Summary of where Novalist already stands strong

Before the gap list, the things Novalist already does well and most competitors do not match all at once:

- Fully offline, plain-files-on-disk, git-friendly, no account required.
- First-class extension SDK with 11+ hook surfaces.
- Built-in git client surfaced in the status bar.
- Custom entity types with custom property types (including typed EntityRef).
- Per-act / per-chapter / per-scene character overrides.
- Both Gregorian and fully custom in-world calendars.
- Per-scene snapshots with side-by-side diff complementary to git.
- Multi-book project with shared World Bible.
- Eight export formats out of the box including Shunn Modern preset.
- Localization shipped (English + German) with drop-in JSON.

## Gap matrix — what Novalist is missing

The table groups gaps by area, then ranks priority. Priority is a judgment call about how much daily-driver value a working novelist gets from the feature:

- **H** = High. Multiple competitors ship it; users actively miss it.
- **M** = Medium. Some competitors ship it; nice-to-have, not a deal-breaker.
- **L** = Low. Niche or out-of-scope for the project's positioning.

### Writing & editor

| Gap | Seen in | Priority | Notes |
| --- | --- | --- | --- |
| `@mention` autocomplete for entities while typing | LivingWriter (Story Elements autofill), Novelcrafter | H | Focus peek triggers on hover after the name already exists. Inline autocomplete lets the writer type `@Tho` and pick "Thorin" from a list — both as a name-spelling aid and as the link target. |
| Typewriter scrolling / center-line locking | Scrivener, iA Writer-style | M | Keeps the active line at a fixed vertical position. Long-form writers ask for it constantly. |
| Page view (visual page boundaries while writing) | Scrivener | M | Novalist has book-style preview width/spacing but not paginated rendering. |
| Built-in thesaurus / synonym lookup | Dabble, Scrivener | M | Currently must alt-tab to a browser. |
| Name generator | Scrivener | L | Scrivener-bundled name picker (by origin / gender). Niche but a recurring ask. |
| Up-to-4-pane split editor | Scrivener | L | Novalist has a 2-pane split. Diminishing returns past 2 panes. |
| Real-time collaboration / co-authoring | Dabble, World Anvil, Novelcrafter | L | Inherently conflicts with the offline-first / git positioning. Realistically out-of-scope. |
| Mobile companion / sync | Scrivener iOS, Dabble, LivingWriter | L | Same positioning conflict. |

### Scene structure & planning

| Gap | Seen in | Priority | Notes |
| --- | --- | --- | --- |
| Structured Goal / Conflict / Outcome fields per scene | yWriter (canonical), Plottr, LivingWriter, One Stop (Formal Scene Map) | H | Novalist tracks POV/emotion/intensity/conflict-tag automatically; it does **not** offer the Dwight Swain Goal-Conflict-Disaster triad as first-class scene metadata. Heavy plotters rely on this. One Stop's Formal Scene Map goes further (inner + outer motivation, inner + outer conflict, stakes) — same shape, more axes. |
| Insights / cross-reference matrix view | NovelPad (Insights Board) | M | NovelPad's filter combines characters × locations × plots × labels into a single query ("every scene where Anya is at the harbor on the heist plotline"). Novalist's Smart Lists are single-axis saved filters; a multi-dimensional pivot is missing. |
| Plot cards that group scene cards (board pivot) | NovelPad, Trello-style boards | M | Novalist Plot Grid is a row × column matrix; NovelPad's plot cards group scene cards visually on a board. Overlaps with the Kanban gap above. |
| Setup ↔ payoff / foreshadowing linker | StoryLine plugin | H | Pair a "planted" scene with the scene that pays it off and get warned when either side moves or is deleted. No competitor mainstream-ships this except StoryLine; high-value for revision. |
| Plot-hole / story-validation pass | StoryLine plugin | M | Automated checks (character on-screen without entry, entity referenced before introduction, plotline orphaned scenes, dangling setups). Codex Hub already has the data — runs cheaply. |
| Larger beat-sheet template library | Plottr (30+), LivingWriter (14), StoryLine (7+) | H | Novalist ships **Three-Act**, **Save the Cat**, **Hero's Journey**. Missing: Seven-Point, Story Circle, Romancing the Beat, 27-Chapter Method, Snowflake stages, Lester Dent pulp, mystery formula, romance arc, fantasy adventure. SDK-pluggable as project templates but valuable in the box. |
| Snowflake-method guided wizard | Manuskript (canonical), Plottr | M | Step-by-step idea → one-sentence summary → paragraph → character summaries → scene list. Novalist's templates seed structure but do not run the guided dialogue. |
| Scene archive (parking lot for deleted/abandoned scenes) | Novelcrafter | M | Snapshots cover per-scene history but there is no "archive" bucket that holds the whole scene out of the manuscript while keeping it browsable. |
| Multiple parallel drafts of the same book | Longform (Obsidian), Scrivener (via duplicated binder) | H | Longform's flagship: keep "Draft 1", "Editor's pass", "Experimental rewrite" under the same project, switch between them, compare. Novalist's snapshots are per-scene/per-version; the whole-book "what if I rewrote everything from chapter 7" experiment is awkward (clone the book folder by hand or fork via git). Native support for named drafts of the same manuscript would close this. |
| Nested / hierarchical scenes (scenes inside scenes) | Longform (Obsidian) | L | Novalist enforces a fixed Chapter → Scene hierarchy. Some writers want arbitrary nesting (parts containing chapters containing sub-scenes containing fragments). Project structure cost is non-trivial; demand is niche outside the Obsidian audience. |
| Kanban board view of scenes by status | StoryLine plugin, several others | M | Novalist has Corkboard + Outliner inside Manuscript view; neither pivots cards by status/act/POV. |

### Worldbuilding & codex

| Gap | Seen in | Priority | Notes |
| --- | --- | --- | --- |
| Interactive map with pins, layers, and articles | World Anvil, Campfire | H | Novalist's Locations are tree-structured but there is no spatial map view. Image Gallery only displays raster images. Pinning a Location entity onto an image map and clicking through to its editor is a flagship feature for fantasy authors. |
| Observers / participants linked to timeline events | Aeon Timeline | M | Aeon links each event to the entities who **participated** in or **witnessed** it. Novalist Timeline shows events but does not anchor an entity's perspective to each one — meaning "what did Anya see this year?" cannot be queried directly. |
| Entity age displayed alongside every event in timeline / scenes | Aeon Timeline | L | Novalist already computes character age from birth date + active in-world calendar; it surfaces in the character editor but not as inline annotation on every scene / event the character touches. |
| Family-tree pedigree view (multi-generational) | World Anvil | M | Novalist has a force-directed Relationships graph with family clustering. A pedigree tree (parents / children / siblings stacked by generation) is a different view from the same data — useful for dynasties. |
| Character psychology fields: wound / fear / lie / secret / want vs need | One Stop for Writers, Save-the-Cat-Writes-a-Novel community | M | Free-form sections cover it today, but no built-in template enforces the structure. Could ship as a "Psychology-driven character" project template. |
| Character interview questionnaire (guided creation) | Bibisco | M | Bibisco walks the writer through a question-by-question character interview. Achievable as an entity template + a one-shot wizard. |
| Built-in writer's thesaurus / reference library | One Stop for Writers (23 thesauruses) | L | Emotion / trait / trope / setting / weather / occupation reference data, browsable while writing. Big content lift; high differentiator for non-internet-using writers. SDK-pluggable as a sidebar contributor. |
| Premise / fabula / narrative-strand explicit fields per book | Bibisco | L | Book metadata fields beyond title + author. Trivially additive. |
| Character nicknames / aliases as first-class field driving mention detection | NovelPad, Novelcrafter | M | Novalist Characters have name + surname; no alias list. Once `@mention` autocomplete (gap above) lands, aliases become the natural extension — typing "the Stranger" still resolves to the same character. |
| Auto-detected character / entity mentions in prose | NovelPad (character tracker), Novelcrafter | M | NovelPad's character board re-reads the manuscript and lists every scene each character appears in. Novalist's Plot Grid is manual; auto-detection would be additive. |
| In-app freeform sketch / mind-map canvas | Bibisco mind maps, Obsidian Excalidraw | M | A whiteboard surface for brainstorming connections between any project items. Relationships graph and Corkboard each cover a slice; neither is a true freeform canvas. |
| Mention heatmap / appearance timeline per entity | Novelcrafter | M | "Where does this character actually appear?" — a strip across the chapters/scenes showing density. Novalist has scene mentions via plotlines/POV but no per-entity heat strip. |
| Entity progressions / per-event field history | Novelcrafter | M | Novalist already has per-chapter/per-scene character overrides — this is a closely related feature, but only for characters. A general "what changed when" log across any entity (e.g. a Faction's leader over time) is missing. |
| Structured magic-system editor | Campfire (Magic module) | M | Currently a Lore entity with free-form sections, or a custom entity type the user defines. Campfire's magic module ships ready-made fields (sources, costs, rules, limitations, practitioners). Achievable via the SDK / a project template, but not provided. |
| Language / conlang module | Campfire (Languages), World Anvil | L | Phonology, grammar, dictionary entries, translator. Niche but the audience that wants it wants it badly. |
| Species / culture / religion / philosophy domain modules | Campfire | L | All achievable as custom entity types today. The gap is that Novalist does not ship pre-baked schemas. SDK-pluggable. |
| Statblocks / TTRPG sheets | World Anvil | L | Novalist is not aimed at TTRPG GMs; out-of-scope. |

### Project management & analytics

| Gap | Seen in | Priority | Notes |
| --- | --- | --- | --- |
| Daily-words history chart (weeks / months / streak) | Scrivener (Writing History), Dabble | H | Dashboard shows today's words vs daily goal and project totals; it does not chart a rolling history. Writers who set streaks rely on the chart. |
| Session targets (per-sitting word goal with timer) | Scrivener | M | A sitting-scoped target distinct from the daily target. |
| Frequency / overused-word analyzer beyond echo phrases | Manuskript (frequency tool), ProWritingAid integrations | M | Novalist's echo phrases handle 2- to 5-grams. Single-word frequency, adverb density, "to be"-verb count, sentence-length histogram are common asks. |
| Characters-per-scene density chart | Novelcrafter | L | "Which scenes have too many speaking characters" — derivable from existing data. |
| Project keywords / global tag view | Scrivener Keyword HUD | M | Scenes have tags (auto-detected) and label colors; there is no global keyword index or filter-by-keyword across scenes. Smart Lists partly covers it. |
| Per-folder / per-scene word-count badges in the Explorer sidebar | Obsidian Novel Word Count plugin | L | Explorer currently lists titles; rolling word totals next to each chapter / scene give an at-a-glance pacing read without opening the Dashboard. |
| Length / POV / location / strand distribution analysis report | Bibisco analysis tools | M | Bibisco produces a structured report: total length, character air-time, POV balance, location frequency, narrative-strand spread. Novalist's Dashboard surfaces several of these inputs but does not assemble the single "is my novel balanced" report. |

### Output & publishing

| Gap | Seen in | Priority | Notes |
| --- | --- | --- | --- |
| Print-formatting parity (themes, ornamental breaks, drop caps, callouts) | Atticus, Vellum | H | Novalist exports EPUB/DOCX/PDF/Markdown/Fountain/LaTeX with a Shunn preset and chapter-level selection. Atticus-style theming (configurable chapter-heading templates, ornamental scene breaks, drop caps, image inserts at chapter starts, custom recto/verso headers) is the obvious missing layer. SDK-pluggable through export contributors but not bundled. |
| User-editable compile pipeline (ordered, swappable steps) | Scrivener Compile, Longform | M | Scrivener's "Compile" and Longform's workflow-based compile let the user assemble an export as a stack of steps (strip headings, prepend front matter, run regex, transform Markdown, pipe to Pandoc, etc.) and save the recipe. Novalist's export is preset-driven with a fixed pipeline per format. Could be exposed as an ordered list of `IExportStep` SDK contributions. |
| Device preview (Kindle, iPad, phone) for exports | Atticus | M | Lets the writer see exported EPUB rendered on simulated devices before shipping to KDP. |
| Web publishing / serialisation (read-only share link, chapter-by-chapter release) | World Anvil, Dabble | L | Offline-first positioning makes this a deliberate non-goal. |
| Beta-reader / shared-review workflow | Dabble, Novelcrafter | M | A way to send a draft to a named reader and receive comments back into the project. Could be implemented as a portable comments bundle in/out. |

### AI assistance

Novalist exposes AI-integration hooks in the SDK (prompt building, response processing, inline actions). In the box, **no AI features ship**. Competitors that lead with AI:

| Gap | Seen in | Priority | Notes |
| --- | --- | --- | --- |
| Inline rewrite / expand / describe / shorten | Sudowrite (canonical), Novelcrafter | M | SDK-pluggable. Listed because it is the single biggest reason readers pick Novelcrafter/Sudowrite over Scrivener in 2026. |
| Beat-to-prose generation | Sudowrite Story Engine, Novelcrafter Beats | M | SDK-pluggable. |
| Chat-with-scene / chat-with-codex | Novelcrafter | M | SDK-pluggable. |
| Scene summarization (one-click) | Novelcrafter, Sudowrite | M | SDK-pluggable. Could be a built-in convenience even without prose generation. |
| AI character / codex extraction from existing prose | Novelcrafter | L | SDK-pluggable. |
| AI feedback / editorial review pass | Sudowrite Feedback | L | SDK-pluggable. |
| Image generation from entity descriptions | Sudowrite Visualize | L | SDK-pluggable. |

Decision point for the project: ship an in-house "Novalist AI" extension with sensible defaults, or stay neutral and let third-party extensions cover the surface. The latter is consistent with the current direction but means the marketing comparison "supports AI" requires the user to install an extension.

### Import / interoperability

| Gap | Seen in | Priority | Notes |
| --- | --- | --- | --- |
| Import from Scrivener project (`.scriv`) | Plottr, LivingWriter, Aeon Timeline sync | H | The single biggest acquisition lever: lets a Scrivener user try Novalist without retyping. Non-trivial parser but the format is well documented. |
| Import from yWriter / DOCX / Markdown chapters | Plottr | M | DOCX-with-headings → chapters/scenes converter, Markdown front-matter → entities, etc. |
| Two-way sync with timeline / planner tools | Aeon Timeline ↔ Scrivener / Ulysses | M | Aeon keeps watch for changes in either project and syncs deltas both ways with field mapping. Novalist already has its own timeline so this matters less, but it is the standard for cross-tool workflows. |
| Round-trip with Plottr / Aeon Timeline | Plottr, Aeon | L | Useful only to the small slice of users who already use those tools. |

## Triage decisions — 2026-05-13

User read the full gap list and triaged. Decisions below override the per-row priority where they conflict.

### Accepted (want, intend to build)

| Gap | Notes from triage |
| --- | --- |
| `@mention` autocomplete for entities while typing | Build into core editor. Pairs naturally with the aliases-as-first-class-field item. |
| Typewriter scrolling / center-line locking | Editor toggle. |
| Page view (visual page boundaries) | Beyond current book-style width / spacing — actual paginated rendering. |
| Scene archive | Parking lot for abandoned scenes; out of manuscript but browsable / restorable. |
| Multiple parallel drafts of the same book | Named drafts under one book; switch active draft, copy scenes between drafts. |
| Interactive map view for Locations | New dedicated view → requires activity-bar entry per project rules. **Two modes**: Edit and View. See **Interactive map — spec** section below for full design. |
| Character nicknames / aliases as first-class field | Drives `@mention` resolution and auto-mention detection. |
| Daily-words rework + history chart | Treat as a ground-up rework of the word-count + writing-history pipeline, not just adding a chart. |

### Accepted, scoped up into a unified wizard system

The Snowflake wizard, the new-project setup, the character-interview questionnaire, and the guided entity creator collapse into a single tiered wizard engine:

1. **New-project wizard** — premise → one-line → paragraph → acts → chapter skeleton → cast seed. Snowflake-style sequence. Triggered on project creation as one of the template options.
2. **New-entity wizard** — pick entity type, then walk every built-in field + every template-declared custom property with: description, surfacing locations ("this shows up in the focus peek"), option explanations (e.g. age: literal vs. birth-date + interval), examples, skip-allowed. End screen reviews the entity.
3. **Character interview mode** — subtype of (2) specialised for Character; absorbs Bibisco's interview and One Stop's wound / fear / lie / secrets prompts.

Shared `WizardStep` model. Templates can declare extra steps. SDK contributors can add wizards or steps.

### Accepted, lower priority / partial

| Gap | Why deferred / partial |
| --- | --- |
| Entity age inline on every event / scene | Context sidebar already surfaces character age in scene context. Gap is mainly Timeline annotation — small win. |
| Atticus-class print formatting | Worth building in eventually. Not at the front of the queue. |
| Beta-reader / shared-review workflow | Planned, not scheduled. Shape: export = **single self-contained HTML file** that acts as a full reader app (chapter nav, reading progress, per-passage highlight + comment UI, export-comments action — all client-side, no server). Reader sends file (or extracted comments payload) back. Novalist re-imports and drops each comment onto its anchor in the **Comments** feature. Plain HTML + embedded JSON so it works in any browser and survives email. No work now — captured for a future cycle. |
| Scrivener `.scriv` importer | Acknowledged as largest acquisition lever, but no first-hand Scrivener experience makes debugging painful. Hold until a community contributor with a Scrivener project shows up, or until we can pair with one. |

### Interactive map — spec

Captured for future implementation. No code now.

**Two modes** toggled from the view's toolbar: **Edit** and **View**.

#### Edit mode

Behaves like a layer-based image editor specialised for cartography:

- **Layers** — ordered stack of image layers, same model as Photoshop / Krita / GIMP. Each layer holds one image plus its transform.
  - Add / delete / reorder / show / hide / lock per layer.
  - Per-layer opacity.
  - Layers can be grouped, and groups can be tagged as a **connected set** (e.g. group "Castle Aldwyn" containing layers "Floor 1", "Floor 2", "Floor 3", "Roof"). Members of a connected set are mutually exclusive — exactly one renders at a time in View mode.
- **Image placement on a layer**:
  - Drag image in from disk, clipboard, URL, or the project Image Gallery.
  - Position (x, y), rotation, uniform scale, non-uniform scale.
  - Free resize handles plus numeric inputs.
  - Snap-to-grid optional.
- **Zoom-stage visibility rules** per layer / per image:
  - Each image declares a **min zoom** and **max zoom** at which it renders.
  - Alternative metric: **min screen coverage** (image renders only when it covers ≥ N % of the viewport). Useful for level-of-detail swaps.
  - One image (e.g. continent silhouette) renders at low zoom; a more detailed one (city tiles) swaps in past a threshold.
- **Polygon mask edit** — per-image polygon clip path:
  - User adds vertices, drags them, inserts mid-edge vertices, closes the polygon.
  - Image is rendered clipped to the polygon. Used to align an irregular sub-map (e.g. a continent outline) against neighbouring images without rectangular seams.
  - Polygon stored alongside the image transform.
- **Pin placement**:
  - Drop a pin anywhere on the canvas.
  - Each pin links to a Codex entity (any type, but typically Location).
  - Pin shows entity name + small thumbnail (primary image).
  - A pin can also link to a **connected layer set** — clicking the pin in View mode swaps the rendered member (e.g. clicking the castle pin reveals interior floor layers).
  - Pin styles: dot, marker, custom SVG icon, per-entity-type default.
- **Inspector panel** on the right — shows transform / clip / zoom rules / link target for the selected image, layer, or pin.

#### View mode

Read-only navigation, behaves like an interactive map widget:

- **Pan and zoom** with mouse wheel, pinch, drag, keyboard.
- Zoom-stage visibility rules drive which image on each layer renders at current zoom.
- **Pin click** → opens the **focus peek** for the linked entity (same peek surface used in the editor). Double-click → opens entity in a tab.
- **Connected-layer pin** → clicking it swaps the visible member of its connected set (e.g. ground floor → first floor → roof). UI shows a small layer-switcher widget over the swap point. Esc returns to the parent layer of the enclosing set.
- **Minimap** in the corner for orientation when zoomed in.
- **Search box** filters and centres on a named pin.
- No accidental edits — drag selects/pans, no transforms.

#### Persistence

- The map definition is a JSON file under the book (e.g. `Books/<book>/Maps/<map>.json`) referencing image paths under the existing `Images/` folder.
- Layers, groups, pins, transforms, polygons, zoom rules → all in the JSON.
- Images themselves remain in `Images/` so the project's plain-files-on-disk story holds and git diffs stay legible.
- Multiple maps per project (world, continent, city, dungeon, etc.).

#### Rendering stack — decided

WebView already integrated into Novalist. Map view ships as a **custom HTML + JS app** bundled with the desktop, driven by the JSON map definition. Pan/zoom/clipping handled in the WebView with a Leaflet-style library (or a hand-rolled canvas renderer); the C# side just loads / saves JSON and routes pin clicks back into the focus-peek system. No native Skia layer needed.

#### Open questions (deferred)

- **Tile-pyramid generation** for very large maps (DeepZoom / DZI). Powerful for huge continent maps but setup burden on the user is high (pre-process source image into tiles). **Leaning no** — single large image per layer is the v1 story; revisit if anyone hits performance limits with realistic source assets.
- **Polygon-clipped image edges**: hard-edge only, or also support feathered / soft edges. Undecided. Hard-edge is trivial in canvas / SVG; feathered needs a blur mask. Decide when the first real test map shows whether seams are visible.

### Out of scope (covered elsewhere)

| Gap | Reason |
| --- | --- |
| All in-box AI features (rewrite, expand, describe, chat, beats, summarize, visualize, codex extraction, feedback) | Shipped by the **AIAssistant native extension** in the extension store. Core stays AI-neutral; the extension owns the surface. Strike from core-gap planning. |

### Untriaged so far (still in the gap list above, not yet decided)

Setup ↔ payoff linker; plot-hole validation; bigger beat-sheet template pack; structured Goal/Conflict/Outcome scene fields; NovelPad-style Insights cross-reference matrix; family-tree pedigree view; mention heatmap; structured magic-system / species / language modules; per-folder word-count badges in explorer; box-set export; user-editable compile pipeline; statblocks; collaboration; mobile companion; thesaurus reference library; observers on timeline events; Aeon-style sync.

## Roadmap (aligned with triage)

Reflects the 2026-05-13 triage. Ordered by suggested build order — small-and-high-impact first, then the larger systems. Not committed deadlines.

### Tier 1 — small, broad-impact, ship first

1. **`@mention` entity autocomplete in the editor** — type `@` → fuzzy picker over Codex entities → inserts styled reference token the focus peek already understands. Pairs with item 2.
2. **Character nicknames / aliases as first-class field** — multi-value alias list on Character (and any entity); drives `@mention` resolution and the auto-mention detector that comes later.
3. **Typewriter scrolling / center-line locking** — editor toggle.
4. **Scene archive** — out-of-manuscript parking lot for abandoned scenes; restorable.

### Tier 2 — medium systems, one per cycle

5. **Word count + writing-history rework** — ground-up rewrite of the counting + persistence + display pipeline. Includes daily / weekly / monthly history chart, streaks, session targets, per-folder / per-scene rollups, Explorer-sidebar badges. Treat the existing Dashboard goal block as input, not the destination.
6. **Page view (paginated rendering in the editor)** — actual page boundaries beyond current book-style width / spacing.
7. **Multiple parallel drafts per book** — named drafts under one book; switch active draft, compare, copy scenes between drafts. Lighter than git branching, surfaced in the book switcher.

### Tier 3 — unified wizard system

8. **Wizard engine** — single `WizardStep` model powering three call sites:
   - 8a. **New-project wizard** — premise → one-line → paragraph → acts → chapter skeleton → cast seed (Snowflake-style). One of the project-creation options.
   - 8b. **New-entity wizard** — guided walk through every built-in field + every template-declared custom property with description, surfacing locations, option explanations, examples.
   - 8c. **Character-interview mode** — subtype of 8b. Absorbs Bibisco's interview and One Stop's wound / fear / lie / secrets prompts.
   - Templates declare extra steps. SDK can contribute wizards / steps.

### Tier 4 — big new view

9. **Interactive map** — full spec in the *Interactive map — spec* section. Edit + View modes, layer stack with connected sets (floors), zoom-stage visibility, polygon-clipped images, pins to entities or to connected layer sets. Rendered in WebView from JSON + custom HTML/JS. New dedicated view → activity-bar entry per project rules.

### Tier 5 — eventually, lower urgency

10. **Atticus-class print theming** — chapter-heading template editor, ornamental scene-break picker, drop-cap toggle, recto/verso header configuration, plumbed through the export pipeline.
11. **Beta-reader HTML round-trip** — single-file self-contained HTML reader app with embedded chapter nav + comment UI; comments re-import into the Comments feature. No work scheduled.

### Explicitly out of scope for the core roadmap

- **All AI features** — owned by the **AIAssistant** native extension (rewrite, expand, describe, chat, beats, summarize, visualize, codex extraction, feedback). Core stays AI-neutral.
- **Scrivener `.scriv` importer** — on hold until a contributor with a real Scrivener project can pair on debugging.
- **Real-time collaboration / mobile companion / web publishing** — conflict with offline-first / plain-files-on-disk positioning.

### Still untriaged (decide in a later pass)

Setup ↔ payoff linker; plot-hole validation; structured Goal / Conflict / Outcome scene fields (and One Stop's deeper inner-vs-outer variant); bigger beat-sheet template pack; NovelPad Insights cross-reference matrix; family-tree pedigree view; mention heatmap; entity progressions across time; structured magic-system / species / language / religion / culture / philosophy modules; box-set / multi-book bundle export; user-editable compile pipeline; Aeon-style observers / participants on timeline events; in-app freeform sketch / mind-map canvas; built-in writer's thesaurus; project keyword HUD; characters-per-scene density chart; bibisco-style novel-balance analysis report; widow / spread auto-handling on PDF; device-live export preview; validated EPUB / PDF-X / ACE; statblocks; constructed languages; family-tree pedigree dedicated view; entity age annotations on timeline.

## Sources

- [Scrivener features](https://www.literatureandlatte.com/scrivener/features)
- [Obsidian StoryLine plugin](https://github.com/PixeroJan/obsidian-storyline)
- [Novelcrafter features](https://www.novelcrafter.com/features)
- [Plottr features](https://www.plottr.com/features/)
- [Sudowrite](https://www.sudowrite.com/)
- [Dabble Writer features](https://www.dabblewriter.com/features)
- [Campfire Writing — write](https://www.campfirewriting.com/write) and the 17-modules breakdown via [selfpublishing.com](https://selfpublishing.com/campfire-writing-review/)
- [World Anvil for authors](https://www.worldanvil.com/author)
- [Manuskript](https://www.theologeek.ch/manuskript/)
- [yWriter 7](https://spacejock.com/yWriter7.html)
- [Atticus](https://www.atticus.io/)
- [LivingWriter templates](https://livingwriter.com/en/writing-templates)
- [Longform plugin for Obsidian](https://github.com/kevboh/longform)
- [Aeon Timeline 3 — what's new](https://www.aeontimeline.com/whats-new/introducing-aeon-timeline-3) and [integrations](https://www.aeontimeline.com/features/integrations)
- [Vellum](https://vellum.pub/)
- [Bibisco](https://bibisco.com/) and [bibisco features](https://bibisco.com/writer-software-bibisco-features/)
- [One Stop for Writers — features and tools](https://onestopforwriters.com/features-tools) and [Storyteller's Roadmap](https://onestopforwriters.com/storytellers-roadmap)
- [NovelPad — features](https://novelpad.co/features) and [Insights Board](https://novelpad.co/blog/how-to-use-novelpads-insight-board)
- [Reedsy Studio](https://reedsy.com/studio)
- [Novel Word Count plugin for Obsidian](https://github.com/isaaclyman/novel-word-count-obsidian)
- [Excalidraw plugin for Obsidian](https://github.com/zsviczian/obsidian-excalidraw-plugin)
- [Outliner plugin for Obsidian](https://github.com/vslinko/obsidian-outliner)
