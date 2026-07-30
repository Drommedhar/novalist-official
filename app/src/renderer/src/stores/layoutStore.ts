import { create } from 'zustand'
import {
  BINDER_MAX,
  BINDER_MIN,
  INSPECTOR_MAX,
  INSPECTOR_MIN,
  savePanelSize,
  useShellStore,
  type BinderTab,
  type InspectorTab,
  type MainView
} from './shellStore'

/**
 * Named workspace layouts.
 *
 * Novalist persisted exactly one panel geometry and always opened in the same
 * shape, so planning, drafting and revising meant dragging the same three
 * panels back and forth several times a day. A layout is a name for a shape:
 * save the one you are in, come back to it with one click.
 *
 * Stored in localStorage beside the panel sizes rather than through the
 * settings backend, because this is view state and follows the machine you
 * work on - the same reasoning that put the panel widths there.
 */
const STORAGE_KEY = 'nl.shell.layouts'

/** Everything a layout restores. Anything not listed here is left alone. */
export interface WorkspaceLayout {
  name: string
  mainView: MainView
  binderTab: BinderTab
  binderVisible: boolean
  binderWidth: number
  inspectorVisible: boolean
  inspectorWidth: number
  inspectorTab: InspectorTab
  notesDockVisible: boolean
  focusMode: boolean
}

function clamp(px: number, min: number, max: number): number {
  return Math.max(min, Math.min(max, Math.round(px)))
}

function read(): WorkspaceLayout[] {
  try {
    const parsed = JSON.parse(localStorage.getItem(STORAGE_KEY) || '[]')
    return Array.isArray(parsed) ? (parsed as WorkspaceLayout[]) : []
  } catch {
    return []
  }
}

function write(layouts: WorkspaceLayout[]): void {
  try {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(layouts))
  } catch {
    // Private mode or a full quota. The session still works, it just forgets -
    // which is the same deal the panel sizes already make.
  }
}

/** The shape the window is in right now, under the given name. */
export function captureLayout(name: string): WorkspaceLayout {
  const s = useShellStore.getState()
  return {
    name,
    mainView: s.mainView,
    binderTab: s.binderTab,
    binderVisible: s.binderVisible,
    binderWidth: s.binderWidth,
    inspectorVisible: s.inspectorVisible,
    inspectorWidth: s.inspectorWidth,
    inspectorTab: s.inspectorTab,
    notesDockVisible: s.notesDockVisible,
    focusMode: s.focusMode
  }
}

interface LayoutState {
  layouts: WorkspaceLayout[]
  /** Saves the current shape. A repeated name overwrites rather than doubles. */
  save(name: string): void
  apply(name: string): void
  remove(name: string): void
  reload(): void
}

export const useLayoutStore = create<LayoutState>((set, get) => ({
  layouts: read(),
  save: (name) => {
    const trimmed = name.trim()
    if (trimmed.length === 0) return
    const captured = captureLayout(trimmed)
    // Overwrite in place so an updated layout keeps its position in the list -
    // a writer who re-saves "Drafting" means that one, not a second entry.
    const next = get().layouts.some((l) => l.name === trimmed)
      ? get().layouts.map((l) => (l.name === trimmed ? captured : l))
      : [...get().layouts, captured]
    write(next)
    set({ layouts: next })
  },
  apply: (name) => {
    const layout = get().layouts.find((l) => l.name === name)
    if (!layout) return

    const binderWidth = clamp(layout.binderWidth, BINDER_MIN, BINDER_MAX)
    const inspectorWidth = clamp(layout.inspectorWidth, INSPECTOR_MIN, INSPECTOR_MAX)
    useShellStore.setState({
      mainView: layout.mainView,
      extView: null,
      binderTab: layout.binderTab,
      binderVisible: layout.binderVisible,
      binderWidth,
      inspectorVisible: layout.inspectorVisible,
      inspectorWidth,
      inspectorTab: layout.inspectorTab,
      notesDockVisible: layout.notesDockVisible,
      focusMode: layout.focusMode
    })
    // The restored widths are also the ones a restart should come back to,
    // otherwise the next launch undoes the layout that was just applied.
    savePanelSize({ binderWidth, inspectorWidth })
  },
  remove: (name) => {
    const next = get().layouts.filter((l) => l.name !== name)
    write(next)
    set({ layouts: next })
  },
  reload: () => set({ layouts: read() })
}))
