import { create } from 'zustand'
import {
  parseSettingsDestination,
  setSettingsDestination
} from '../views/settings/settingsNavigation'
import { isSettingsSectionKey } from '../views/settings/settingsRegistry'

/** Main-area destinations. Surfaced as the slim activity-bar icon rail
 * (mirrors the Avalonia MainWindow activity bar); the editor ("write") is
 * reached by opening a scene in the binder, not via a rail button. */
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

/**
 * Ordered activity-bar groups, top block. Group boundaries render as a hairline
 * separator in the rail. Order follows the desktop activity bar: content views
 * first (Dashboard, Manuscript), then planning, then world, then publish.
 * Settings lives in the bottom block (see ActivityBar), not here.
 */
export const activityGroups: { key: string; views: MainView[] }[] = [
  { key: 'shell.groupWrite', views: ['dashboard', 'manuscript'] },
  { key: 'shell.groupPlan', views: ['timeline', 'plotGrid', 'calendar', 'relationships', 'dialogue', 'style', 'canvas', 'series'] },
  { key: 'shell.groupWorld', views: ['codex', 'wiki', 'maps', 'languages', 'research', 'gallery'] },
  { key: 'shell.groupPublish', views: ['expose', 'export', 'git'] }
]

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

export const BINDER_MIN = 180
export const BINDER_MAX = 640
export const INSPECTOR_MIN = 220
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

interface ShellState {
  mainView: MainView
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
  inspectorVisible: boolean
  inspectorWidth: number
  /** Inspector drawer used when there is not room for a persistent sidebar. */
  inspectorOverlayOpen: boolean
  shellWidth: number
  shellCapacity: ShellCapacity
  inspectorTab: InspectorTab
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
  setMainView(view: MainView): void
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
  'mainView' | 'extView' | 'panes' | 'binderOverlayOpen' | 'inspectorOverlayOpen'
> {
  return {
    mainView,
    extView: null,
    panes: setPaneViewIn(state.panes, state.activePaneId, mainView),
    binderOverlayOpen: false,
    inspectorOverlayOpen: false
  }
}

export const useShellStore = create<ShellState>((set, get) => ({
  mainView: 'write',
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
  binderWidth: initialPanelSize(storedPanels.binderWidth, 0.15, BINDER_MIN, BINDER_MAX, screenW),
  binderOverlayOpen: false,
  inspectorVisible: true,
  inspectorWidth: initialPanelSize(
    storedPanels.inspectorWidth,
    0.18,
    INSPECTOR_MIN,
    INSPECTOR_MAX,
    screenW
  ),
  inspectorOverlayOpen: false,
  shellWidth: screenW,
  shellCapacity: shellCapacityForWidth(screenW),
  inspectorTab: 'context',
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
  setMainView: (mainView) => set((s) => showView(s, mainView)),

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
    set((s) => ({ ...showView(s, 'maps'), pendingMapNav: { mapId, pinId } })),
  clearPendingMapNav: () => set({ pendingMapNav: null }),
  openSettings: (search = '') => {
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
  },
  setExtView: (extView) => set({ extView }),
  setBinderTab: (binderTab) => set({ binderTab }),
  toggleFocusMode: () => set((s) => ({ focusMode: !s.focusMode })),
  setFindReplaceOpen: (findReplaceOpen) => set({ findReplaceOpen }),
  setCleanupOpen: (cleanupOpen) => set({ cleanupOpen }),
  setCommandPaletteOpen: (commandPaletteOpen) => set({ commandPaletteOpen }),
  setQuickOpenOpen: (quickOpenOpen) => set({ quickOpenOpen }),
  setQuickCaptureOpen: (quickCaptureOpen) => set({ quickCaptureOpen }),
  navigateToResearch: (itemId) =>
    set((s) => ({ ...showView(s, 'research'), pendingResearchId: itemId })),
  clearPendingResearch: () => set({ pendingResearchId: null }),
  navigateToLanguage: (word) =>
    set((s) => ({ ...showView(s, 'languages'), pendingLanguageQuery: word })),
  clearPendingLanguage: () => set({ pendingLanguageQuery: null }),
  setHelpOpen: (helpOpen) => set({ helpOpen }),
  setLayoutsOpen: (layoutsOpen) => set({ layoutsOpen }),
  setTourOpen: (tourOpen) => set({ tourOpen }),
  toggleBinder: () =>
    set((s) =>
      s.shellCapacity === 'compact'
        ? { binderOverlayOpen: !s.binderOverlayOpen, inspectorOverlayOpen: false }
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
  toggleNotesDock: () => set((s) => ({ notesDockVisible: !s.notesDockVisible })),
  setBackendVersion: (backendVersion) => set({ backendVersion }),
  setShellMetrics: (rawWidth) =>
    set((s) => {
      const shellWidth = Math.max(1, Math.round(rawWidth))
      const shellCapacity = shellCapacityForWidth(shellWidth)
      return {
        shellWidth,
        shellCapacity,
        ...(shellCapacity !== 'compact' ? { binderOverlayOpen: false } : {}),
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
