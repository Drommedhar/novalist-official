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
  | 'codex'
  | 'wiki'
  | 'maps'
  | 'research'
  | 'gallery'
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
  { key: 'shell.groupPlan', views: ['timeline', 'plotGrid', 'calendar', 'relationships'] },
  { key: 'shell.groupWorld', views: ['codex', 'wiki', 'maps', 'research', 'gallery'] },
  { key: 'shell.groupPublish', views: ['export', 'git'] }
]

export type BinderTab = 'chapters' | 'smartLists'

export interface ActiveExtView {
  extensionId: string
  key: string
}

/** Right context sidebar tabs, mirroring the desktop Context / Footnotes tabs. */
export type InspectorTab = 'context' | 'footnotes'

/** Active destination in the mobile bottom (native Liquid Glass) tab bar. */
export type MobileTab = 'dashboard' | 'manuscript' | 'codex' | 'planning' | 'settings'

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
  commandPaletteOpen: boolean
  /** In-app user-manual help viewer overlay. */
  helpOpen: boolean
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
  setCommandPaletteOpen(open: boolean): void
  setHelpOpen(open: boolean): void
}

export const useShellStore = create<ShellState>((set) => ({
  mainView: 'write',
  mobileTab: 'dashboard',
  extView: null,
  binderTab: 'chapters',
  binderVisible: true,
  binderWidth: 240,
  inspectorVisible: true,
  inspectorWidth: 280,
  inspectorTab: 'context',
  notesDockVisible: false,
  settingsSearch: '',
  pendingMapNav: null,
  backendVersion: null,
  focusMode: false,
  findReplaceOpen: false,
  commandPaletteOpen: false,
  helpOpen: false,
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
  setCommandPaletteOpen: (commandPaletteOpen) => set({ commandPaletteOpen }),
  setHelpOpen: (helpOpen) => set({ helpOpen }),
  toggleBinder: () => set((s) => ({ binderVisible: !s.binderVisible })),
  setBinderWidth: (px) => set({ binderWidth: Math.max(180, Math.min(500, px)) }),
  toggleInspector: () => set((s) => ({ inspectorVisible: !s.inspectorVisible })),
  setInspectorWidth: (px) => set({ inspectorWidth: Math.max(220, Math.min(520, px)) }),
  setInspectorTab: (inspectorTab) => set({ inspectorTab }),
  toggleNotesDock: () => set((s) => ({ notesDockVisible: !s.notesDockVisible })),
  setBackendVersion: (backendVersion) => set({ backendVersion })
}))
