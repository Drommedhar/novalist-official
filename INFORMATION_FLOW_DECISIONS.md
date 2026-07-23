# Information-flow work — decisions log & things for you to review

Everything in `INFORMATION_FLOW_ANALYSIS.md` has been implemented. This file records
the judgement calls I made while working autonomously, plus the handful of things I'd
like you to confirm.

**State of the tree:** all work is **uncommitted** on `main`. You didn't ask me to
commit, and the guidance I run under is to commit only when asked — so I left the
changeset in the working tree for you to review. 77 files changed, ~2,900 insertions.

**Verification (all green):**

| Check | Result |
| --- | --- |
| `dotnet build novalist-official.sln` | succeeded |
| Novalist.Core tests | 955 passed |
| Novalist.Backend tests | 465 passed |
| Novalist.Sdk tests | 42 passed |
| Coverage gate — Core | **100%** (7609/7609) |
| Coverage gate — Backend | **100%** (5376/5376) |
| Coverage gate — Sdk | **100%** (15/15) |
| `tsc` web + node | clean |
| locale-doctor | 1567 keys × 3 locales, 0 missing / 0 untranslated / 0 placeholder drift |
| Emoji scan of added lines | 0 hits |
| AI Assistant extension (separate repo) | builds |
| **Playwright e2e** | **6 passed, 1 skipped** (the skip needs a packaged build) |

Two notes on the e2e run:

- `m1-realproject` and `m2-editor-typing` used to fail on Windows before the app
  even launched — they shelled out to `rsync` to copy the test project, and rsync
  does not exist there. I replaced that with Node's own recursive copy
  (`app/e2e/copyProject.ts`), so both now run on any platform. That is a test-infra
  fix, not a product change, but it is in the diff.
- New spec `app/e2e/m6-capture.spec.ts` drives "Add selection to entity" with real
  mouse gestures (drag-select, right-click, click the row) and asserts the picker
  opens quoting the selected passage — added after that flow was reported as doing
  nothing. It passes; see the note in the open-questions section.
- **One flaky backend test.** During one coverage run a single backend test failed;
  it did not reproduce across nine subsequent runs and I did not capture its name.
  The backend fixtures do real temp-directory file IO, which races with indexing /
  antivirus on Windows, so that is the likely cause. Flagging it rather than
  pretending the run was clean.

---

## [CONFIRM] Things I'd most like your call on

1. **Automatic bare-name linking in Wiki section prose.** Section text now auto-links
   any word/phrase resolving to exactly one entity (plus explicit `[[Name]]` links).
   Trade-off: a character literally named "Rose" turns every occurrence of that word
   into a link. Ambiguous names (2+ entities) never link. If you'd rather have
   **explicit `[[ ]]` only**, it's a one-line change in `WikiProseLinker.Linkify`.
   -> fine like that
2. **PeekCard "AI focus": you were right, and it is now restored (read side).**
   I deleted it as a permanently-empty stub. That was the wrong call — I checked,
   and the feature was real: your project still holds its output in
   `.novalist/settings.json` under `chapterAnalysis` (7 findings, several attributed
   to `Amy Calder` / `Dana Harrow`). What had happened is that the Electron rewrite
   ported neither the read nor the write half.

   **Restored now:** `entities/peek` surfaces the cached findings that name the
   hovered entity within the open chapter, matching on its name *and* aliases and
   for every entity type; the peek card renders them under "AI focus" again. The
   host only ever *reads* `ProjectSettings.ChapterAnalysis` — it never generates
   findings — so this keeps the no-AI-in-core rule intact. Hovering a character in
   your analysed chapter should show its findings immediately.

   The restored reader is a faithful port of the original
   `Novalist.Desktop/Editor/FocusPeekExtension.GetCachedAiFindings` (recovered from
   `a36e8e6^`, the commit before "Remove the old Avalonia UI"): same chapter-scoped
   lookup, same `scene_stats` exclusion, same `Excerpt` and per-type marker
   (`→` reference, `⚠` inconsistency, `•` suggestion) that
   `FocusPeekCardView.axaml` rendered. It matches aliases too, which the original
   did not.

   **The write half is not recoverable from git — it never lived in either repo.**
   I searched exhaustively: a pickaxe (`git log -S`) over all 294 commits of
   novalist-official shows `ChapterAnalysis` only ever appearing in three places —
   the `ProjectSettings` model, the `AiFinding` SDK model, and
   `FocusPeekExtension` (the *reader*) — from the initial commit onwards. The
   novalist-aiassistant repo has **never** referenced `ChapterAnalysis`,
   `SceneAnalysisResult`, `CachedAiFinding` or `ProjectSettings` in any of its 32
   commits, before or after its Avalonia→webview port. So the producer of the
   `chapterAnalysis` block in your project (timestamped 2026-04-03) was something
   outside both repositories' histories — most likely a Novalist build predating
   this squashed "Initial commit", or an extension binary whose source is not here.

   **[CONFIRM]** So closing the loop means *writing* it fresh, not restoring it.
   That is a contained job — Story Analysis already produces exactly the right
   shape (type / title / description / excerpt / entityName, per scene) — but it
   needs a new host API for extensions to persist findings, since the SDK exposes
   none today. Want me to build that?

3. **Scene analysis is now localized, driven by per-language JSON.** *(Resolved —
   you asked for this; it replaces the earlier "blank it out" behaviour.)* The
   keyword lists and the emotion key list moved out of hardcoded C# into
   `Novalist.Core/Resources/Analysis/analysis.<tag>.json`, one file per language,
   embedded in the assembly. English, German and Simplified Chinese ship — matching
   the bundled UI locales — and **adding a language is now a data change, not a code
   change**: drop in a JSON file and it is supported.
   - The JSON also declares the **emotion keys and their order**, which is what the
     Inspector's dropdown offers.
   - Keys stay **stable identifiers** across languages (the renderer localizes them
     via `emotion.*`, and scenes persist them), so switching writing language never
     invalidates a scene's stored emotion. A test enforces that every lexicon
     declares the same keys in the same order.
   - Regional tags fall back to their base language (`de-AT` uses `de`); `zh` finds
     `zh-CN`.
   - `wordBoundaries: false` in the Chinese file turns off word-boundary matching,
     since Chinese is not space-delimited. Sentence splitting and exclamation
     counting now understand the CJK forms (`。！？` and `！`) too.
   - A language with **no** lexicon (French, say) still blanks the keyword-derived
     fields with the explanatory note, rather than scoring prose against another
     language's words.
   - **[CONFIRM]** The German and Chinese word lists are my own translations of the
     English ones, chosen as stems (`kämpf` catches `kämpfen`). They are a
     reasonable first pass, not native-speaker-reviewed — worth a skim, especially
     the German stems, for false positives.
     -> german looks good and i dont speak chinese so the chinese user will correct them if they are wrong.

4. **Quick-capture inbox is a reserved `inbox` tag on a Research note**, not a new
   store or a new view. Keeps the on-disk format unchanged and means captures are
   searchable and taggable immediately. Filing actions are **non-destructive**: they
   copy the text onto the entity and clear the `inbox` tag, leaving the note behind.
   If you'd rather filing *moved* the note (deleting it), that's a small change.

---

## Decisions made, by area

### Wiki
- **Section-prose cross-links** are produced **server-side** by a new tested
  `WikiProseLinker` (Novalist.Core) that emits `nventity:type/id` Markdown links; the
  renderer intercepts that scheme. This replaces the dead `nv-entity-mention` span
  path (react-markdown was stripping the spans). Code spans, fenced code, images and
  existing Markdown links are protected. `[[Name|display text]]` is supported.
- **Cross-book appearances: labelled, not aggregated.** Appearances still come from
  the active book; in a multi-book project the heading now says *"Appearances in
  &lt;book&gt;"* so the scope is explicit. True cross-book aggregation would mean
  teaching `AppearanceIndexService` to walk every book's scene manifest — a
  materially bigger change I did not want to make unsupervised. **This is the one
  item from the analysis I deliberately solved with the cheaper of the two options
  I'd offered.**
- **Wiki birth dates** now read the structured `AgeMode`/`BirthDate` fields, falling
  back to sniffing free-text `Age` only for older records. The Wiki still shows the
  *date*, not a computed age — unlike the Inspector it has no scene to measure
  against. "Age at last appearance" is possible but was out of scope.
- Added: index filter box, `Contains` (child locations), `Research` section, `Events`
  section, and a note on articles with no appearances explaining where appearances
  come from.

### Capture
- **`@`-picker "Create" row** and the editor's **"Create entity from selection"** share
  one mechanism: the editor drops a `nv-mention-pending` placeholder immediately and
  the host upgrades it to a real mention once the entity exists (or reverts it to
  plain text on cancel/failure). So the writer never waits on a round-trip, and a
  cancelled create leaves the prose exactly as typed.
- **"Add selection to entity"** got a dedicated atomic `entities/appendToSection` RPC
  rather than a client-side read-modify-write of the whole sections array — avoids a
  lost-update race and works for custom types.
- **Quick capture** is `Ctrl+Shift+K`; **Quick Open** is `Ctrl+P`. Both were free.
  `Ctrl+P` deliberately mirrors the editor convention: content, not commands
  (`Ctrl+Shift+P` stays the command palette).

### Search
- **`search/global`** scans scene titles, prose, synopses, notes, comments, footnotes,
  every Codex entry (names, aliases, field values, custom props, sections), research
  items, and manual timeline events. No index — a novel-sized project is small enough
  to scan directly, and an index would need invalidation plumbing for little gain.
  Results are capped **per kind** so one noisy source can't crowd out the rest.
- Debounced 250 ms with a sequence guard, minimum 2 characters, because each query
  reads every scene file.

### Research
- `ResearchItem` gained **`entityRefs`**; linked items appear in a **Research** section
  on the entity's Wiki article and clicking one deep-links back with the item selected.
  I did **not** add scene refs — entity links cover the "visible while writing" need,
  and scene links would duplicate what tags already do. Tell me if you want them.
- **Drag-and-drop** needed a preload addition (`filePath`) because Electron 32+ removed
  `File.path`; the mobile shim returns `''` since mobile has no desktop file drops.
- **Fetch title** is on-demand only, HTTP(S) only, 10 s timeout, reads at most 256 KB,
  and fails silently to keep the app offline-first.

### AI seam
- New **`IEntityExtractionContributor`** mirrors `IArticleGeneratorContributor`
  exactly: the host assembles the passage and the known-names list, the extension
  returns **proposals only**, and the host filters (dropping known names and unknown
  type keys), shows the review list, and does every write. The extension has no write
  access and core has no AI dependency.
- Implemented in **both** the sample SDK extension (deterministic stub, so the seam is
  covered by tests) and the **AI Assistant** (`d:\git\novalist-aiassistant`), which
  prompts for a strict JSON array and parses it tolerantly.
- **Field-fill and section-suggestion were not built.** The analysis listed three AI
  ideas; extraction is by far the highest-value one and is a coherent, complete seam.
  The other two each need a differently-shaped contract (existing entity + proposed
  per-field values, with a diff UI). I'd rather design those with you than guess.

### Model changes worth knowing about
- `LocationData`, `ItemData`, `LoreData` gained **`Relationships`** (same shape as
  characters). The Codex editor now shows the relationships block for all four
  built-in types; characters keep the automatic-inverse behaviour, the others don't
  (they have no inverse concept). `Item.Origin` now links when it names a location.
- `SceneData` gained **`PlotlineNotes`** (per-plotline cell notes, right-click a cell).
- `SmartList.Color` was **deleted** (never set by any UI); `SmartList.PlotlineId` was
  **wired through** (it existed but was neither evaluated nor exposed).
- `WikiArticleCacheEntry.Model` was **deleted** — never populated, never read.

All of these are additive to on-disk JSON except the two deletions, which are
backward-compatible (unknown properties are ignored on load).

---

## Follow-ups I'd suggest, in priority order

1. Decide on the remaining **[CONFIRM]** items above — in particular, skim the German
   and Chinese analysis word lists.
2. Add analysis lexicons for any further writing languages you care about (copy
   `analysis.en.json`, keep the emotion keys, translate the words).
3. **Cross-book appearance aggregation**, if a shared cast across books matters to you.
4. **AI field-fill / section suggestions** — the two remaining ideas from section 4.4
   of the analysis.
5. **Manuscript import (DOCX/Markdown)** — listed in the analysis as a "Later" item and
   genuinely large (format detection, chapter splitting, style mapping). Not attempted.
