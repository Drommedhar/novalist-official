import { type Mode } from '../shell/modes'

/**
 * Everything the main area can show.
 *
 * No longer the top-level navigation: a writer picks a *mode* and the mode's
 * panel lists the views it holds. `MainView` stays as the identifier panes,
 * deep links, help targets and the command palette address, which is what let
 * the navigation change without any of them having to.
 */
export type MainView =
  | 'write'
  | 'dashboard'
  | 'manuscript'
  | 'drafts'
  | 'narration'
  | 'timeline'
  | 'plotGrid'
  | 'calendar'
  | 'relationships'
  | 'dialogue'
  | 'style'
  | 'canvas'
  | 'codex'
  | 'wiki'
  | 'maps'
  | 'languages'
  | 'series'
  | 'research'
  | 'gallery'
  | 'expose'
  | 'export'
  | 'git'
  | 'extensions'
  | 'settings'
  | 'about'

/**
 * The dialogs the shell owns.
 *
 * They were local state inside the toolbar that raised them, which meant a
 * dialog could only ever be opened by the one button that happened to hold its
 * flag - so "New chapter" could not be reached from the command palette, the
 * menu bar, or anything else. Owning them here is what lets the command
 * registry name them.
 */
export type ShellDialog =
  | 'chapter'
  | 'scene'
  | 'book'
  | 'draft'
  | 'renameProject'
  | 'renameBook'
  | 'snapshots'
  | 'draftCompare'
  | 'deleteDraft'
  | 'paneLayouts'
  | 'createProject'
  | 'restoreBackup'
  | 'importPlugin'
  | 'importManuscript'
  | 'importFolder'

export type BinderTab = 'chapters' | 'smartLists' | 'collections' | 'bookmarks'

export interface ActiveExtView {
  extensionId: string
  key: string
}

/** Right context sidebar tabs, mirroring the desktop Context / Footnotes tabs. */
export type InspectorTab = 'context' | 'footnotes' | 'inbox' | 'notes'

/** Active destination in the mobile bottom (native Liquid Glass) tab bar. */
export type MobileTab = 'dashboard' | 'manuscript' | 'codex' | 'planning' | 'settings' | 'export'

/* ===== Panes =====
 * The content area was one view at a time, with the editor allowed to split in
 * two. A writer with the manuscript, the Codex and their notes open at once had
 * to choose two of the three and keep swapping for the rest.
 *
 * A tree rather than a list, because "split this one again" is the thing people
 * actually do: three panes down the left and one tall one on the right is a
 * shape a flat list cannot hold.
 */
export type PaneNode =
  | { kind: 'leaf'; id: string; view: MainView }
  | { kind: 'split'; id: string; direction: 'row' | 'column'; children: PaneNode[]; sizes: number[] }

/** A layout the writer named and can come back to. */
export interface SavedLayout {
  name: string
  root: PaneNode
}

/** Available horizontal capacity of the actual shell content area. */
export type ShellCapacity = 'compact' | 'medium' | 'wide'

export interface PanelSizes {
  binderWidth?: number
  inspectorWidth?: number
  notesDockHeight?: number
  /** Not a size, but the same kind of thing: view state, remembered per machine. */
  modePanelVisible?: boolean
}

/**
 * Which mobile layout the native shell is showing, mirroring its horizontal size
 * class: 'phone' is the compact single-pane layout (iPhone, and a narrow iPad
 * Split View / Slide Over window), 'tablet' the iPad two-pane one. Announced by
 * RendererHostPage through window.__novalistLayout and kept here so views can
 * adapt without each re-deriving it from the window width.
 */
export type MobileLayout = 'phone' | 'tablet'

/**
 * A screen's claim that leaving it right now would cost the writer work.
 *
 * The plot thread screen lost a full edit to a click on a dialog backdrop, and
 * an activity-bar button is the same click with a different target: the screen
 * unmounts and the state it held goes with it. A guard is the screen saying so
 * once, to the one place every navigation passes through.
 */
export interface UnsavedGuard {
  /** Identifies the registration, so unmounting the old screen cannot cancel
   *  the guard of the screen that replaced it. */
  id: string
  /** What is unsaved, in the writer's words - a thread's name, a scene title. */
  label: string
  /** Read at the moment of leaving rather than at registration, because a
   *  screen goes clean and dirty again while it sits there. */
  isDirty(): boolean
  /** Write the edits, so the prompt can offer to keep them. */
  save(): Promise<void>
}

/** A navigation held back until the writer says what to do with their edits. */
export interface PendingLeave {
  label: string
  proceed(): void
}

export interface ShellState {
  mainView: MainView
  /**
   * The workspace the writer is in. Which views are one click away, and what
   * the window looks like around them, both follow from it.
   */
  mode: Mode
  /** The content area's pane tree. One leaf until the writer splits it. */
  panes: PaneNode
  /** Which pane a view change lands in, and which one is outlined. */
  activePaneId: string
  /** Layouts the writer named. */
  layouts: SavedLayout[]
  mobileTab: MobileTab
  mobileLayout: MobileLayout
  extView: ActiveExtView | null
  binderTab: BinderTab
  binderVisible: boolean
  binderWidth: number
  /** A compact-shell drawer. Kept separate from the wide-layout preference. */
  binderOverlayOpen: boolean
  /** Whether desktop navigation is shown. When visible it stays above scenes. */
  modePanelVisible: boolean
  inspectorVisible: boolean
  inspectorWidth: number
  /** Inspector drawer used when there is not room for a persistent sidebar. */
  inspectorOverlayOpen: boolean
  shellWidth: number
  shellCapacity: ShellCapacity
  inspectorTab: InspectorTab
  /**
   * One-shot: the footnote whose text box should take the caret, consumed by
   * the Footnotes panel.
   *
   * Inserting a footnote put an empty note in a list on the other side of the
   * window and left the caret in the prose, so the writer had to go and find
   * the row before they could write the note - which is the moment they know
   * what it says.
   */
  pendingFootnoteText: string | null
  /**
   * One-shot: a suggested edit to scroll to once its scene is on screen, or ''
   * for the first one in that scene. Consumed by the editor pane showing it.
   */
  pendingSuggestion: { sceneId: string; changeId: string } | null
  /** Bottom scene-notes dock (Synopsis + Notes), Scene view only. Off by default. */
  notesDockVisible: boolean
  /** One-shot prefill for the Settings search box, used to deep-link a section. */
  settingsSearch: string
  /** One-shot request to open a specific map and centre a pin, consumed by
   * MapsView. Set by the focus-peek card's "ON MAPS" links. */
  pendingMapNav: { mapId: string; pinId: string } | null
  /** Chapter requested from the binder; consumed by ExportView. */
  pendingExportChapter: string | null
  mobileExportSelection: { scope: string; chapters: string[] } | null
  backendVersion: string | null
  focusMode: boolean
  focusPaneId: string | null
  focusPanel: 'binder' | 'inspector' | 'notes' | null
  focusPanelTransient: boolean
  revealFocusPanel(panel: 'binder' | 'inspector'): void
  focusToolsVisible: boolean
  setFocusToolsVisible(visible: boolean): void
  closeFocusPanel(): void
  returnToFocus(): void
  findReplaceOpen: boolean
  cleanupOpen: boolean
  commandPaletteOpen: boolean
  /** Quick-open overlay: one search box across scenes, Codex, research, events. */
  quickOpenOpen: boolean
  /** Quick-capture overlay: jot a note straight into the research inbox. */
  quickCaptureOpen: boolean
  /** One-shot request to open a research item, consumed by ResearchView. */
  pendingResearchId: string | null
  /** A coined word to search for when Languages opens. */
  pendingLanguageQuery: string | null
  /** In-app user-manual help viewer overlay. */
  helpOpen: boolean
  /** Named workspace layouts: save the shape you are in, come back to it. */
  layoutsOpen: boolean
  /** The short walk through the views, offered once per installation. */
  tourOpen: boolean
  /** Which shell-owned dialog is up, or null. One at a time, by construction. */
  dialog: ShellDialog | null
  /**
   * Whether typing proposes changes rather than making them. Shell state
   * because it is a mode of the writing view rather than of one toolbar, and
   * because a mode nothing outside its own button can leave is a trap.
   */
  suggestionMode: boolean
  openDialog(dialog: ShellDialog): void
  closeDialog(): void
  toggleSuggestionMode(): void
  setSuggestionMode(on: boolean): void
  setMainView(view: MainView): void
  /** Switches workspace, landing on the view the writer last had in it. */
  setMode(mode: Mode): void
  /** Back to the screen a project opens on. */
  goHome(): void
  /** What the screens in front of the writer would lose if they left now, by
   *  id. A map rather than one slot, because a dialog with unsaved input can
   *  sit inside a screen that has some of its own. */
  unsavedGuards: Record<string, UnsavedGuard>
  /** The move the writer asked for, waiting on an answer about their edits. */
  pendingLeave: PendingLeave | null
  registerUnsavedGuard(guard: UnsavedGuard): void
  /** Called by id, so a screen that has already been replaced cannot unregister
   *  the one that replaced it. */
  clearUnsavedGuard(id: string): void
  /**
   * Do this, unless the screen being left holds edits nobody saved.
   *
   * Every navigation runs through here, so a screen registers once and is
   * covered by the activity bar, the palette, a hotkey, a plugin and the
   * binder alike - rather than each door having to remember to ask.
   */
  guardLeave(proceed: () => void): void
  /** The writer's answer to that prompt. */
  resolveLeave(action: 'cancel' | 'discard' | 'save'): Promise<void>
  toggleModePanelVisible(): void
  setActivePane(id: string): void
  /** Points one pane at a view without moving the writer into it. */
  setPaneView(id: string, view: MainView): void
  splitActivePane(direction: 'row' | 'column'): void
  /** Splits a named pane and returns the id of the pane that appeared. */
  splitPaneById(id: string, direction: 'row' | 'column'): string | null
  closeActivePane(): void
  /** Closes one pane. The last pane in the window always stays. */
  closePaneById(id: string): void
  setPaneSizes(splitId: string, sizes: number[]): void
  /** Back to a single pane, the arrangement the window starts in. */
  resetPanes(): void
  saveLayout(name: string): void
  applyLayout(name: string): void
  deleteLayout(name: string): void
  setMobileTab(tab: MobileTab): void
  openExport(chapterGuid?: string): void
  setMobileLayout(layout: MobileLayout): void
  /**
   * Tablet: whether the native sidebar is showing as an icon-only rail. Lives
   * here rather than in TabletShell because that component unmounts whenever a
   * narrow Split View drops to the phone layout - local state would reset to
   * "expanded" while the native sidebar stayed a rail, desyncing the toggle.
   */
  sidebarCollapsed: boolean
  setSidebarCollapsed(collapsed: boolean): void
  /** Switch to the Maps view and ask it to open the given map and focus a pin. */
  navigateToMapPin(mapId: string, pinId: string): void
  /** MapsView clears the pending nav once it has consumed it. */
  clearPendingMapNav(): void
  /** Navigate to Settings, optionally prefilling the search to reach a section. */
  openSettings(search?: string): void
  setExtView(view: ActiveExtView | null): void
  setBinderTab(tab: BinderTab): void
  toggleBinder(): void
  setBinderWidth(px: number): void
  toggleInspector(): void
  setInspectorWidth(px: number): void
  setInspectorTab(tab: InspectorTab): void
  /**
   * Shows the Footnotes list and asks it to put the caret in one note's box.
   * The inspector is opened if it was away, because a panel the writer cannot
   * see is not somewhere a caret can usefully go.
   */
  requestFootnoteText(footnoteId: string): void
  /** The Footnotes panel clears it once the box has the caret. */
  clearPendingFootnoteText(): void
  /** Asks the editor showing this scene to scroll to a suggested edit. */
  revealSuggestion(sceneId: string, changeId: string): void
  /** The editor pane clears it once it has taken the writer there. */
  clearPendingSuggestion(): void
  toggleNotesDock(): void
  setBackendVersion(version: string | null): void
  /** Actual rendered shell width, after DPI/UI scale. */
  setShellMetrics(width: number): void
  toggleFocusMode(): void
  setFindReplaceOpen(open: boolean): void
  setCleanupOpen(open: boolean): void
  setCommandPaletteOpen(open: boolean): void
  setQuickOpenOpen(open: boolean): void
  setQuickCaptureOpen(open: boolean): void
  /** Switch to Research and ask it to select the given item. */
  navigateToResearch(itemId: string): void
  /** ResearchView clears the pending selection once it has consumed it. */
  clearPendingResearch(): void
  /** Switch to Languages and search for a word. */
  navigateToLanguage(word: string): void
  /** LanguagesView clears it once consumed. */
  clearPendingLanguage(): void
  setHelpOpen(open: boolean): void
  setLayoutsOpen(open: boolean): void
  setTourOpen(open: boolean): void
}
