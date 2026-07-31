import { create } from 'zustand'

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

/* Measured against the display rather than the window. This module is evaluated
 * during renderer boot, which races the main process maximising the window, so
 * innerWidth here is still the constructor's 1440 and would size the panels for
 * a window the user never sees. availWidth is stable whenever it is read. */
const screenW = typeof window === 'undefined' ? 1440 : window.screen?.availWidth || window.innerWidth
const screenH = typeof window === 'undefined' ? 900 : window.screen?.availHeight || window.innerHeight

export const NOTES_DOCK_DEFAULT = initialPanelSize(
  storedPanels.notesDockHeight,
  0.16,
  NOTES_DOCK_MIN,
  NOTES_DOCK_MAX,
  screenH
)

interface ShellState {
  mainView: MainView
  mobileTab: MobileTab
  extView: ActiveExtView | null
  binderTab: BinderTab
  binderVisible: boolean
  binderWidth: number
  inspectorVisible: boolean
  inspectorWidth: number
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
  /** In-app user-manual help viewer overlay. */
  helpOpen: boolean
  /** Named workspace layouts: save the shape you are in, come back to it. */
  layoutsOpen: boolean
  /** The short walk through the views, offered once per installation. */
  tourOpen: boolean
  setMainView(view: MainView): void
  setMobileTab(tab: MobileTab): void
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
  setHelpOpen(open: boolean): void
  setLayoutsOpen(open: boolean): void
  setTourOpen(open: boolean): void
}

export const useShellStore = create<ShellState>((set) => ({
  mainView: 'write',
  mobileTab: 'dashboard',
  extView: null,
  binderTab: 'chapters',
  binderVisible: true,
  binderWidth: initialPanelSize(storedPanels.binderWidth, 0.15, BINDER_MIN, BINDER_MAX, screenW),
  inspectorVisible: true,
  inspectorWidth: initialPanelSize(
    storedPanels.inspectorWidth,
    0.18,
    INSPECTOR_MIN,
    INSPECTOR_MAX,
    screenW
  ),
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
  helpOpen: false,
  layoutsOpen: false,
  tourOpen: false,
  setMainView: (mainView) => set({ mainView, extView: null }),
  setMobileTab: (mobileTab) => set({ mobileTab }),
  navigateToMapPin: (mapId, pinId) =>
    set({ mainView: 'maps', extView: null, pendingMapNav: { mapId, pinId } }),
  clearPendingMapNav: () => set({ pendingMapNav: null }),
  openSettings: (search = '') => set({ mainView: 'settings', extView: null, settingsSearch: search }),
  setExtView: (extView) => set({ extView }),
  setBinderTab: (binderTab) => set({ binderTab }),
  toggleFocusMode: () => set((s) => ({ focusMode: !s.focusMode })),
  setFindReplaceOpen: (findReplaceOpen) => set({ findReplaceOpen }),
  setCleanupOpen: (cleanupOpen) => set({ cleanupOpen }),
  setCommandPaletteOpen: (commandPaletteOpen) => set({ commandPaletteOpen }),
  setQuickOpenOpen: (quickOpenOpen) => set({ quickOpenOpen }),
  setQuickCaptureOpen: (quickCaptureOpen) => set({ quickCaptureOpen }),
  navigateToResearch: (itemId) =>
    set({ mainView: 'research', extView: null, pendingResearchId: itemId }),
  clearPendingResearch: () => set({ pendingResearchId: null }),
  setHelpOpen: (helpOpen) => set({ helpOpen }),
  setLayoutsOpen: (layoutsOpen) => set({ layoutsOpen }),
  setTourOpen: (tourOpen) => set({ tourOpen }),
  toggleBinder: () => set((s) => ({ binderVisible: !s.binderVisible })),
  setBinderWidth: (px) => set({ binderWidth: clamp(px, BINDER_MIN, BINDER_MAX) }),
  toggleInspector: () => set((s) => ({ inspectorVisible: !s.inspectorVisible })),
  setInspectorWidth: (px) => set({ inspectorWidth: clamp(px, INSPECTOR_MIN, INSPECTOR_MAX) }),
  setInspectorTab: (inspectorTab) => set({ inspectorTab }),
  toggleNotesDock: () => set((s) => ({ notesDockVisible: !s.notesDockVisible })),
  setBackendVersion: (backendVersion) => set({ backendVersion })
}))
