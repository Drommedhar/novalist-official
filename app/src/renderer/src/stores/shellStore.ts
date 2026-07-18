import { create } from 'zustand'

/** Main-area destinations, grouped for the binder-rail view switcher. */
export type MainView =
  | 'write'
  | 'dashboard'
  | 'manuscript'
  | 'timeline'
  | 'plotGrid'
  | 'calendar'
  | 'relationships'
  | 'codex'
  | 'maps'
  | 'research'
  | 'gallery'
  | 'export'
  | 'git'

export const viewGroups: { key: string; views: MainView[] }[] = [
  { key: 'shell.groupWrite', views: ['write', 'manuscript', 'dashboard'] },
  { key: 'shell.groupPlan', views: ['timeline', 'plotGrid', 'calendar', 'relationships'] },
  { key: 'shell.groupWorld', views: ['codex', 'maps', 'research', 'gallery'] },
  { key: 'shell.groupPublish', views: ['export', 'git'] }
]

export type BinderTab = 'chapters' | 'smartLists'

interface ShellState {
  mainView: MainView
  binderTab: BinderTab
  binderVisible: boolean
  inspectorVisible: boolean
  backendVersion: string | null
  findReplaceOpen: boolean
  commandPaletteOpen: boolean
  setMainView(view: MainView): void
  setBinderTab(tab: BinderTab): void
  toggleBinder(): void
  toggleInspector(): void
  setBackendVersion(version: string | null): void
  setFindReplaceOpen(open: boolean): void
  setCommandPaletteOpen(open: boolean): void
}

export const useShellStore = create<ShellState>((set) => ({
  mainView: 'write',
  binderTab: 'chapters',
  binderVisible: true,
  inspectorVisible: true,
  backendVersion: null,
  findReplaceOpen: false,
  commandPaletteOpen: false,
  setMainView: (mainView) => set({ mainView }),
  setBinderTab: (binderTab) => set({ binderTab }),
  setFindReplaceOpen: (findReplaceOpen) => set({ findReplaceOpen }),
  setCommandPaletteOpen: (commandPaletteOpen) => set({ commandPaletteOpen }),
  toggleBinder: () => set((s) => ({ binderVisible: !s.binderVisible })),
  toggleInspector: () => set((s) => ({ inspectorVisible: !s.inspectorVisible })),
  setBackendVersion: (backendVersion) => set({ backendVersion })
}))
