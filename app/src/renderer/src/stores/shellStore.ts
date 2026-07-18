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

interface ShellState {
  mainView: MainView
  binderVisible: boolean
  inspectorVisible: boolean
  backendVersion: string | null
  setMainView(view: MainView): void
  toggleBinder(): void
  toggleInspector(): void
  setBackendVersion(version: string | null): void
}

export const useShellStore = create<ShellState>((set) => ({
  mainView: 'write',
  binderVisible: true,
  inspectorVisible: true,
  backendVersion: null,
  setMainView: (mainView) => set({ mainView }),
  toggleBinder: () => set((s) => ({ binderVisible: !s.binderVisible })),
  toggleInspector: () => set((s) => ({ inspectorVisible: !s.inspectorVisible })),
  setBackendVersion: (backendVersion) => set({ backendVersion })
}))
