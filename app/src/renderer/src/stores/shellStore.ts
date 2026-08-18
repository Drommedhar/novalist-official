import { create } from 'zustand'
import {
  parseSettingsDestination,
  setSettingsDestination
} from '../views/settings/settingsNavigation'
import { isSettingsSectionKey } from '../views/settings/settingsRegistry'
import { HOME_VIEW, MODE_VIEWS, modeOf, type Mode } from '../shell/modes'

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
  | 'snapshots'
  | 'draftCompare'
  | 'deleteDraft'
  | 'paneLayouts'
  | 'createProject'
  | 'importPlugin'
  | 'importManuscript'

export type BinderTab = 'chapters' | 'smartLists' | 'collections' | 'bookmarks'

export interface ActiveExtView {
  extensionId: string
  key: string
}

/** Right context sidebar tabs, mirroring the desktop Context / Footnotes tabs. */
export type InspectorTab = 'context' | 'footnotes' | 'inbox'

/** Active destination in the mobile bottom (native Liquid Glass) tab bar. */
export type MobileTab = 'dashboard' | 'manuscript' | 'codex' | 'planning' | 'settings'

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

let paneSeq = 0
function paneId(): string {
  paneSeq += 1
  return `pane-${paneSeq}`
}

export function newLeaf(view: MainView): PaneNode {
  return { kind: 'leaf', id: paneId(), view }
}

/**
 * Whether two pane trees are the same arrangement.
 *
 * Ids are deliberately ignored: applying a layout re-identifies every node, so
 * a tree restored from a layout never shares an id with the one that was saved.
 * What makes two arrangements the same is their shape, their direction, their
 * proportions and the view in each leaf.
 */
export function sameArrangement(a: PaneNode, b: PaneNode): boolean {
  if (a.kind !== b.kind) return false
  if (a.kind === 'leaf') return a.view === (b as typeof a).view

  const other = b as typeof a
  return (
    a.direction === other.direction &&
    a.children.length === other.children.length &&
    // A dragged divider is a different arrangement, but a pixel of rounding is
    // not - the sizes are floats the writer never typed.
    a.sizes.every((size, i) => Math.abs(size - (other.sizes[i] ?? 0)) < 0.5) &&
    a.children.every((child, i) => sameArrangement(child, other.children[i]))
  )
}

/**
 * The built-in single-pane layout, always offered and never deletable.
 *
 * Not a `SavedLayout`: it is the arrangement the window starts in rather than
 * one the writer named, so keeping it out of the stored list is what stops it
 * being renamed, overwritten or forgotten. The sentinel cannot collide with a
 * layout somebody names "Default" either.
 */
export const DEFAULT_LAYOUT = '__default'

/**
 * The layout the window is currently in, or '' when it is in none.
 *
 * A layout the writer named wins over the built-in default: both match an
 * unsplit window, and the name they chose says more than "Default" does.
 */
export function matchingLayout(panes: PaneNode, layouts: SavedLayout[]): string {
  const named = layouts.find((l) => sameArrangement(panes, l.root))
  if (named) return named.name
  return panes.kind === 'leaf' ? DEFAULT_LAYOUT : ''
}

/** The leaf with this id, or null. */
export function findPane(node: PaneNode, id: string): PaneNode | null {
  if (node.id === id) return node
  if (node.kind === 'split') {
    for (const child of node.children) {
      const found = findPane(child, id)
      if (found) return found
    }
  }
  return null
}

/** Every leaf, left to right, top to bottom. */
export function paneLeaves(node: PaneNode): Extract<PaneNode, { kind: 'leaf' }>[] {
  return node.kind === 'leaf' ? [node] : node.children.flatMap(paneLeaves)
}

/**
 * Whether any pane is showing one of these views.
 *
 * What the surfaces around the content area have to ask. `mainView` names the
 * view of the pane the writer is in, so a shell asking it about the editor got
 * "no" the moment they clicked into the Codex beside it - which is how the
 * context sidebar and the notes dock disappeared as soon as anyone split the
 * window.
 */
export function anyPaneShows(node: PaneNode, views: MainView[]): boolean {
  return paneLeaves(node).some((leaf) => views.includes(leaf.view))
}

/**
 * Splits a leaf in two. The new pane opens on the same view, because splitting
 * to look at the same thing twice - two places in one manuscript - is at least
 * as common as splitting to look at two different things.
 */
export function splitPane(
  node: PaneNode,
  id: string,
  direction: 'row' | 'column'
): { root: PaneNode; created: string | null } {
  if (node.kind === 'leaf') {
    if (node.id !== id) return { root: node, created: null }
    const fresh = newLeaf(node.view)
    return {
      root: {
        kind: 'split',
        id: paneId(),
        direction,
        children: [node, fresh],
        sizes: [50, 50]
      },
      created: fresh.id
    }
  }

  let created: string | null = null
  const children = node.children.map((child) => {
    const result = splitPane(child, id, direction)
    if (result.created) created = result.created
    return result.root
  })
  return { root: { ...node, children }, created }
}

/**
 * Removes a pane. A split left holding one child collapses into it, or the
 * tree grows a spine of pointless containers as panes come and go.
 */
export function closePane(node: PaneNode, id: string): PaneNode | null {
  if (node.id === id) return null
  if (node.kind === 'leaf') return node

  const children = node.children
    .map((child) => closePane(child, id))
    .filter((child): child is PaneNode => child !== null)

  if (children.length === 0) return null
  if (children.length === 1) return children[0]
  return { ...node, children, sizes: even(children.length) }
}

/** Points one leaf at a different view. */
export function setPaneViewIn(node: PaneNode, id: string, view: MainView): PaneNode {
  if (node.kind === 'leaf') return node.id === id ? { ...node, view } : node
  return { ...node, children: node.children.map((c) => setPaneViewIn(c, id, view)) }
}

function even(count: number): number[] {
  return Array.from({ length: count }, () => 100 / count)
}

/* ===== Panel geometry =====
 * Sizes the user drags survive a restart, and the first-run defaults are taken
 * from the window rather than fixed, because the app opens maximised: a flat
 * 240px binder that reads fine on a laptop is a sliver on a 2560px display.
 * Persisted to localStorage alongside the other view-state preferences (see
 * ContextPanel's section-collapse flags) rather than through the settings
 * backend - this is view state, not configuration. */
const PANEL_STORAGE_KEY = 'nl.shell.panels'

/* The floors are what a panel has to be to do its job, not the narrowest it can
 * be drawn. At 180 the binder left about 120px for a scene title - the app runs
 * on a 17px serif, so that is a dozen characters - and every chapter and every
 * scene in the tree read as an ellipsis, with the three scene-filter chips
 * coming out as "In the ...", "Every..." and "Out o...". 290 is what an
 * ordinary twenty-five-character scene title measures once the indent, the
 * status dot and the word count have taken their share; the inspector wraps its
 * text rather than clipping it, so it needs less. */
export const BINDER_MIN = 290
export const BINDER_MAX = 640
export const INSPECTOR_MIN = 280
export const INSPECTOR_MAX = 720
export const NOTES_DOCK_MIN = 80
export const NOTES_DOCK_MAX = 640

/** Available horizontal capacity of the actual shell content area. */
export type ShellCapacity = 'compact' | 'medium' | 'wide'

export function shellCapacityForWidth(width: number): ShellCapacity {
  if (width < 900) return 'compact'
  if (width < 1240) return 'medium'
  return 'wide'
}

/**
 * A remembered drag width is a preference, not permission to squeeze the
 * editor out of the window. Runtime width is capped against the shell itself;
 * the stored preference remains untouched and comes back on a wider monitor.
 */
export function panelWidthForShell(
  preferred: number,
  shellWidth: number,
  min: number,
  max: number
): number {
  const capacityCap = Math.max(min, Math.floor(shellWidth * 0.28))
  return clamp(preferred, min, Math.min(max, capacityCap))
}

interface PanelSizes {
  binderWidth?: number
  inspectorWidth?: number
  notesDockHeight?: number
  /** Not a size, but the same kind of thing: view state, remembered per machine. */
  modePanelDocked?: boolean
}

function clamp(px: number, min: number, max: number): number {
  return Math.max(min, Math.min(max, Math.round(px)))
}

function readPanelSizes(): PanelSizes {
  try {
    return JSON.parse(localStorage.getItem(PANEL_STORAGE_KEY) || '{}') as PanelSizes
  } catch {
    return {}
  }
}

/** Merges one measurement into the stored set. Called on drag end, not on every
 *  pointer move, so a drag writes once. */
export function savePanelSize(patch: PanelSizes): void {
  try {
    localStorage.setItem(PANEL_STORAGE_KEY, JSON.stringify({ ...readPanelSizes(), ...patch }))
  } catch {
    // Private mode or a full quota: the session still works, it just forgets.
  }
}

/** A stored size if the user has ever set one, else a share of the window. */
function initialPanelSize(
  stored: number | undefined,
  fraction: number,
  min: number,
  max: number,
  basis: number
): number {
  if (typeof stored === 'number' && Number.isFinite(stored)) return clamp(stored, min, max)
  return clamp(basis * fraction, min, max)
}

const storedPanels = readPanelSizes()

/* Initial guesses only. AppShell immediately replaces the basis with its own
 * ResizeObserver measurement, which is the width that actually matters after
 * OS DPI, page scale, split view and window restoration have been applied. */
const screenW = typeof window === 'undefined' ? 1440 : window.innerWidth || 1440
const screenH = typeof window === 'undefined' ? 900 : window.innerHeight || 900

export const NOTES_DOCK_DEFAULT = initialPanelSize(
  storedPanels.notesDockHeight,
  0.16,
  NOTES_DOCK_MIN,
  NOTES_DOCK_MAX,
  screenH
)

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

interface ShellState {
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
  /**
   * Whether the mode panel is docked beside the rail. A remembered preference:
   * in Write the panel lists two views beside a binder that is already a list,
   * and a writer who wants the window back should be able to have it without
   * losing the switcher - the rail still changes mode, and the panel comes back
   * as an overlay.
   */
  modePanelDocked: boolean
  /**
   * The mode panel as an overlay: always how it appears in a window too narrow
   * to dock it, and how it appears at any width once undocked. The same rows in
   * the same order - a different arrangement of one list, not a second one.
   */
  modePanelOpen: boolean
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
  backendVersion: string | null
  focusMode: boolean
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
  setModePanelOpen(open: boolean): void
  toggleModePanelDocked(): void
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

/** Sets one split's proportions. */
function resize(node: PaneNode, splitId: string, sizes: number[]): PaneNode {
  if (node.kind === 'leaf') return node
  if (node.id === splitId) return { ...node, sizes }
  return { ...node, children: node.children.map((c) => resize(c, splitId, sizes)) }
}

/* Layouts are about the writer's screen rather than their book, so they live
 * beside the other view-state preferences instead of in the project.
 *
 * Its own key: pane layouts and the named workspace layouts (layoutStore) both
 * called themselves "nl.shell.layouts" and stored different shapes under it, so
 * saving one kind erased the other and opening the pane-layout list on an entry
 * the dialog had written asked for the leaves of a tree that was not there. */
const LAYOUT_STORAGE_KEY = 'nl.shell.paneLayouts'

function readLayouts(): SavedLayout[] {
  try {
    const raw = localStorage.getItem(LAYOUT_STORAGE_KEY)
    const parsed: unknown = raw ? JSON.parse(raw) : []
    if (!Array.isArray(parsed)) return []
    // Anything without a pane tree is not a pane layout, whoever wrote it.
    return (parsed as SavedLayout[]).filter(
      (layout) => typeof layout?.name === 'string' && isPaneNode(layout.root)
    )
  } catch {
    return []
  }
}

function isPaneNode(node: unknown): node is PaneNode {
  if (!node || typeof node !== 'object') return false
  const candidate = node as PaneNode
  if (candidate.kind === 'leaf') return typeof candidate.id === 'string'
  return (
    candidate.kind === 'split' &&
    Array.isArray(candidate.children) &&
    candidate.children.every(isPaneNode)
  )
}

/**
 * Gives a restored tree fresh pane ids.
 *
 * Ids are handed out per session, so a layout saved yesterday holds "pane-2"
 * while this session is about to hand that name to the next split - and two
 * panes answering to one id are one pane as far as everything keyed by it is
 * concerned, which for the editor means both showing the same scene.
 */
function reidentify(node: PaneNode): PaneNode {
  return node.kind === 'leaf'
    ? { ...node, id: paneId() }
    : { ...node, id: paneId(), children: node.children.map(reidentify) }
}

function persistLayouts(layouts: SavedLayout[]): void {
  try {
    localStorage.setItem(LAYOUT_STORAGE_KEY, JSON.stringify(layouts))
  } catch {
    // A full or blocked store costs the writer their saved layouts, not their
    // session.
  }
}

const storedLayouts = readLayouts()
const initialPanes = newLeaf('write')

/**
 * Put a view in front of the writer.
 *
 * The content area is a tree of panes and the main area renders that tree, so
 * setting `mainView` alone changes a label and nothing on screen. Every
 * navigation has to land in the active pane - setMainView did, and the four
 * that navigate somewhere specific (a map pin, a research item, Settings) did
 * not, so clicking one of those quietly did nothing after panes shipped.
 */
function showView(
  state: ShellState,
  mainView: MainView
): Pick<
  ShellState,
  'mainView' | 'mode' | 'extView' | 'panes' | 'binderOverlayOpen' | 'inspectorOverlayOpen'
> {
  return {
    mainView,
    // A view carries its mode with it, so a deep link, a hotkey or the palette
    // lands the writer in the workspace that view belongs to rather than
    // leaving the rail pointing somewhere they no longer are. Dashboard,
    // Settings and About belong to no mode and leave the last one standing.
    mode: modeOf(mainView) ?? state.mode,
    extView: null,
    panes: setPaneViewIn(state.panes, state.activePaneId, mainView),
    binderOverlayOpen: false,
    inspectorOverlayOpen: false
  }
}

/**
 * The view a mode opens on.
 *
 * Remembered per mode, so going to Plan and back to World returns to the Codex
 * entry you were reading rather than to the top of the mode.
 */
const lastInMode: Partial<Record<Mode, MainView>> = {}

export const useShellStore = create<ShellState>((set, get) => ({
  mainView: 'write',
  mode: 'write',
  panes: initialPanes,
  activePaneId: initialPanes.id,
  layouts: storedLayouts,
  mobileTab: 'dashboard',
  // Compact until the native side says otherwise, so the desktop build and any
  // pre-announcement frame render the narrow layout rather than flashing panes.
  mobileLayout: 'phone',
  sidebarCollapsed: false,
  extView: null,
  binderTab: 'chapters',
  binderVisible: true,
  binderWidth: initialPanelSize(storedPanels.binderWidth, 0.17, BINDER_MIN, BINDER_MAX, screenW),
  binderOverlayOpen: false,
  modePanelDocked: storedPanels.modePanelDocked !== false,
  modePanelOpen: false,
  inspectorVisible: true,
  inspectorWidth: initialPanelSize(
    storedPanels.inspectorWidth,
    0.2,
    INSPECTOR_MIN,
    INSPECTOR_MAX,
    screenW
  ),
  inspectorOverlayOpen: false,
  shellWidth: screenW,
  shellCapacity: shellCapacityForWidth(screenW),
  inspectorTab: 'context',
  pendingFootnoteText: null,
  pendingSuggestion: null,
  notesDockVisible: false,
  settingsSearch: '',
  pendingMapNav: null,
  backendVersion: null,
  focusMode: false,
  findReplaceOpen: false,
  cleanupOpen: false,
  commandPaletteOpen: false,
  quickOpenOpen: false,
  quickCaptureOpen: false,
  pendingResearchId: null,
  pendingLanguageQuery: null,
  helpOpen: false,
  layoutsOpen: false,
  tourOpen: false,
  dialog: null,
  suggestionMode: false,
  unsavedGuards: {},
  pendingLeave: null,
  openDialog: (dialog) => set({ dialog }),
  closeDialog: () => set({ dialog: null }),
  toggleSuggestionMode: () => set((s) => ({ suggestionMode: !s.suggestionMode })),
  setSuggestionMode: (suggestionMode) => set({ suggestionMode }),
  registerUnsavedGuard: (guard) =>
    set((s) => ({ unsavedGuards: { ...s.unsavedGuards, [guard.id]: guard } })),

  clearUnsavedGuard: (id) =>
    set((s) => {
      if (!(id in s.unsavedGuards)) return {}
      const rest = { ...s.unsavedGuards }
      delete rest[id]
      return { unsavedGuards: rest }
    }),

  guardLeave: (proceed) => {
    const dirty = Object.values(get().unsavedGuards).filter((g) => g.isDirty())
    if (dirty.length === 0) {
      proceed()
      return
    }
    set({ pendingLeave: { label: dirty[0].label, proceed } })
  },

  resolveLeave: async (action) => {
    const pending = get().pendingLeave
    if (!pending) return
    if (action === 'cancel') {
      set({ pendingLeave: null })
      return
    }
    const dirty = Object.values(get().unsavedGuards).filter((g) => g.isDirty())
    // Everything the move would have cost, not only the screen the prompt
    // happened to name.
    if (action === 'save') for (const guard of dirty) await guard.save()
    // The guards go before the move does, so the navigation being released is
    // not stopped a second time by what it is leaving.
    set({ pendingLeave: null, unsavedGuards: {} })
    pending.proceed()
  },

  setMainView: (mainView) =>
    get().guardLeave(() =>
      set((s) => {
        const mode = modeOf(mainView)
        if (mode) lastInMode[mode] = mainView
        return showView(s, mainView)
      })
    ),

  setMode: (mode) =>
    get().guardLeave(() =>
      set((s) => {
        const views = MODE_VIEWS[mode]
        const landing = lastInMode[mode] ?? views[0]
        return { ...showView(s, landing), mode }
      })
    ),

  goHome: () => get().guardLeave(() => set((s) => showView(s, HOME_VIEW))),

  setModePanelOpen: (modePanelOpen) =>
    set(modePanelOpen ? { modePanelOpen, binderOverlayOpen: false } : { modePanelOpen }),

  toggleModePanelDocked: () =>
    set((s) => {
      const modePanelDocked = !s.modePanelDocked
      savePanelSize({ modePanelDocked })
      // Undocking while it is on screen should not leave the overlay behind it.
      return { modePanelDocked, modePanelOpen: false }
    }),

  setActivePane: (activePaneId) =>
    set((s) => {
      const pane = findPane(s.panes, activePaneId)
      return pane && pane.kind === 'leaf'
        ? { activePaneId, mainView: pane.view }
        : { activePaneId }
    }),

  setPaneView: (id, view) =>
    set((s) => {
      const pane = findPane(s.panes, id)
      if (!pane || pane.kind !== 'leaf' || pane.view === view) return {}
      return {
        panes: setPaneViewIn(s.panes, id, view),
        // The label the toolbar and the palette read follows the pane the writer
        // is in, so retargeting some other pane must not move it.
        ...(id === s.activePaneId ? { mainView: view, extView: null } : {})
      }
    }),

  splitActivePane: (direction) =>
    set((s) => {
      const { root, created } = splitPane(s.panes, s.activePaneId, direction)
      return created ? { panes: root, activePaneId: created } : {}
    }),

  splitPaneById: (id, direction) => {
    const { root, created } = splitPane(get().panes, id, direction)
    if (created) set({ panes: root, activePaneId: created })
    return created
  },

  closeActivePane: () => get().closePaneById(get().activePaneId),

  closePaneById: (id) =>
    set((s) => {
      // The last pane stays: a content area with nothing in it is not a layout,
      // it is a broken window.
      if (paneLeaves(s.panes).length < 2) return {}
      const root = closePane(s.panes, id)
      if (!root) return {}
      // Closing the pane you were in moves you somewhere real; closing another
      // one leaves you where you were.
      if (id !== s.activePaneId && findPane(root, s.activePaneId)) return { panes: root }
      const first = paneLeaves(root)[0]
      return { panes: root, activePaneId: first.id, mainView: first.view }
    }),

  setPaneSizes: (splitId, sizes) =>
    set((s) => ({ panes: resize(s.panes, splitId, sizes) })),

  resetPanes: () =>
    set((s) => {
      if (s.panes.kind === 'leaf') return {}
      // The view you are on comes with you. Which view a pane shows is where you
      // are in the book rather than how the window is arranged, and going back
      // to one pane should not also navigate you somewhere you did not ask for.
      const here = findPane(s.panes, s.activePaneId)
      const view = here && here.kind === 'leaf' ? here.view : s.mainView
      const root = newLeaf(view)
      return { panes: root, activePaneId: root.id, mainView: view }
    }),

  saveLayout: (name) =>
    set((s) => {
      const layouts = [
        ...s.layouts.filter((l) => l.name !== name),
        { name, root: s.panes }
      ]
      persistLayouts(layouts)
      return { layouts }
    }),

  applyLayout: (name) =>
    set((s) => {
      const layout = s.layouts.find((l) => l.name === name)
      if (!layout) return {}
      const root = reidentify(layout.root)
      const first = paneLeaves(root)[0]
      return { panes: root, activePaneId: first.id, mainView: first.view }
    }),

  deleteLayout: (name) =>
    set((s) => {
      const layouts = s.layouts.filter((l) => l.name !== name)
      persistLayouts(layouts)
      return { layouts }
    }),

  setMobileTab: (mobileTab) => set({ mobileTab }),
  setMobileLayout: (mobileLayout) => set({ mobileLayout }),
  setSidebarCollapsed: (sidebarCollapsed) => set({ sidebarCollapsed }),
  navigateToMapPin: (mapId, pinId) =>
    get().guardLeave(() =>
      set((s) => ({ ...showView(s, 'maps'), pendingMapNav: { mapId, pinId } }))
    ),
  clearPendingMapNav: () => set({ pendingMapNav: null }),
  openSettings: (search = '') =>
    get().guardLeave(() => {
      const current = get()
      const parsed = parseSettingsDestination(search)
      const directSection = isSettingsSectionKey(search) ? { section: search } : null
      setSettingsDestination({
        ...(parsed ?? directSection ?? (search ? { query: search } : { section: 'appearance' })),
        ...(current.mainView !== 'settings'
          ? { origin: { view: current.mainView, labelKey: `shell.view.${current.mainView}` } }
          : {})
      })
      set((s) => ({ ...showView(s, 'settings'), settingsSearch: '' }))
    }),
  setExtView: (extView) => set({ extView }),
  setBinderTab: (binderTab) => set({ binderTab }),
  toggleFocusMode: () => set((s) => ({ focusMode: !s.focusMode })),
  setFindReplaceOpen: (findReplaceOpen) => set({ findReplaceOpen }),
  setCleanupOpen: (cleanupOpen) => set({ cleanupOpen }),
  setCommandPaletteOpen: (commandPaletteOpen) => set({ commandPaletteOpen }),
  setQuickOpenOpen: (quickOpenOpen) => set({ quickOpenOpen }),
  setQuickCaptureOpen: (quickCaptureOpen) => set({ quickCaptureOpen }),
  navigateToResearch: (itemId) =>
    get().guardLeave(() =>
      set((s) => ({ ...showView(s, 'research'), pendingResearchId: itemId }))
    ),
  clearPendingResearch: () => set({ pendingResearchId: null }),
  navigateToLanguage: (word) =>
    get().guardLeave(() =>
      set((s) => ({ ...showView(s, 'languages'), pendingLanguageQuery: word }))
    ),
  clearPendingLanguage: () => set({ pendingLanguageQuery: null }),
  setHelpOpen: (helpOpen) => set({ helpOpen }),
  setLayoutsOpen: (layoutsOpen) => set({ layoutsOpen }),
  setTourOpen: (tourOpen) => set({ tourOpen }),
  toggleBinder: () =>
    set((s) =>
      s.shellCapacity === 'compact'
        ? // One drawer at a time. Two of them stack against the same edge, so
          // opening the second would put it over the first.
          { binderOverlayOpen: !s.binderOverlayOpen, inspectorOverlayOpen: false, modePanelOpen: false }
        : { binderVisible: !s.binderVisible }
    ),
  setBinderWidth: (px) => set({ binderWidth: clamp(px, BINDER_MIN, BINDER_MAX) }),
  toggleInspector: () =>
    set((s) =>
      s.shellCapacity === 'wide'
        ? { inspectorVisible: !s.inspectorVisible }
        : { inspectorOverlayOpen: !s.inspectorOverlayOpen, binderOverlayOpen: false }
    ),
  setInspectorWidth: (px) => set({ inspectorWidth: clamp(px, INSPECTOR_MIN, INSPECTOR_MAX) }),
  setInspectorTab: (inspectorTab) => set({ inspectorTab }),
  requestFootnoteText: (footnoteId) =>
    set((s) => ({
      // Wide enough for a sidebar means showing it; anything narrower has the
      // same drawer the inspector button opens, and one drawer at a time.
      ...(s.shellCapacity === 'wide'
        ? { inspectorVisible: true }
        : { inspectorOverlayOpen: true, binderOverlayOpen: false }),
      inspectorTab: 'footnotes' as InspectorTab,
      pendingFootnoteText: footnoteId
    })),
  clearPendingFootnoteText: () => set({ pendingFootnoteText: null }),
  revealSuggestion: (sceneId, changeId) => set({ pendingSuggestion: { sceneId, changeId } }),
  clearPendingSuggestion: () => set({ pendingSuggestion: null }),
  toggleNotesDock: () => set((s) => ({ notesDockVisible: !s.notesDockVisible })),
  setBackendVersion: (backendVersion) => set({ backendVersion }),
  setShellMetrics: (rawWidth) =>
    set((s) => {
      const shellWidth = Math.max(1, Math.round(rawWidth))
      const shellCapacity = shellCapacityForWidth(shellWidth)
      return {
        shellWidth,
        shellCapacity,
        // A docked panel that is still flagged open would reopen as an overlay
        // the moment the window narrowed again.
        ...(shellCapacity !== 'compact' ? { binderOverlayOpen: false, modePanelOpen: false } : {}),
        ...(shellCapacity === 'wide' ? { inspectorOverlayOpen: false } : {}),
        // Crossing between constrained modes should never leave two drawers
        // stacked over the manuscript.
        ...(shellCapacity !== s.shellCapacity
          ? shellCapacity === 'compact'
            ? { inspectorOverlayOpen: false }
            : { binderOverlayOpen: false }
          : {})
      }
    })
}))
