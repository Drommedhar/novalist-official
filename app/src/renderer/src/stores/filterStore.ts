import { create } from 'zustand'

/**
 * One filter model, shared live by the views that narrow the book.
 *
 * The Manuscript status filter, the Timeline's character and location filters
 * and the Plot Grid's own were three unrelated pieces of local state. Narrowing
 * to one character meant setting it again in each view, and the setting was
 * gone the moment you navigated away — so a revision pass over one thread was
 * re-typed at every step, and none of it could be saved.
 */
export interface ProjectFilter {
  /** Chapter status, or '' for every status. */
  status: string
  /** A Codex entry id the scene or event must involve, or ''. */
  character: string
  location: string
  /** A plotline id, or ''. */
  plotline: string
  /** A scene stage key, or ''. */
  stage: string
}

export const EMPTY_FILTER: ProjectFilter = {
  status: '',
  character: '',
  location: '',
  plotline: '',
  stage: ''
}

/** A filter the writer named and can come back to. */
export interface FilterPreset {
  name: string
  filter: ProjectFilter
}

/**
 * Presets live beside the panel geometry rather than in the project file.
 *
 * A filter is a way of looking at the book, not part of it — the same
 * reasoning that put workspace layouts and panel widths in local storage. They
 * are keyed by project so two books do not share one set.
 */
function storageKey(projectPath: string | null): string {
  return `nl.filters.${projectPath ?? 'none'}`
}

function read(projectPath: string | null): FilterPreset[] {
  try {
    const parsed = JSON.parse(localStorage.getItem(storageKey(projectPath)) || '[]')
    return Array.isArray(parsed) ? (parsed as FilterPreset[]) : []
  } catch {
    return []
  }
}

function write(projectPath: string | null, presets: FilterPreset[]): void {
  try {
    localStorage.setItem(storageKey(projectPath), JSON.stringify(presets))
  } catch {
    // Private mode or a full quota. The session still filters, it just forgets.
  }
}

/** True when nothing is being narrowed, so a view can skip the work entirely. */
export function isEmptyFilter(filter: ProjectFilter): boolean {
  return (
    filter.status === '' &&
    filter.character === '' &&
    filter.location === '' &&
    filter.plotline === '' &&
    filter.stage === ''
  )
}

/** How many things the filter is narrowing by, for the badge on the button. */
export function activeCount(filter: ProjectFilter): number {
  return Object.values(filter).filter((v) => v !== '').length
}

interface FilterState {
  filter: ProjectFilter
  presets: FilterPreset[]
  /** The project the presets were loaded for, so a switch reloads them. */
  projectPath: string | null
  set(patch: Partial<ProjectFilter>): void
  clear(): void
  loadPresets(projectPath: string | null): void
  save(name: string): void
  apply(name: string): void
  remove(name: string): void
}

export const useFilterStore = create<FilterState>((set, get) => ({
  filter: { ...EMPTY_FILTER },
  presets: [],
  projectPath: null,

  set: (patch) => set((s) => ({ filter: { ...s.filter, ...patch } })),
  clear: () => set({ filter: { ...EMPTY_FILTER } }),

  loadPresets: (projectPath) => {
    // Only when the project actually changed. Every view that shows the bar
    // calls this on mount, and resetting there would clear the filter the
    // moment the writer navigated - which is the exact problem being fixed.
    if (projectPath === get().projectPath) return;
    // Switching project does drop the filter as well as the presets: narrowing
    // to a character in one book means nothing in the next, and carrying it
    // over would silently hide most of the new book.
    set({ projectPath, presets: read(projectPath), filter: { ...EMPTY_FILTER } })
  },

  save: (name) => {
    const trimmed = name.trim()
    if (trimmed.length === 0) return
    const preset: FilterPreset = { name: trimmed, filter: { ...get().filter } }
    // Overwrite in place so a re-saved preset keeps its position; a writer who
    // saves "Mira's thread" twice means that one, not a second entry.
    const next = get().presets.some((p) => p.name === trimmed)
      ? get().presets.map((p) => (p.name === trimmed ? preset : p))
      : [...get().presets, preset]
    write(get().projectPath, next)
    set({ presets: next })
  },

  apply: (name) => {
    const preset = get().presets.find((p) => p.name === name)
    if (!preset) return
    // Spread over the empty filter so a preset written before a field existed
    // clears that field rather than leaving whatever was set.
    set({ filter: { ...EMPTY_FILTER, ...preset.filter } })
  },

  remove: (name) => {
    const next = get().presets.filter((p) => p.name !== name)
    write(get().projectPath, next)
    set({ presets: next })
  }
}))
