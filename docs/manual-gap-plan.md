# Manual Gap — Implementation Plan

Derived from `manual-gap-audit.md` + user decisions. Three streams of work:
**A) Code changes** (build feature so manual becomes correct).
**B) Manual edits** (feature won't be built; correct the docs).
**C) Verification needed first** (decision pending — confirm state, then route to A or B).

---

## A) Code changes — build feature to match manual

### A1. Hotkeys & menus

**A1.1 — Hotkey focus-context routing** _(was #3, #53)_
- Build minimal `HotkeyContextResolver` in `Novalist.Desktop` reading focused element ancestor chain. Returns `ContextScope` (`Editor` / `Explorer` / `Any`).
- Extend `HotkeyDescriptor` with optional `ContextScope`.
- `HotkeyDispatcher` consults resolver before invoking command.
- Reassign `app.editor.addComment` to `ContextScope.Editor`, `app.chapter.create` to `ContextScope.Explorer` — resolves `Ctrl+Shift+M` collision.
- Also retag `app.editor.bold` (Ctrl+B) as `Editor` so Ctrl+B outside editor falls through to `app.nav.toggleExplorer`. Same for `Ctrl+Shift+P` aliasing.
- Tests: focus editor → Ctrl+Shift+M = Add Comment; focus explorer → Ctrl+Shift+M = Create Chapter.

**A1.2 — Edit → Add Comment / Add Footnote menu items** _(was #4)_
- Add `AddCommentCommand` / `AddFootnoteCommand` on `MainWindowViewModel` delegating to active `SceneViewModel`.
- Add `<MenuItem>` rows under Edit in [MainWindow.axaml:23-58](../Novalist.Desktop/MainWindow.axaml#L23-L58).
- Locale keys `menu.addComment`, `menu.addFootnote` in en.json + de.json.
- Wire `InputGesture` references so existing hotkeys appear next to menu label.

### A2. Context sidebar

**A2.1 — Add `Somber` emotional tone** _(was #7)_
- Append `somber` profile to `EmotionProfile` list at [ContextSidebarViewModel.cs:95-112](../Novalist.Desktop/ViewModels/ContextSidebarViewModel.cs#L95-L112).
- Keyword seed: `grim`, `mournful`, `bleak`, `subdued`, `solemn`, `heavy`.
- Add locale `context.tone.somber`.

### A3. Explorer

**A3.1 — Explorer search box** _(was #9)_
- Add `SearchText : string` (observable) to `ExplorerViewModel`.
- Insert `<TextBox Watermark="{loc:Loc explorer.search}">` above tree in [ExplorerView.axaml](../Novalist.Desktop/Views/ExplorerView.axaml).
- Apply `ICollectionView` filter on chapter/scene title (case-insensitive contains).
- Hide empty chapters when no children match.
- Locale `explorer.search` (en/de).

**A3.2 — Multi-select + bulk operations** _(was #11)_
- Set `SelectionMode="Multiple"` on Explorer list / tree control.
- Add `SelectedScenes : ObservableCollection<SceneViewModel>` on `ExplorerViewModel`.
- Refactor delete / move-to-chapter / set-status commands to accept collections.
- Context-menu items display "Delete N scenes" / "Move N scenes" when selection > 1.

**A3.3 — Right-click `Duplicate`, `Set status →`, `Open in split pane`** _(was #12)_
- `DuplicateSceneCommand` / `DuplicateChapterCommand` on `ExplorerViewModel`: deep-clone via `JsonSerializer`, new id, insert after source.
- `Set status →` submenu binding to `StoryStatus` enum values; click sets `Scene.Status`.
- `Open in split pane`: add `IsSplitOpen`, `SecondaryScene` to `MainWindowViewModel`; render right pane in `MainWindow.axaml`; new command opens scene in secondary pane.
- Locales `explorer.duplicate`, `explorer.status`, `explorer.openInSplitPane`.
- Apply across both scene and chapter context menus where applicable.

### A4. Welcome screen

**A4.1 — Recent card `Remove from list`** _(was #14)_
- Add `ContextMenu` to recent-project card `Border` in [WelcomeView.axaml:189-230](../Novalist.Desktop/Views/WelcomeView.axaml#L189-L230).
- Add `RemoveRecentCommand(RecentProject project)` on `WelcomeViewModel` — filters and persists `AppSettings.RecentProjects`.
- Locale `welcome.removeFromList`.

### A5. Codex Hub

**A5.1 — `Manage types` button** _(was #15, part of #43)_
- Add header button in [CodexHubView.axaml](../Novalist.Desktop/Views/CodexHubView.axaml).
- Bind to `OpenEntityTypeManagerCommand` on `CodexHubViewModel` opening existing `EntityTypeManagerDialog`.
- Locale `codexHub.manageTypes`.

**A5.2 — `Templates` button** _(was #16, part of #43)_
- Add header button in `CodexHubView.axaml`.
- Bind to `OpenTemplateEditorCommand` opening existing `TemplateEditorDialog`.
- Locale `codexHub.templates`.

**A5.3 — Sort options** _(was #17, part of #43)_
- Add `CodexSortMode` enum (`Name`, `RecentlyModified`).
- Add `SortMode` property on `CodexHubViewModel`.
- Add `ComboBox` in view header.
- Apply via `ICollectionView.SortDescriptions`.
- Locales `codexHub.sortName`, `codexHub.sortRecent`.

### A6. Timeline

**A6.1 — Toolbar: Previous / Next / Today / Jump-to-date** _(was #20)_
- Add 4 buttons to [TimelineView.axaml](../Novalist.Desktop/Views/TimelineView.axaml) toolbar.
- Commands on `TimelineViewModel`:
  - `PanPreviousCommand` (shift visible range by one unit at current zoom)
  - `PanNextCommand`
  - `ScrollToTodayCommand`
  - `JumpToDateCommand` opening date-picker flyout.
- Locales `timeline.prev`, `timeline.next`, `timeline.today`, `timeline.jumpTo`.

**A6.2 — Categories `Location` and `Other`** _(was #22)_
- Extend `TimelineCategory` enum in [TimelineData.cs:14-16](../Novalist.Domain/TimelineData.cs#L14-L16) with `Location`, `Other`.
- Add filter combo entries in `TimelineView.axaml`.
- Locales `timeline.catLocation`, `timeline.catOther`.
- No migration needed (existing events stay on current category).

### A7. Calendar

**A7.1 — Drag-to-reschedule scene block** _(was #23)_
- Set `DragDrop.AllowDrop="True"` on day cells in [CalendarView.axaml](../Novalist.Desktop/Views/CalendarView.axaml).
- Add drag source on scene block via `DragDrop.DoDragDrop`.
- Drop handler invokes `RescheduleSceneCommand(SceneId, DateTime)` on `CalendarViewModel`.
- Persist `Scene.Date`, re-render calendar.

### A8. Relationships graph

**A8.1 — Toolbar (Search / Group / Role / Hide-world-bible)** _(was #24)_
- Add toolbar `StackPanel` above Canvas in [RelationshipsGraphView.axaml](../Novalist.Desktop/Views/RelationshipsGraphView.axaml).
- Controls:
  - `TextBox` → `SearchQuery` (highlight matching nodes).
  - `ComboBox` → `FilterGroup` (distinct `Character.Group` values).
  - `ComboBox` → `FilterRole`.
  - `ToggleButton` → `HideWorldBibleCharacters` (filters `IsWorldBibleOnly`).
- Add 4 properties to `RelationshipsGraphViewModel.cs`; re-run layout when changed.
- Locales `graph.search`, `graph.filterGroup`, `graph.filterRole`, `graph.hideWorldBible`.

### A9. Research view

**A9.1 — Search box** _(was #26)_
- Add `SearchText` to `ResearchViewModel`.
- Add `TextBox` above list; filter title / tags / notes (contains).
- Locale `research.search`.

**A9.2 — Tag editor + tag filter** _(was #27, #45)_
- Detail pane: token-style tag editor (chip list + add-tag input) bound to `ResearchItem.Tags`.
- Toolbar: chip-filter row bound to `FilterTags : ObservableCollection<string>` (multi-tag AND filter).
- Locales `research.tags`, `research.addTag`.

**A9.3 — PDF viewer + image preview + file metadata + Reveal-in-file-manager** _(was #28)_
- `RevealInExplorerCommand` invoking `Process.Start("explorer", $"/select,{path}")` (Windows) / `open -R` (macOS) / `xdg-open` (Linux fallback to containing dir).
- Image preview: when extension matches `.png/.jpg/.jpeg/.gif/.webp` render `<Image Source>` instead of TextBox.
- PDF viewer: embed PDF control (evaluate `PdfiumViewer` vs WebView2 with built-in pdf viewer) when extension `.pdf`.
- File metadata panel: size / created / modified / full path from `FileInfo`.
- Locales `research.reveal`, `research.metadata`.

### A10. Image Gallery

**A10.1 — Per-image `Open externally`, `Copy as markdown`** _(was #29)_
- `OpenExternallyCommand(string path)` using `Process.Start(new ProcessStartInfo(path){UseShellExecute=true})` on `ImageGalleryViewModel`.
- `CopyAsMarkdownCommand` putting `![{name}]({relPath})` on Avalonia clipboard.
- Two context-menu entries.
- Locales `imageGallery.openExternally`, `imageGallery.copyMarkdown`.

### A11. Dashboard

**A11.1 — Author line in header** _(was #35, ties to #41)_
- Add `Author : string` to `ProjectSettings.cs`.
- Add `Author` property on `DashboardViewModel.cs` projecting from project.
- Add `<TextBlock Text="{Binding Author}">` to [DashboardView.axaml](../Novalist.Desktop/Views/DashboardView.axaml) header.
- Settings entry: see A13.1.

**A11.2 — Recent activity timeline** _(was #37)_
- New `RecentActivityService` logging edits (`sceneId`, `timestamp`, `type=Edit/Create/Delete`) to a ring buffer persisted at `.novalist/activity.json`.
- Expose `RecentActivity : ObservableCollection<ActivityItem>` on `DashboardViewModel`.
- Render as list in `DashboardView.axaml`.
- Locale `dashboard.recentActivity`.

### A12. Add-image dialog

**A12.1 — `From clipboard`, `From URL` sources** _(was #39)_
- Extend `AddImageSourceChoice` enum with `Clipboard`, `Url`.
- Add two buttons to `AddImageSourceDialog.axaml`.
- Clipboard handler: `await TopLevel.Clipboard.GetDataAsync("image/png")`, write bytes to gallery folder.
- URL handler: prompt for URL, `HttpClient.GetByteArrayAsync`, validate content-type starts with `image/`, save to gallery.
- Locales `addImage.clipboard`, `addImage.url`.

### A13. Settings

**A13.1 — Per-project Author name** _(was #41, depends on A11.1)_
- After `ProjectSettings.Author` exists, bind in Settings → Project section.
- Export form prefills from `ProjectSettings.Author` and writes back on change.

### A14. Project Overview popup

**A14.1 — Chapter progress + readability list** _(was #49)_
- In overview popup add `ItemsControl` over chapters.
- Per-chapter row: progress bar (`WordCount / TargetWordCount`), avg `FleschReadingEase`, click → navigate to first scene.
- Locales `overview.chapterProgress`, `overview.readability`.

---

## B) Manual edits — remove or correct stale claims

For each entry below: open the listed page, locate the offending passage, and either delete or rewrite to match actual implementation. Use plain prose, no emoji, no SVG glyphs in docs.

**B1. `05-editor.md` + `26-hotkeys.md` — Strikethrough hotkey** _(was #1)_
- Remove strikethrough from formatting list and from hotkey table. Editor supports Bold / Italic / Underline only.

**B2. `04-chapters-and-scenes.md` — `Ctrl+Alt+S` snapshot hotkey** _(was #2)_
- Drop the "(default binding)" phrase. State: snapshots are taken via Edit → Take Snapshot or scene right-click → Take Snapshot.

**B3. `05-editor.md` — Indentation and lists section** _(was #5)_
- Remove the "Indentation and lists" subsection (Tab/Shift+Tab indent/outdent, bullet/numbered lists).

**B4. `22-context-sidebar.md` — Counts** _(was #6)_
- Rewrite Counts subsection to match actual VM output: word count, sentence count, average sentence length. Remove paragraph count and dialogue percentage claims.

**B5. `22-context-sidebar.md` — Detected-entity Ignore action** _(was #8)_
- Remove the "False positives can be hidden via the **Ignore** action on a row" sentence.

**B6. `04-chapters-and-scenes.md` — Collapse all / Expand all** _(was #10)_
- Remove collapse-all / expand-all references from Explorer toolbar description.

**B7. `08-plot-grid.md` — Scene `Plotlines →` Explorer submenu** _(was #13)_
- Remove the "edit a scene's plotline memberships from the scene's right-click menu in the Explorer (**Plotlines →** …)" sentence. Plot Grid view remains the way to manage plotline membership.

**B8. `07-templates.md` + `23-settings.md` — Set active / Duplicate** _(was #18, #48)_
- Drop **Set active** and **Duplicate** from per-template action lists. Per-type templates have Add / Edit / Delete only.

**B9. `10-manuscript.md` — Outliner columns** _(was #19)_
- Rewrite Outliner column list to match implementation: Chapter, Title, Synopsis, POV, Word Count. Remove Status / Date / Date-range / sortable headers / search-filter claims.

**B10. `12-timeline.md` — Week zoom** _(was #21)_
- Drop Week zoom from list. Zoom cycles Day → Month → Year.

**B11. `14-relationships.md` — Double-click pin/unpin** _(was #25)_
- Remove the hedged "(some builds)" double-click pin/unpin sentence entirely.

**B12. `21-find-replace.md` — `Selection` scope** _(was #30, #46)_
- Remove `Selection` from documented scopes. Add `CurrentChapter` to the list (actual implementation). Scopes documented: Current Scene, Current Chapter, Active Book, Project.

**B13. `21-find-replace.md` — Replace (single) / Skip buttons** _(was #31)_
- Remove Replace-one and Skip references. Dialog has Find + Replace All only.

**B14. `21-find-replace.md` — Match counter "12 of 42"** _(was #33)_
- Remove the match-counter sentence.

**B15. `08-plot-grid.md` — Plotline color / description / drag-reorder** _(was #34, #44)_
- Rewrite row context-menu description to Rename / Delete only. Drop change-color, edit-description, drag-reorder.

**B16. `11-dashboard.md` — `Last edited` timestamp** _(was #36)_
- Remove the Last-edited line from header description.

**B17. `11-dashboard.md` — Status segment click → filter Manuscript** _(was #38)_
- Remove the "each segment is clickable: clicking a status filters the Manuscript view to that status" sentence.

**B18. `23-settings.md` + `05-editor.md` — Page format A4 / US Letter** _(was #40)_
- Rewrite available formats to actual list: US Trade (6×9), Digest (5.5×8.5), A5, Mass Market, Custom. Remove A4 and US Letter.

**B19. `23-settings.md` — Per-project autosave interval** _(was #42)_
- Remove the per-project autosave-interval bullet. Note autosave runs at a fixed cadence (or drop the topic entirely).

**B20. `26-hotkeys.md` — Sweep aligned with code changes**
- Remove rows for `Ctrl+Shift+X` strikethrough and `Ctrl+Alt+S` snapshot (per B1/B2).
- Verify all remaining rows match actual `HotkeyDescriptor` defaults after A1.1 lands.

---

## C) Verification needed first — confirm state, then route

Each item: read the file/UI listed, decide if claim holds. If yes, do nothing. If no, either schedule a code change or a manual edit. Keep notes inline.

**C1. Match line-number-within-scene in Find results** _(was #32)_
- Source check: `FindReplace.cs` and `FindReplaceService.cs` for `LineNumber` field on match record.
- If absent: choose between A) add line-number compute (small) or B) remove from manual.

**C2. Snapshot restore creates pre-restore snapshot** _(was #47)_
- Trace `SnapshotService.RestoreSnapshot(...)` for an automatic `TakeSnapshot` call before applying restore.
- If absent: choose between A) add auto-snapshot before restore or B) remove from `17-snapshots.md`.

**C3. Manuscript inline editing** _(was #50)_
- Inspect `manuscript.html` / `ManuscriptViewModel` for `contenteditable` or edit-input bridge.
- If absent: A) implement edit-on-input bridge or B) remove the "edit inline in Manuscript mode" sentence.

**C4. Echo-phrase click → Find across book** _(was #51)_
- Check `DashboardView.axaml` for click binding on echo-phrase row.
- If absent: A) wrap in clickable element invoking `FindAcrossBookCommand(phrase)` or B) remove from manual.

**C5. Calendar Today / Jump-to-date wired to UI** _(was #52)_
- Check `CalendarView.axaml` for buttons bound to existing `TodayCommand` / `JumpToDateCommand`.
- If absent: A) wire buttons (cheap — methods exist) or B) remove from manual.

**C6. `Ctrl+]` / `Ctrl+[` Next/Previous scene on US keyboard** _(was #54)_
- Manual test on Windows US layout (and DE for parity).
- If broken: switch to explicit `Key.OemOpenBrackets` + `KeyModifiers.Control` and re-test.

**C7. Smart Lists color swatch + result count** _(was #55)_
- Read `SmartListsPanelView.axaml`.
- If absent: add swatch `Border` + count `TextBlock` to row template.

**C8. Manuscript "Filter to one plotline"** _(was #56)_
- Search `ManuscriptViewModel.cs` for plotline filter.
- Manual already hedges; if not implemented, A) implement or B) drop hedged sentence.

**C9. Manuscript word-count / reading-time honoring `FilterStatus`** _(was #57)_
- Inspect Manuscript toolbar bindings — do they reflect filtered collection?
- If not: bind `VisibleWordCount` / `VisibleReadingTime` computed from filtered set.

**C10. Welcome card sort order** _(was #58)_
- Inspect `WelcomeViewModel` recent-projects ordering.
- If not sorted by `LastOpenedAt` desc: fix.

**C11. Character `Add override` flow** _(was #59)_
- Trace `EntityEditorView.axaml` + `EntityEditorViewModel.cs` for an add-override affordance.
- If absent: A) add `AddOverrideCommand(scope, scopeTargetId)` + button "Add override for current chapter/act/scene" or B) trim manual claim.

**C12. Timeline `Apply structure...` flyout + `Export outline` button — reverse gap** _(was #60)_
- Both controls exist; manual lacks documentation.
- Action: add documentation to `12-timeline.md` describing Apply Structure (story-structure templates populating timeline events) and Export Outline (markdown/text outline export). No code change.

**C13. Snapshot folder configurable per book** _(was #61)_
- Search `BookData` for `SnapshotFolder` property.
- If absent: A) add `SnapshotFolder : string` (default `Snapshots`) to `BookData` + Settings exposure + `SnapshotService` use, or B) remove "configurable" from manual.

---

## Suggested execution order

1. **Quick wins (small, isolated):** A2.1 (Somber), A4.1 (Remove recent), A5.1–A5.3 (Codex Hub buttons + sort), A10.1 (image gallery actions), A1.2 (Edit menu items), A6.2 (timeline categories).
2. **Verification pass:** Walk C1–C13, classify each as code/docs/done.
3. **Manual edit pass (B1–B20):** One PR sweeping all docs corrections — fast, low-risk, eliminates noise.
4. **Medium code work:** A3.1 (Explorer search), A3.2 (multi-select), A9.1 (research search), A6.1 (timeline toolbar), A8.1 (graph toolbar), A11.1+A13.1 (Author end-to-end), A12.1 (clipboard/URL image), A14.1 (overview popup).
5. **Larger code work:** A1.1 (hotkey context routing), A3.3 (split pane, etc.), A7.1 (calendar drag), A9.2–A9.3 (research tags + PDF/image preview), A11.2 (activity service).
