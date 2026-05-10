# Novalist vs Scrivener & Competitors — Full Parity Audit

## Context

Novalist (`e:\git\novalist-official`, .NET 8 / Avalonia desktop, Win+macOS) is a self-contained novel-writing app. Bundled AI extension lives at `E:\git\novalist-aiassistant`. Goal: full audit of gaps versus Scrivener and modern competitors (Ulysses, Dabble, Plottr, Novelcrafter, Sudowrite, Campfire), prioritized across **Editor**, **Organization**, **Plotting**, **Research**.

What Novalist already exceeds Scrivener on: codex with templates + custom entity types + per-chapter character overrides; native AI (LMStudio/Copilot, story analysis, codex-grounded chat); built-in Git; multi-book projects; LanguageTool grammar; multi-language readability (Flesch-Kincaid, Amstad, ARI) + reading time in [`TextStatistics.cs`](Novalist.Core/Utilities/TextStatistics.cs); customizable hotkeys; extension SDK.

Reference inventory (verified against code):
- Models: [`SceneData.cs`](Novalist.Core/Models/SceneData.cs) — has Title, Order, FileName, Date, WordCount, Notes, AnalysisOverrides (POV/emotion/intensity/conflict/tags). **No Synopsis.** [`ChapterData.cs`](Novalist.Core/Models/ChapterData.cs) — has Status (5 levels), Act, Date. [`BookData.cs`](Novalist.Core/Models/BookData.cs:44) — `SnapshotFolder = "Snapshots"` reserved but no snapshot model/service. [`TimelineData.cs`](Novalist.Core/Models/TimelineData.cs) — manual events with chapter+scene links, characters, locations, categories.
- Views: [`Novalist.Desktop/Views/`](Novalist.Desktop/Views/) — ManuscriptView, EditorView, ExplorerView, DashboardView, TimelineView, CodexHubView, ImageGalleryView, ContextSidebarView, FocusPeekCardView. **No** CorkboardView, OutlinerView, ScrivenningsView, ResearchView, PlotGridView, FocusModeView.
- Services: ProjectService, ExportService, GitService, GrammarCheckService, EntityService, SettingsService, FileService, UpdateService, ExtensionGalleryService, PluginImportService. **No** SnapshotService.
- Editor: WebView2 rich text in [`EditorView.axaml.cs`](Novalist.Desktop/Views/EditorView.axaml.cs). No focus mode / typewriter / split / inline comments / find-replace UI surfacing.
- AI extension: chat + analysis. No inline rewrite/expand/describe, no character chat, no synopsis/auto-tag generators.

---

## Gap Matrix

Legend: ✅ have · ⚠️ partial · ❌ missing

### 1. Editor

| Feature | Status | Notes |
|---|---|---|
| Rich text + spellcheck + LanguageTool grammar | ✅ | |
| Multi-language readability + reading time | ✅ | `TextStatistics.cs` |
| Word count + daily goal | ✅ | Dashboard |
| Composite multi-scene read flow | ✅ | `ManuscriptView` already concatenates scenes with status filters |
| **Snapshots / scene versioning** | ⚠️ | Folder reserved in `BookData.SnapshotFolder`; no model, service, or UI |
| **Focus / composition mode** | ❌ | No full-screen distraction-free toggle |
| **Split editor** | ❌ | |
| **Inline comments / annotations** | ❌ | Only out-of-band scene notes |
| **Footnotes / endnotes** | ❌ | |
| **Named paragraph styles** | ❌ | |
| **Project-wide find & replace** | ❌ | Not in views inventory |

### 2. Organization

| Feature | Status | Notes |
|---|---|---|
| Book→Chapter→Scene hierarchy + 5-level status | ✅ | |
| Scene-level POV/emotion/intensity/conflict/tags | ✅ | `SceneAnalysisOverrides` |
| **Synopsis field on scene** | ❌ | Not in `SceneData` — blocks corkboard/outliner |
| **Corkboard (index card grid)** | ❌ | |
| **Outliner table view** | ❌ | No columnar inline-editable view |
| **Collections / smart lists** | ❌ | No saved query (e.g. "all POV=Alice scenes") |
| **First-class colored labels** in tree | ⚠️ | Custom tags only |
| **Bookmarks / favorites** | ❌ | |
| **Project templates** (novel/screenplay/non-fiction) | ❌ | Single default skeleton |
| **Drag-reorder in board view** | ❌ | Tree-only reorder |

### 3. Plotting

| Feature | Status | Notes |
|---|---|---|
| Manual timeline + categories + chapter/scene/char/loc links | ✅ | `TimelineData` |
| `ChapterData.Act` field | ⚠️ | Field exists, no UI for act-level structure view |
| **Story-structure templates** (Save the Cat, Hero's Journey, 3-Act, 7-Point, 27-chapter, Snowflake) | ❌ | |
| **Beat sheet view** | ❌ | |
| **Plot Grid** (scenes × plotlines matrix) | ❌ | No `PlotlineData` model |
| **Subplot tracks visualization** | ❌ | |
| **Character arc per chapter visualization** | ⚠️ | Per-chapter character overrides exist; no arc graph |
| **In-world calendar / dates** | ❌ | Free-text `Date` only |
| **Outline export from timeline** | ❌ | |
| **Relationships network graph** | ⚠️ | `CharacterData` has relationships; no graph view |

### 4. Research

| Feature | Status | Notes |
|---|---|---|
| Image gallery | ✅ | `ImageGalleryView` |
| **Embedded PDFs** | ❌ | |
| **Embedded web pages / archived snapshots** | ❌ | |
| **Audio / video clips** | ❌ | |
| **Web link list with previews** | ❌ | |
| **Project-level research notes folder** | ❌ | Scene notes only |
| **Side-by-side reference + editor** | ❌ | |

### 5. Compile / Export

| Feature | Status | Notes |
|---|---|---|
| EPUB/DOCX/PDF/MD + title page + chapter selection + SDK contributors | ✅ | |
| **Compile presets** (font, margins, scene separator, chapter heading style) | ❌ | |
| **Industry-standard manuscript (Shunn) preset** | ❌ | |
| **MOBI / KF8** | ❌ | EPUB only |
| **Final Draft (.fdx) screenplay** | ❌ | |
| **LaTeX** | ❌ | |

### 6. AI (extension at `E:\git\novalist-aiassistant`)

| Feature | Status | Notes |
|---|---|---|
| Codex-aware chat, story analysis, LMStudio + Copilot | ✅ | |
| AI auto-tag POV/emotion/intensity (story analysis) | ✅ | Done by AiAssistant when enabled |
| Inconsistency detection + entity reference checks | ✅ | Story analysis |
| **Inline selection actions** (Rewrite / Expand / Describe / Shorten / Show-don't-tell) | ❌ | Sudowrite parity |
| **Character chat / roleplay** (uses existing character sheets as persona) | ❌ | Novelcrafter parity. Codex characters already provide rich persona data |
| **AI synopsis generator** per scene/chapter | ❌ | Required once corkboard ships |
| **Brainstorm "what next"** continuation | ⚠️ | Possible via free-form chat; no first-class action |

### 7. Sync / Collab

❌ No cloud, mobile, or co-author. Out of scope for this audit (Git covers single-author).

---

## Recommended Priority

### P0 — Highest impact (Scrivener parity, unlocks downstream)

1. **`Synopsis` on `SceneData`** — string property; backward-compatible nullable. Blocks #2, #3, AI synopsis generator. File: [`SceneData.cs`](Novalist.Core/Models/SceneData.cs).
2. **Snapshot service + model** — finish the stub already implied by [`BookData.SnapshotFolder`](Novalist.Core/Models/BookData.cs:44). New `SceneSnapshot` model, `SnapshotService` (take/list/diff/restore), UI in editor ribbon. Files written under `<book>/Snapshots/<sceneId>/<timestamp>.json`.
3. **Corkboard view** — virtual card grid per chapter; cards show synopsis, status color, word count; drag reorder. New `CorkboardView.axaml` + `CorkboardViewModel` reusing `ManuscriptViewModel` data sources.
4. **Outliner table view** — columnar inline-editable grid (Title / Synopsis / Status / POV / WordCount / Tags). Companion to corkboard. New `OutlinerView.axaml`.
5. **Focus / composition mode** — full-screen toggle on `EditorView`; hide chrome. Hotkey-bindable through existing hotkey system.
6. **Project templates** — bundled starter skeletons (3-Act Novel, Hero's Journey, Save-the-Cat, Screenplay, Non-fiction). New `ProjectTemplate.cs` + selector in [`WelcomeView`](Novalist.Desktop/Views/WelcomeView.axaml). Templates seed chapters with `Act`, plotline placeholders, and timeline beat events.

### P1 — High impact

7. **Split editor** — second pane via `Grid` + `GridSplitter` hosting a second `EditorView` instance.
8. **Inline comments** — anchor to text range in WebView2 (decoration JSON stored on scene); right-side comment list. Reuse `SceneNotesView` chrome for visual consistency.
9. **Project-wide find & replace** — service over scene file index; results panel with jump-to-scene; replace-all with snapshot pre-take.
10. **Research panel** — new top-level `ResearchData` (PDF via PDFium/PdfPig, archived web HTML, audio/video via Avalonia media, link list with thumbnails). New `ResearchView.axaml`. Side-by-side dock toggle next to editor.
11. **Plot Grid** — new `PlotlineData` (id, name, color, description); matrix view: rows = plotlines, cols = chapters; cells = scene cards assignable. New `PlotGridView.axaml`.
12. **Story-structure templates on timeline** — bundle Save-the-Cat (15 beats), Hero's Journey (12), 3-Act (8), 7-Point, 27-chapter as `TimelineManualEvent` presets selectable from `TimelineView`.
13. **Compile presets** — per-format `ExportPreset` (font, margin, scene separator, chapter heading template, page numbers). Extend [`ExportService.cs`](Novalist.Core/Services/ExportService.cs). Bundle Shunn manuscript preset.
14. **Collections / smart lists** — saved query DSL (status/POV/tag/plotline) over scenes; persist in `ProjectMetadata`; surface in `ExplorerView` as virtual folders.

### P2 — Modern AI parity (in `novalist-aiassistant`)

15. **Character chat** — chat panel mode where AI roleplays as a chosen `CharacterData` with codex grounding. Existing character sheets (demographics, role, relationships, per-chapter overrides, custom properties) feed the persona system prompt. New `Services/CharacterChatService.cs`.
16. **Inline selection actions** — context menu on selected text → Rewrite / Expand / Describe / Shorten / Show-don't-tell / Brainstorm. New `Services/InlineRewriteService.cs`.
17. **AI synopsis generator** — populate `SceneData.Synopsis` from scene text. New `Services/SceneSynopsisService.cs`. Reuse story-analysis pipeline for batch generation.
18. **Brainstorm "what next"** — first-class action: given preceding scenes + codex, propose 3 continuations.

### P3 — Polish

19. Footnotes / endnotes.
20. Named paragraph styles (couples to compile presets).
21. Relationships network graph (visualize existing `CharacterData` relations).
22. Bookmarks / favorites in tree.
23. MOBI / Final Draft (.fdx) / LaTeX export.
24. In-world calendar with date arithmetic; bind `ChapterData.Date` and `TimelineManualEvent.Date`.
25. Outline export from timeline.

### Out of scope

Cloud sync, mobile, real-time collab — single-author + Git suffices.

---

## Critical Files

**New models**
- `Novalist.Core/Models/SceneSnapshot.cs`
- `Novalist.Core/Models/PlotlineData.cs`
- `Novalist.Core/Models/ResearchData.cs` (item types: pdf, web, audio, video, link, note)
- `Novalist.Core/Models/ProjectTemplate.cs`
- `Novalist.Core/Models/ExportPreset.cs`
- `Novalist.Core/Models/SmartList.cs`

**Modified models**
- [`SceneData.cs`](Novalist.Core/Models/SceneData.cs) — add `Synopsis`, optional `LabelColor`, optional comment-anchor list
- [`ProjectMetadata.cs`](Novalist.Core/Models/ProjectMetadata.cs) — add `SmartLists`, `Plotlines`, `ResearchItems`

**New services**
- `Novalist.Core/Services/SnapshotService.cs`
- `Novalist.Core/Services/FindReplaceService.cs`
- `Novalist.Core/Services/ProjectTemplateService.cs`

**Modified services**
- [`ExportService.cs`](Novalist.Core/Services/ExportService.cs) — preset support
- [`ProjectService.cs`](Novalist.Core/Services/ProjectService.cs) — snapshot path resolution, plotline/research persistence

**New views**
- `Novalist.Desktop/Views/CorkboardView.axaml`
- `Novalist.Desktop/Views/OutlinerView.axaml`
- `Novalist.Desktop/Views/ResearchView.axaml`
- `Novalist.Desktop/Views/PlotGridView.axaml`
- `Novalist.Desktop/Views/SnapshotHistoryView.axaml`
- `Novalist.Desktop/Views/FindReplaceView.axaml`

**Modified views**
- [`EditorView.axaml(.cs)`](Novalist.Desktop/Views/EditorView.axaml) — focus mode, split host, comment anchors
- [`WelcomeView.axaml`](Novalist.Desktop/Views/WelcomeView.axaml) — template chooser
- [`ExplorerView.axaml`](Novalist.Desktop/Views/ExplorerView.axaml) — Smart Lists section
- [`TimelineView.axaml`](Novalist.Desktop/Views/TimelineView.axaml) — story-structure preset picker

**SDK** ([`Novalist.Sdk/Hooks/`](Novalist.Sdk/Hooks/))
- New contributor interfaces if templates / research importers / story-structure presets should be extension-pluggable

**AI extension** (`E:\git\novalist-aiassistant`)
- `Services/CharacterChatService.cs` — persona prompt built from `CharacterData` (demographics, role, relationships, custom properties, per-chapter overrides)
- `Services/InlineRewriteService.cs` — selection-context actions
- `Services/SceneSynopsisService.cs` — synopsis generation (auto-tag already covered by existing story analysis)
- `Views/` + ribbon hooks for selection-context actions and character-chat mode

---

## Verification

For each P0 item, end-to-end manual test:

1. **Synopsis** — open scene, set synopsis in side panel, save, reload project; verify persisted in `scenes.json`. Old projects (no `synopsis` key) load without error.
2. **Snapshots** — edit scene, take named snapshot, edit further, list shows entries with timestamps, restore reverts content + word count. Snapshot files appear under `<book>/Snapshots/<sceneId>/`. Project close+reopen retains history.
3. **Corkboard** — open chapter, see N cards = N scenes with synopsis + status color. Drag to reorder; switch back to manuscript tree, order persisted in `scenes.json`.
4. **Outliner** — toggle into outliner; edit Status / POV / Synopsis inline; reload project; values persist.
5. **Focus mode** — bound hotkey toggles full-screen; chrome hidden; typewriter mode keeps caret near vertical center across line breaks; Esc restores chrome and previous layout.
6. **Templates** — `New Project` from Welcome → "Save the Cat Novel" → project opens with prepopulated chapters (`Act` set), 15-beat timeline events, plotline placeholders.

P1 smoke (after build):
- Split editor — open scene A left, scene B right; edit both; both save independently.
- Inline comments — select text, add comment, anchor survives further edits to surrounding text; reload preserves anchor.
- Find/Replace — search across all scenes; replace-all triggers automatic snapshot for each affected scene.
- Research — drop PDF, view inline; drop URL, archived HTML opens offline; toggle side-by-side with editor.
- Plot Grid — create plotline, assign scenes; toggle scene status from grid cell, propagates to scene data.

Regression:
- Open a pre-change `.novalist` project; confirm load + save round-trip preserves all data (new fields are nullable / defaulted).
- Run any existing tests under `Novalist.*.Tests/` if present (none found in inventory — `dotnet build` and manual smoke only).
- Verify EPUB/DOCX/PDF/MD export still produce identical bytes when no preset is selected (preset = optional).
