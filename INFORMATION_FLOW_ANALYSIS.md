# Information flow in Novalist — an honest assessment

Scope: how well the app (a) gives the writer access to information while they work, and (b) lets them put information down where it belongs, with minimal friction. Based on a code-level sweep of the renderer, backend RPCs, core services, the manual, and the AI Assistant extension (July 2026, post-Wiki).

The short version: **Novalist is now very good at *presenting* structured information once it exists, and surprisingly weak at two things: finding information globally, and capturing information without leaving the editor.** The Wiki, hover peek, and Inspector form a strong retrieval layer for entities — but everything upstream of that (search, capture, notes) is fragmented or missing. The single biggest theme below is: the writing surface is a read-only consumer of the knowledge base; it should also be a producer.

---

## 1. What already works well

Worth stating so the criticism has context:

- **One derivation pipeline, shared everywhere.** `AppearanceIndexService` + `EntityResolveIndex` (Novalist.Core/Services) feed the Wiki, the editor peek, and the Inspector identically. Appearances, co-occurrence, stats, and ambiguity handling agree across surfaces. This is the right architecture and everything proposed below can build on it.
- **The Wiki is a genuinely strong reader.** Infobox, appearances-as-timeline, referenced-by, appears-with, changes-over-time for character overrides, map-pin links, and the on-demand cached AI summary (clean SDK seam via `IArticleGeneratorContributor`, no core AI calls). Test coverage is real.
- **The focus-peek card** is the best in-flow retrieval surface in the app: override-aware, relationship-navigable, reachable from prose hover and from Inspector cards.
- **Codex capture depth** is excellent once you are *in* the Codex: wizards, the character interview, templates, custom types, overrides, reciprocal relationships.

The gaps below are mostly absences around this solid core, not flaws in it.

---

## 2. Broken or half-dead things (fix before building new features)

These are small, but they undermine trust in the surfaces that exist.

1. **Wiki section-prose cross-links are dead.** `WikiArticle.tsx` renders sections through react-markdown *without* `rehype-raw`, so persisted `nv-entity-mention` spans are stripped; `onProseClick` and the `idToType` map are dead code, and `wiki.css` still styles a class that never renders. The manual (`docs/manual/30-wiki.md`, "Entity mentions inside section prose are clickable") promises behavior that no longer happens — and Codex sections are authored in a plain textarea, so there is no way to *produce* a mention span in section prose in the first place. Either restore rendering (rehype-raw or a custom renderer) **and** add mention support to section editing, or do plain-text name resolution over section prose via `EntityResolveIndex` (probably the better fix: no authoring change needed, links appear automatically). Update the manual either way.
2. **Smart Lists have a dead plotline filter.** `SmartList.cs` defines `plotlineId` (and `color`), but `SmartListsRpc` never exposes them, so the UI can't use them. Either wire them through or delete them.
3. **PeekCard "AI focus" is a permanent stub** (`PeekCard.tsx` ~343): it always renders the localized placeholder because the chapter-analysis pipeline was never exposed over RPC. Ship it or remove the section — a card that always shows a stub reads as broken.
4. **`WikiArticleCacheEntry.Model` is written by nobody.** Either populate it from the generator result (useful when regenerating after a model upgrade) or drop the field.
5. **Wiki birth-date handling is cosmetic.** `CharacterData` has structured `BirthDate` / `AgeMode` / `AgeIntervalUnit`, but `WikiRpc` only reads the free-text `Age` and relabels it if it parses as a date. The Inspector and peek already compute age-at-scene properly; the Wiki should use the same structured fields.

---

## 3. Retrieval — finding and reading information

### 3.1 There is no global search. This is the biggest single gap in the app.

Today the writer has:

- **Find & Replace** — scene *prose* only (`SearchRpc` / `FindReplaceService`). It does not see entity fields, sections, synopses, scene notes, comments, footnotes, research, or titles.
- **Command palette** — actions only. It cannot navigate to content at all.
- **Per-view filter boxes** — Codex (name only), Research (title/content/tags), Relationships (name). All siloed, all substring.
- **The Wiki index has no search box at all** — ironic, since it is the designated browse-everything surface.

A writer who remembers "I wrote something about the frost oath ritual somewhere" has no way to find out whether it lives in a scene, a lore entry's section, a scene note, a comment, or a research note. Every one of those is a separate manual hunt. For an app whose pitch is "your world's knowledge base," this is the gap users will hit daily.

**How:** one `search/global` RPC that fans out over scene text (reuse `FindReplaceService`), entity names/aliases/fields/sections, synopses, scene notes, research items, comments/footnotes, and timeline events — returning typed hits (type, title, snippet, target ref). One renderer surface: a quick-open dialog (`Ctrl+P` or extend the palette with a `?`/text mode) with type-grouped results; Enter navigates (scene → editor, entity → Wiki, research → Research view). Everything is already loadable in one pass server-side (WikiRpc's article builder proves it); a naive scan is fine at novel scale — no index needed initially. Quick win inside the same feature: a filter box on the Wiki index (client-side, one afternoon).

### 3.2 Dead-end views: information you can see but not follow

Several surfaces show a fact and then strand you — each is a one-line-ish fix and together they would make the app feel dramatically more connected:

- **Relationships graph nodes are not clickable** (`RelationshipsView.tsx`) — the obvious gesture (click a character, get their article or peek) does nothing.
- **Timeline entity chips are plain text** (`TimelineView.tsx` ~331) — events link to scenes but not to the characters/locations they display.
- **Plot Grid scene column headers don't open the scene** (`PlotGridView.tsx` ~63) — only a tooltip.
- **Dashboard "Recent activity" rows are display-only** (`DashboardView.tsx` ~356) — a list of recently edited scenes you cannot click is a to-do list with no checkboxes.

**How:** all four already have ids in their DTOs; wire `openScene` / `wikiStore.openArticle` / `useEntityPeek` onto the existing elements. This is a single small PR ("every entity or scene shown anywhere is a link") and arguably a product rule worth adding to CLAUDE.md afterwards.

### 3.3 The Inspector is confined to the writing views

`ContextPanel` only mounts for `write`/`manuscript`. That is defensible (it is scene context), but the *peek* layer deserves to be universal — e.g. hovering a name in a Timeline event, a research note, or a Wiki appearance row should raise the same card. The hook (`useEntityPeek`) is already shared; it just isn't wired outside editor/Inspector/Wiki.

### 3.4 Scene-analysis heuristics are English-only

`ContextRpc` emotion/intensity/conflict/POV detection runs on hardcoded English keyword lists even for German/Chinese projects — the Inspector will confidently show wrong values for two of the three shipped languages. Honest options: per-language keyword lists (cheap, mediocre), marking the fields "not available for this language" instead of silently wrong, or an SDK contributor seam so the AI extension can supply analysis (consistent with the no-core-AI rule). The silent-wrong status quo is the worst of the three.

---

## 4. Capture — putting information down where it belongs

### 4.1 The editor is a capture dead zone

From inside the writing surface, the writer can add: a comment, a footnote, a dictionary word. That's it. Confirmed by grep: `entities/create` has exactly one caller in the whole renderer — the Codex "New entry" dialog.

Concretely missing, in rough order of value:

1. **Select text → "Create character / location / item / lore"** as a built-in context-menu item (and inline action). Selection becomes the name; the entity is created with the current scene recorded; a toast offers "Open in Codex" for details. The plumbing for selection-gated actions already exists (`registerInlineAction`, extension context-menu items) — nothing built-in uses it.
2. **`@`-mention picker: "Create '<typed name>'…" row on no match.** Today the picker says "No matches" and abandons you at the exact moment you have a new name under your cursor. This is the single cheapest high-value capture fix in the app: the picker already has the name, the scene, and the insertion point; creating the entity and inserting the mention span in one gesture closes the loop that feeds `AppearanceIndexService`.
3. **Select text → "Add to entity section"** — append a selected passage (a description you just wrote of a place, a character's mannerism) to a chosen entity's section, with a back-reference. This is the direct answer to "put it down where it belongs": the information is *already written*; it just has no way to travel from prose to the Codex except copy-paste through three view switches.
4. **Command palette creation verbs** — "New character…", "New research note…", "New timeline event…" as palette commands. The palette is fed only by `buildDefaultHotkeys()`; it needs a command registry that accepts non-hotkey commands anyway (extensions will want this too).

### 4.2 No quick capture / inbox / scratchpad — anywhere

Grep for inbox/scratchpad/quick-capture across renderer and backend: zero hits. There is no global "jot this down now, file it later" mechanism, and no project-level notepad. The five note-ish mechanisms that do exist (scene notes, synopsis, comments, footnotes, entity sections, research notes) are all *pre-filed* — each demands you first decide where the note belongs and navigate there. That is backwards for the most common writing situation: an idea arrives mid-sentence and you have four seconds of willingness to record it.

**How:** a global hotkey opens a minimal capture popover (one textarea, Enter to save, Esc to cancel) writing to a project-level **Inbox** — plausibly just a reserved Research tag/type, avoiding a new store. The Inbox surfaces as a Research section (and optionally a Dashboard tile: "3 unfiled notes"). Each inbox item gets one-click "file as…" actions: → research note, → scene note of current scene, → new entity, → append to entity section. Capture is instant; filing is deferred. This one feature plus 4.1 would transform capture friction more than anything else on this list.

### 4.3 Research is an isolated island

- A "Link" is a URL string in a textarea — no fetch, no title, no snapshot of content ("Research" promises a scrapbook; this is a bookmark field).
- **No drag-and-drop** of files (the only `onDrop` handlers in the app are reorder-only) and no paste-to-create, even though *entity images* already support paste-from-clipboard and from-URL (`EntityImages.tsx`) — the richer capture exists in the codebase and wasn't generalized.
- **No cross-links in either direction**: a research item cannot reference or be referenced by an entity or scene; nothing in the editor, peek, or Wiki ever surfaces research. Research the writer diligently collects is invisible at the moment of writing — the exact moment it exists for.

**How:** (a) drag-drop and paste onto the Research list, reusing the image/file import paths; (b) URL title fetch for links (keep offline-first: fetch on demand, degrade to raw URL); (c) an `entityRefs`/`sceneRefs` list on `ResearchData` + a "Research" section on Wiki articles and a peek row, so tagged research follows the entity around; (d) include research in global search (3.1).

### 4.4 The AI Assistant generates prose but never structures information

The extension can rewrite, expand, brainstorm, and summarize — but it contains zero calls that create or update entities. Given how much of a manuscript *is* unstructured entity data, the missing flows are:

- **"Extract entities from this scene/chapter"** — propose characters/locations/items found in prose that aren't in the Codex yet, with a review-and-accept list (never silent creation).
- **"Fill fields from the manuscript"** — for a selected character, scan their appearances and propose eye color, role, relationships, etc., diffed against current values.
- **"Suggest section content"** — draft a Background section from what the manuscript establishes.

**How:** consistent with the standing rule (AI reaches the app only via extension contributions), this means new SDK seams mirroring the `IArticleGeneratorContributor` pattern — e.g. an `IEntityExtractionContributor` where the *host* builds the deterministic scene/entity dossier and owns the review UI plus the actual `entities/create`/update writes, and the extension only returns proposals. Core stays AI-free; the extension stays write-free. This is the highest-leverage AI feature the app could ship, and the wiki-summary seam already proved the pattern.

### 4.5 Smaller capture friction, noted honestly

- **Relationships view is read-only** with no hint of where relationships are authored — a first-time user will look for "add" here and conclude the feature is missing. Minimum: an empty-state/toolbar note linking to the Codex; better: click-node → peek → "Edit in Codex".
- **Plot Grid cells are booleans** — no per-cell note ("what does this scene do for this thread?"), which is the main thing plot grids are for in Scrivener-alikes. A one-line text per cell would double the feature's value.
- **Import is Obsidian-plugin-only.** No DOCX/Markdown manuscript import despite DOCX export existing. Anyone migrating an existing novel starts with paste. (Large, but worth keeping on the map.)

---

## 5. Wiki — second-round improvements

Beyond the fixes in section 2, ranked by value:

1. **Index search box** (also covered by 3.1; do the client-side filter immediately regardless).
2. **Child locations on parent articles.** `BuildReferencedBy` scans only character relationships and custom EntityRef fields — `Location.Parent` is never reverse-scanned, so "Aldland" never lists its cities. A "Contains" section is cheap and makes location articles actually wiki-like.
3. **Surface timeline events.** The app has authored timeline data; the Wiki's "Appearances" is purely mention-derived. Manual events referencing an entity (today the link is display-text only — worth making a real entity ref while at it) belong on the article as an "Events" block merged into the appearances chronology.
4. **World Bible entities are active-book-only.** Appearances/stats/plotlines/pins all derive from the active book, so a trilogy's shared protagonist shows one book's worth of history with no indication the rest exists. Either aggregate across books (grouped by book) or label the section "Appearances in <book>".
5. **Location/Item/Lore have no relationships at all** — the data model omits the field, so those articles always lack the connective tissue that makes wikis browsable (item → owner, location → faction). Consider generalizing relationships beyond characters, or at least resolving `Item.Origin` text against `EntityResolveIndex` for a link.
6. **Un-mentioned entities get empty articles** with no explanation. A gentle note ("Not yet mentioned in any scene — appearances are built from @-mentions and recognized names") plus a link to the editor docs would turn confusion into instruction, and dovetails with the @-create flow (4.1.2) that makes mentions more likely to exist.

---

## 6. Suggested order of attack

**Now (small, high leverage, mostly wiring):**
1. Fix the dead Wiki section links + manual drift (section 2.1).
2. Make everything clickable: relationships nodes, timeline chips, plot-grid headers, dashboard recent activity (3.2).
3. Wiki index filter box (5.1) and "Contains" for locations (5.2).
4. `@`-picker "Create new entity" row (4.1.2).
5. Remove or wire the dead SmartList plotline filter and the PeekCard AI-focus stub (2.2, 2.3).

**Next (each a real feature, all renderer+one-RPC scale):**
6. Selection → create entity / append to entity section, context menu + inline actions (4.1.1, 4.1.3).
7. Global search + quick open (3.1) — the flagship of this batch.
8. Quick-capture popover + Inbox with file-as actions (4.2).
9. Research: drag-drop/paste capture, link titles, entity/scene refs surfaced in Wiki and peek (4.3).

**Later (bigger design surface):**
10. AI entity extraction / field-fill via new SDK contributor seams (4.4).
11. Timeline events on Wiki articles + cross-book appearances (5.3, 5.4).
12. Relationships for non-character types (5.5); plot-grid cell notes (4.5); manuscript import.

Items 4, 6, 7, and 8 are the ones that change the daily writing experience; everything in "Now" is about making the existing investment feel finished.
