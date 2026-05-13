# Manual-vs-Implementation Gap Audit

Generated 2026-05-12. Compares `docs/manual/` claims against actual code.
Each entry numbered; **Fix** describes work needed to make implementation match manual (add/build, not delete from docs).

## Confirmed gaps (documented, not implemented)

### Hotkeys & menus

**1. Strikethrough `Ctrl+Shift+X`** — _05-editor.md, 26-hotkeys.md_
-> Remove incorrect description in manual.
**2. `Ctrl+Alt+S` snapshot hotkey** — _04-chapters-and-scenes.md_
- No descriptor registered.
- **Fix:** Add `HotkeyDescriptor("app.snapshot.take", "Ctrl+Alt+S")` in `MainWindowViewModel` hotkey list, route to existing `TakeSnapshotCommand` on `SceneViewModel` (current scene). Add locale `hotkey.snapshot.take`.
-> Remove incorrect description in manual.

**3. `Ctrl+Shift+M` collision (Add Comment vs Create chapter)** — _26-hotkeys.md_
- Both `app.editor.addComment` and `app.chapter.create` share gesture.
- **Fix:** Add focus-context predicate so `addComment` fires only when editor focused (`ActiveContentView == "Scene"`), `chapter.create` when Explorer focused. Build minimal `IFocusContext` layer in `HotkeyDispatcher` that reads `FocusManager.GetFocusedElement()` ancestor type before invoking `CanExecute`.
-> Do this

**4. Edit → Add Comment / Add Footnote menu items** — _05-editor.md:145, 22-context-sidebar.md:78,129_
- Edit menu in [MainWindow.axaml:23-58](../Novalist.Desktop/MainWindow.axaml#L23-L58) lacks them.
- **Fix:** Create `AddCommentCommand` / `AddFootnoteCommand` on `MainWindowViewModel` delegating to active `SceneViewModel`. Add two `<MenuItem>` rows under Edit. Add locale `menu.addComment`, `menu.addFootnote`.
  -> Do this

**5. Indent/Outdent (Tab / Shift+Tab) and Bullet/Numbered list** — _05-editor.md "Indentation and lists"_
- `editor.html` lacks the actions.
- **Fix:** In `editor.html` add keydown handler for `Tab`/`Shift+Tab` calling `document.execCommand('indent'/'outdent')`. Add ribbon buttons + JS handlers for `insertUnorderedList`, `insertOrderedList`. Wire ribbon entries in `MainWindow.axaml` ribbon section. Locales: `ribbon.indent`, `ribbon.outdent`, `ribbon.bulletList`, `ribbon.numberedList`.
-> Remove incorrect description in manual.

### Editor / Context sidebar

**6. Paragraph count + dialogue percentage** — _22-context-sidebar.md "Counts"_
- VM exposes sentences/words only.
- **Fix:** In `ContextSidebarViewModel.cs` add `ParagraphCount` (split scene text on `\n\n+`) and `DialoguePercent` (chars within balanced `"…"` and `'…'` / total chars × 100). Add display rows in `ContextSidebarView.axaml`. Locales `context.paragraphs`, `context.dialoguePercent`.
-> Remove incorrect description in manual and describe correct.

**7. Emotional tone `Somber`** — _22-context-sidebar.md_
- 15 profiles present; `somber` absent.
- **Fix:** Add `somber` entry to `EmotionProfile` list at [ContextSidebarViewModel.cs:95-112](../Novalist.Desktop/ViewModels/ContextSidebarViewModel.cs#L95-L112) with keyword set (`grim`, `mournful`, `bleak`, `subdued`, `solemn`, `heavy`). Add locale `context.tone.somber`.
-> Do this

**8. Detected-entity `Ignore` action** — _22-context-sidebar.md_
- No mechanism.
- **Fix:** Add `IgnoredEntities : List<string>` to `ProjectSettings`. Add `IgnoreEntityCommand(string token)` on `ContextSidebarViewModel`, plus per-row context menu in `ContextSidebarView.axaml`. Filter detected list against ignored set on each refresh. Add locale `context.ignore`.
-> Remove incorrect description in manual.

### Explorer (Chapters tab)

**9. Explorer search box** — _04-chapters-and-scenes.md_
- [ExplorerView.axaml](../Novalist.Desktop/Views/ExplorerView.axaml) has no TextBox.
- **Fix:** Add `SearchText : string` to `ExplorerViewModel`. Insert `<TextBox Watermark="{loc:Loc explorer.search}">` above tree. Apply `ICollectionView` filter on chapter/scene titles (case-insensitive contains). Hide empty chapters when no children match.
-> Do this

**10. Collapse all / Expand all** — _04-chapters-and-scenes.md_
- **Fix:** Add `CollapseAllCommand` / `ExpandAllCommand` on `ExplorerViewModel` iterating chapter VMs setting `IsExpanded`. Add two toolbar buttons next to `+Chapter / +Scene`. Locales `explorer.collapseAll`, `explorer.expandAll`.
-> Remove incorrect description in manual.

**11. Multi-select + bulk operations** — _04-chapters-and-scenes.md_
- **Fix:** Switch `TreeView`/`ListBox` to `SelectionMode="Multiple"`. Add `SelectedScenes : ObservableCollection<SceneViewModel>` to VM. Refactor delete/move-to-chapter/set-status to accept collection. Context menu items operate on full selection when count > 1.
-> Do this

**12. Right-click `Duplicate`, `Set status →`, `Open in split pane`** — _04-chapters-and-scenes.md, 02-interface-overview.md_
- Scene menu [ExplorerView.axaml:181-201](../Novalist.Desktop/Views/ExplorerView.axaml#L181-L201) lacks them.
- **Fix:**
  - `DuplicateSceneCommand` / `DuplicateChapterCommand` in `ExplorerViewModel`: deep-clone `SceneData` via `JsonSerializer`, generate new id, insert after source.
  - `Set status →` submenu binding to `StoryStatus` enum values; on click set `Scene.Status`.
  - `Open in split pane` requires split editor — add `IsSplitOpen`, `SecondaryScene` to `MainWindowViewModel`, render right pane in `MainWindow.axaml`. Command opens scene in secondary pane.
  - Locales `explorer.duplicate`, `explorer.status`, `explorer.openInSplitPane`.
-> Do this

**13. Scene `Plotlines →` submenu** — _08-plot-grid.md_
- Not in Explorer.
- **Fix:** Add `Plotlines` `MenuItem` on scene context menu, `ItemsSource` bound to all `PlotlineData` for the book. Each child is a checkable item toggling membership in `Scene.PlotlineIds`. Locale `explorer.plotlines`.
-> Remove incorrect description in manual.

### Welcome screen

**14. Recent card `Remove from list`** — _01-getting-started.md, 03-projects-and-books.md_
- [WelcomeView.axaml:189-230](../Novalist.Desktop/Views/WelcomeView.axaml#L189-L230) recent card has no ContextMenu.
- **Fix:** Add `ContextMenu` to recent-project card root `Border`. Add `RemoveRecentCommand(RecentProject project)` on `WelcomeViewModel` that filters `AppSettings.RecentProjects` and persists. Locale `welcome.removeFromList`.
-> Do this

### Codex Hub

**15. `Manage types` button** — _06-codex.md_
- **Fix:** Add `<Button>` to [CodexHubView.axaml](../Novalist.Desktop/Views/CodexHubView.axaml) header bound to new `OpenEntityTypeManagerCommand` on `CodexHubViewModel` opening existing `EntityTypeManagerDialog`. Locale `codexHub.manageTypes`.
-> Do this

**16. `Templates` button** — _07-templates.md_
- **Fix:** Add `<Button>` to `CodexHubView.axaml` header bound to `OpenTemplateEditorCommand` opening existing `TemplateEditorDialog`. Locale `codexHub.templates`.
-> Do this

**17. Sort options** — _06-codex.md_
- **Fix:** Add `CodexSortMode` enum (`Name`, `RecentlyModified`). Property `SortMode` on `CodexHubViewModel`. Add `ComboBox` in view. Apply via `ICollectionView.SortDescriptions`. Locales `codexHub.sortName`, `codexHub.sortRecent`.
-> Do this

### Templates

**18. Set active / Duplicate template** — _07-templates.md, 23-settings.md_
- Settings shows Add/Edit/Delete only.
- **Fix:** Add `IsActive : bool` (or `ActiveTemplateId` on `EntityType`) to `EntityTemplate`. Add `SetActiveTemplateCommand` and `DuplicateTemplateCommand` on `TemplateSettingsViewModel`. Show badge "Active" next to active row in [SettingsView.axaml:475+](../Novalist.Desktop/Views/SettingsView.axaml#L475). Locales `templates.setActive`, `templates.duplicate`, `templates.active`.
-> Remove incorrect description in manual.

### Manuscript view (Outliner)

**19. Status / Date / Date-range columns + sortable headers + search/filter** — _10-manuscript.md_
- [ManuscriptView.axaml:178](../Novalist.Desktop/Views/ManuscriptView.axaml#L178) has 5 columns only.
- **Fix:**
  - Add `DataGridTemplateColumn` for Status (StatusPill template).
  - Add `DataGridTextColumn` for Date (binding `Scene.Date`).
  - Add `DataGridTextColumn` for Date range (computed `Scene.DateRangeDisplay`).
  - Set `CanUserSortColumns="True"` and `SortMemberPath` on each column.
  - Add search `TextBox` filtering `ObservableCollection` via `ICollectionView`.
  - Locales `manuscript.col.status`, `manuscript.col.date`, `manuscript.col.dateRange`.
-> Remove incorrect description in manual.

### Timeline

**20. Previous / Next / Today / Jump-to-date toolbar** — _12-timeline.md_
- Absent in [TimelineView.axaml](../Novalist.Desktop/Views/TimelineView.axaml).
- **Fix:** Add four buttons to timeline toolbar. Add commands on `TimelineViewModel`: `PanPrevious()` (shift visible range by one unit at current zoom), `PanNext()`, `ScrollToToday()`, `JumpToDate(DateTime)` opening a date-picker flyout. Locales `timeline.prev`, `timeline.next`, `timeline.today`, `timeline.jumpTo`.
-> Do this

**21. Week zoom** — _12-timeline.md_
- [TimelineViewModel.cs:325](../Novalist.Desktop/ViewModels/TimelineViewModel.cs#L325) cycles year/month/day.
- **Fix:** Add `Week` to zoom rotation. Implement week-tick rendering in timeline canvas (7-day grouping with ISO week numbers). Add locale `timeline.zoomWeek`.
-> Remove incorrect description in manual.

**22. Categories `Location` and `Other`** — _12-timeline.md_
- [TimelineData.cs:14-16](../Novalist.Domain/TimelineData.cs#L14-L16) ships plot/character/world only.
- **Fix:** Extend `TimelineCategory` enum with `Location`, `Other`. Add filter combo entries in `TimelineView.axaml`. Locales `timeline.catLocation`, `timeline.catOther`. Migration: existing events default to existing category (no migration needed).
-> Do this

### Calendar

**23. Drag-to-reschedule scene block** — _13-calendar.md_
- **Fix:** Wire `DragDrop.AllowDrop="True"` on calendar day cells. Add drag source on scene block in `CalendarView.axaml` with `DragDrop.DoDragDrop`. Drop handler invokes `RescheduleSceneCommand(SceneId, DateTime)` on `CalendarViewModel` updating `Scene.Date`, persists project, re-renders calendar.
-> Do this

### Relationships graph

**24. Toolbar (Search / Group / Role / Hide-world-bible toggle)** — _14-relationships.md_
- [RelationshipsGraphView.axaml](../Novalist.Desktop/Views/RelationshipsGraphView.axaml) is title + Canvas only.
- **Fix:** Add toolbar StackPanel above Canvas with:
  - `TextBox` bound to `SearchQuery` (highlight nodes matching name).
  - `ComboBox` bound to `FilterGroup` populated from distinct `Character.Group` values.
  - `ComboBox` bound to `FilterRole`.
  - `ToggleButton` bound to `HideWorldBibleCharacters` (filters out characters where `IsWorldBibleOnly == true`).
  - Add the four properties to `RelationshipsGraphViewModel.cs` and re-run layout when changed.
  - Locales: `graph.search`, `graph.filterGroup`, `graph.filterRole`, `graph.hideWorldBible`.
-> Do this

**25. Double-click pin/unpin node** — _14-relationships.md_
- **Fix:** Subscribe `DoubleTapped` on node visual; call `TogglePinNode(nodeId)` on VM that flips `Node.IsPinned`. Pinned nodes excluded from force-layout step.
-> Remove incorrect description in manual.

### Research view

**26. Search box** — _15-research.md_
- No filter TextBox.
- **Fix:** Add `SearchText` to `ResearchViewModel`. Insert `TextBox` above list; filter by title/tags/notes contains. Locale `research.search`.
-> Do this

**27. Tag editor + tag filter** — _15-research.md_
- `ResearchItem.Tags` model exists; no UI.
- **Fix:** Add token-style tag editor in detail pane (chip list + add-tag input). Add `FilterTags : ObservableCollection<string>` and chip-filter row in toolbar; multi-tag AND filter. Locales `research.tags`, `research.addTag`.
-> Do this

**28. PDF viewer + image preview + file metadata + Reveal-in-file-manager** — _15-research.md_
- Detail pane is TextBox + Open externally + Delete + Save.
- **Fix:**
  - Add `RevealInExplorerCommand` invoking `Process.Start("explorer", $"/select,{path}")` (Windows) / `open -R` (macOS) / `xdg-open` (Linux).
  - Image preview: when item extension is `.png/.jpg/.jpeg/.gif/.webp`, render `<Image Source>` instead of TextBox.
  - PDF viewer: embed `PdfiumViewer` or `PDFsharp` based control (or WebView2 with `chrome://pdf-viewer/`). Render when extension `.pdf`.
  - File metadata panel: `FileInfo` block showing size, created, modified, full path.
  - Locales `research.reveal`, `research.metadata`.
-> Do this

### Image Gallery

**29. Per-image `Open externally`, `Copy as markdown`** — _19-image-gallery.md_
- **Fix:** Add `OpenExternallyCommand(string path)` using `Process.Start(new ProcessStartInfo(path){UseShellExecute=true})` on `ImageGalleryViewModel`. Add `CopyAsMarkdownCommand` putting `![{name}]({relPath})` on Avalonia clipboard. Two new context-menu entries. Locales `imageGallery.openExternally`, `imageGallery.copyMarkdown`.
-> Do this

### Find & Replace

**30. Scope `Selection`** — _21-find-replace.md_
- [FindReplace.cs:3-13](../Novalist.Domain/FindReplace.cs#L3-L13) has `CurrentScene/CurrentChapter/ActiveBook/Project`.
- **Fix:** Add `Selection` member to `FindScope`. Capture editor selection range (offsets) when dialog opens. Constrain match search to that range in `FindReplaceService.cs`. Add ComboBox entry; locale `find.scope.selection`. (Also document existing `CurrentChapter` in manual.)
-> Remove incorrect description in manual.

**31. Replace (single) and Skip buttons** — _21-find-replace.md_
- [FindReplaceDialog.axaml:41-42](../Novalist.Desktop/Views/FindReplaceDialog.axaml#L41-L42) has Find + Replace-all only.
- **Fix:** Add `CurrentMatchIndex` on `FindReplaceViewModel`. Add `ReplaceCurrentCommand` (replace current match, advance), `SkipCurrentCommand` (advance without replace). Two buttons in dialog. Locales `find.replaceOne`, `find.skip`.
-> Remove incorrect description in manual.

**32. Match line-number-within-scene** — _21-find-replace.md_
- **Fix:** Add `LineNumber` to match record in `FindReplace.cs`. In `FindReplaceService`, compute from scene text by counting `\n` up to match offset + 1. Bind in result list `DataTemplate`.

**33. Match counter "12 of 42"** — _21-find-replace.md_
- **Fix:** Bind label `{Binding CurrentMatchIndex+1} / {Binding TotalMatches}` in `FindReplaceDialog.axaml`. Add formatting via `MultiBinding` or VM-computed string. Locale `find.counter`.
-> Remove incorrect description in manual.

### Plot Grid

**34. Plotline `Change color`, `Edit description`, drag-reorder** — _08-plot-grid.md_
- Row menu [PlotGridView.axaml:87-95](../Novalist.Desktop/Views/PlotGridView.axaml#L87-L95) only Rename/Delete.
- **Fix:**
  - Add `ChangeColorCommand` opening color picker (Avalonia `ColorPicker`); persist to `Plotline.Color`.
  - Add `EditDescriptionCommand` opening text dialog; persist to `Plotline.Description`.
  - Drag-reorder: enable `DragDrop` on row, update `Plotline.Order` on drop, resort `ObservableCollection`.
  - Locales `plotGrid.changeColor`, `plotGrid.editDescription`.
-> Remove incorrect description in manual.

### Dashboard

**35. Author line in header** — _11-dashboard.md_
- No `Author` on VM or `ProjectSettings`.
- **Fix:** Add `Author : string` to `ProjectSettings.cs`. Add `Author` property on `DashboardViewModel.cs` projecting from project. Add `<TextBlock Text="{Binding Author}">` to [DashboardView.axaml](../Novalist.Desktop/Views/DashboardView.axaml) header. Settings page entry to edit (covers #41 too).
-> Do this

**36. `Last edited` timestamp** — _11-dashboard.md_
- **Fix:** Track `LastEditedAt : DateTime` in `ProjectMetadata`, updated by autosave / explicit save. Expose on `DashboardViewModel`. Render with relative-time formatter ("2 hours ago"). Locale `dashboard.lastEdited`.
-> Remove incorrect description in manual.

**37. Recent activity timeline** — _11-dashboard.md_
- **Fix:** Add `RecentActivityService` logging edits (sceneId, timestamp, type=Edit/Create/Delete) to ring-buffer persisted under `.novalist/activity.json`. Expose `RecentActivity : ObservableCollection<ActivityItem>` on `DashboardViewModel`. Render as list in `DashboardView.axaml`. Locale `dashboard.recentActivity`.
-> Do this

**38. Status segment click → filter Manuscript** — _11-dashboard.md_
- **Fix:** Add `PointerPressed` handler on each status-bar segment in `DashboardView.axaml` invoking `MainWindowViewModel.OpenManuscriptWithStatusFilterCommand(StoryStatus s)` that sets `ManuscriptViewModel.FilterStatus` and switches `ActiveContentView` to Manuscript.
-> Remove incorrect description in manual.

### Add-image dialog

**39. `From clipboard`, `From URL` sources** — _06-codex.md, 19-image-gallery.md_
- Enum has `Library/Import` only.
- **Fix:** Extend `AddImageSourceChoice` with `Clipboard`, `Url`. Add two buttons to `AddImageSourceDialog.axaml`.
  - Clipboard: `await TopLevel.Clipboard.GetDataAsync("image/png")`, write bytes to gallery folder.
  - URL: prompt for URL, `HttpClient.GetByteArrayAsync`, save to gallery (validate content-type starts with `image/`).
  - Locales `addImage.clipboard`, `addImage.url`.
-> Do this

### Settings

**40. Book page format `A4`, `US Letter`** — _23-settings.md, 05-editor.md_
- [BookWidthCalculator.cs:82](../Novalist.Domain/BookWidthCalculator.cs#L82) lists 5 sizes.
- **Fix:** Add `A4` (210×297mm) and `USLetter` (8.5×11in) to `BookPageFormat` enum and switch case in `BookWidthCalculator`. Add ComboBox entries in Settings. Locales `settings.format.a4`, `settings.format.usLetter`.
-> Remove incorrect description in manual.

**41. Per-project Author name** — _23-settings.md_
- **Fix:** (See #35.) After adding `ProjectSettings.Author`, bind in Settings → Project section. In export form prefill from `ProjectSettings.Author` and write back on change.
-> Do this

**42. Per-project autosave interval** — _23-settings.md_
- **Fix:** Add `AutosaveIntervalSeconds : int` (default 30) to `ProjectSettings`. Add `NumericUpDown` in Settings → Editor. `AutosaveService` reads value on project load and on settings change. Locale `settings.autosaveInterval`.
-> Remove incorrect description in manual.

## Partial / stubbed implementations

**43. Codex Hub partial** — _06-codex.md_ — see #15, #16, #17.

**44. Plot Grid metadata partial** — _08-plot-grid.md_ — model has Color/Description/Order; UI exposes none. See #34.

**45. Research tags UI** — _15-research.md_ — model only. See #27.

**46. Find/Replace scopes mismatch** — _21-find-replace.md_ — see #30; also update manual to note `CurrentChapter`.

**47. Snapshot pre-restore snapshot** — _17-snapshots.md_
- **Fix:** In `SnapshotService.RestoreSnapshot(...)`, before applying restore call `TakeSnapshot(label = "Pre-restore auto")`. Confirm flow surfaced in [SnapshotsDialog.axaml.cs:69](../Novalist.Desktop/Views/SnapshotsDialog.axaml.cs#L69).

**48. Settings → Templates Set active / Duplicate** — _23-settings.md_ — see #18.

**49. Project Overview popup completeness** — _11-dashboard.md, 02-interface-overview.md_
- Popup exists ([MainWindow.axaml:791](../Novalist.Desktop/MainWindow.axaml#L791)); detailed chapter list with mini-bar/readability/drill-down not verified.
- **Fix:** In overview popup add `ItemsControl` over chapters showing per-chapter progress bar (`WordCount / TargetWordCount`), avg `FleschReadingEase`, click handler navigating to scene. Locales `overview.chapterProgress`, `overview.readability`.
-> Do this

**50. Manuscript inline editing** — _10-manuscript.md_
- Composite WebView at [ManuscriptViewModel.cs:43](../Novalist.Desktop/ViewModels/ManuscriptViewModel.cs#L43); inline-edit unverified.
- **Fix:** In `manuscript.html` JS, mark each scene `<section contenteditable="true">`. On `input` event, debounce 500ms and post change back via WebView bridge with `{sceneId, newText}`. Apply via `SceneViewModel.UpdateText`. Add visual save indicator.

**51. Echo-phrase click → Find across book** — _11-dashboard.md_
- `EchoPhrase` at [DashboardViewModel.cs:448](../Novalist.Desktop/ViewModels/DashboardViewModel.cs#L448).
- **Fix:** Wrap echo-phrase TextBlock in `Button.Classes="link"`. Bind `Command` to `FindAcrossBookCommand(phrase)` opening Find&Replace dialog prefilled with phrase and `Scope=ActiveBook`.

**52. Calendar Today / Jump-to-date wired** — _13-calendar.md_
- VM methods exist ([CalendarViewModel.cs:86,98](../Novalist.Desktop/ViewModels/CalendarViewModel.cs#L86-L98)); UI binding unverified.
- **Fix:** Add two toolbar buttons in `CalendarView.axaml` bound to `TodayCommand` and `JumpToDateCommand` (flyout with `DatePicker`). Locales `calendar.today`, `calendar.jumpTo`.

**53. Hotkey context routing** — _26-hotkeys.md_
- Only `CanExecute` on `ActiveContentView == "Scene"` exists.
- **Fix:** Build `HotkeyContextResolver` reading focused element ancestor chain (`Editor` / `Explorer` / `Other`). Each `HotkeyDescriptor` gets optional `ContextScope` enum. Dispatcher checks scope before invoking. Solves #3 too. Update Ctrl+B and Ctrl+Shift+P to use `ContextScope`.

## Unclear / needs human verification (verify, then either confirm or schedule fix)

**54. `Ctrl+]` / `Ctrl+[` Next/Previous scene on US keyboard** — _26-hotkeys.md_
- Bound to `OemCloseBrackets`/`OemOpenBrackets` ([MainWindowViewModel.cs:692-693](../Novalist.Desktop/ViewModels/MainWindowViewModel.cs#L692-L693)).
- **Fix if broken:** Switch to explicit `Key.OemOpenBrackets` with `KeyModifiers.Control`. Test on US, DE, FR layouts. Provide alt binding if layout-dependent.

**55. Smart Lists color swatch + result count** — _16-smart-lists.md_
- Verify `SmartList.Color` rendered as swatch and count badge present in [SmartListsPanelView.axaml](../Novalist.Desktop/Views/SmartListsPanelView.axaml).
- **Fix if absent:** Add `Border Width=10 Height=10 Background="{Binding ColorBrush}"` and `TextBlock Text="{Binding ResultCount}"` to each row template.

**56. Manuscript Mode "Filter to one plotline"** — _08-plot-grid.md_
- Manual hedges; no `PlotlineFilter` in `ManuscriptViewModel.cs`.
- **Fix:** Add `FilterPlotlineId : Guid?` to `ManuscriptViewModel`. Add ComboBox in Manuscript toolbar listing book's plotlines + "All". Filter visible chunks where scene has plotline.

**57. Manuscript word-count / reading-time honoring `FilterStatus`** — _10-manuscript.md_
- VM filter exists (line 83), display claim unverified.
- **Fix if not honoring filter:** Compute `VisibleWordCount` and `VisibleReadingTime` from filtered collection (not full project). Bind in toolbar.

**58. Welcome card sort order ("most recent")** — _01-getting-started.md_
- **Fix if not sorted:** Order `AppSettings.RecentProjects` by `LastOpenedAt` descending in `WelcomeViewModel` constructor and on `RefreshRecents`.

**59. Character `Add override` flow** — _06-codex.md_
- Banner + StopOverrideButton at [EntityEditorView.axaml:61-71](../Novalist.Desktop/Views/EntityEditorView.axaml#L61-L71); add-override UI not traced.
- **Fix if absent:** Add `AddOverrideCommand(OverrideScope scope, Guid scopeTargetId)` on `EntityEditorViewModel` constructing a new `CharacterOverride`. Add UI affordance (Button "Add override for current chapter/act/scene") that creates override and enters override-edit mode.

**60. Timeline `Apply structure...` flyout + `Export outline` button (reverse gap)** — _12-timeline.md_
- Present at [TimelineView.axaml:64-79](../Novalist.Desktop/Views/TimelineView.axaml#L64-L79), undocumented.
- **Fix:** Document in `12-timeline.md`: Apply Structure flyout (lists story-structure templates, populates timeline events), Export Outline (exports current timeline as markdown/text outline). Reverse direction from other entries but worth fixing for consistency.

**61. Snapshot folder configurable per book** — _17-snapshots.md, 03-projects-and-books.md_
- **Fix if absent:** Add `SnapshotFolder : string` (default `Snapshots`) to `BookData`. Use this when computing snapshot path in `SnapshotService`. Expose in book Settings.
