import { create } from 'zustand'
import type { RecentProjectDto } from './projectStore'

export interface ProjectShelf { id: string; name: string; projects: string[] }
interface Library { shelves: ProjectShelf[]; unshelved: string[]; shelfOrder: string[] }
const KEY = 'nl.projectLibrary.v1'
// A copied project can retain its internal id. Its folder is the library entry.
export const projectKey = (project: RecentProjectDto): string => project.path

/** The empty id represents My projects, which can move but cannot be removed. */
function shelfOrder(shelves: ProjectShelf[], saved: unknown): string[] {
  const ids = new Set(['', ...shelves.map((shelf) => shelf.id)])
  const order = Array.isArray(saved) ? saved.filter((id) => ids.has(id)) : []
  return [...new Set([...order, ...ids])]
}

function readLibrary(): Library {
  try {
    const parsed = JSON.parse(localStorage.getItem(KEY) ?? '{}')
    const shelves = Array.isArray(parsed.shelves) ? parsed.shelves.filter((s: ProjectShelf) =>
      s && typeof s.id === 'string' && typeof s.name === 'string' && Array.isArray(s.projects)
    ).map((s: ProjectShelf) => ({ ...s, projects: s.projects.filter((id) => typeof id === 'string') })) : []
    return { shelves, unshelved: Array.isArray(parsed.unshelved) ? parsed.unshelved.filter((id: unknown) => typeof id === 'string') : [], shelfOrder: shelfOrder(shelves, parsed.shelfOrder) }
  } catch { return { shelves: [], unshelved: [], shelfOrder: [''] } }
}

interface BookshelfState extends Library {
  registerProjects(keys: string[]): void
  addShelf(name: string): void
  renameShelf(id: string, name: string): void
  removeShelf(id: string): void
  moveShelf(id: string, delta: number): void
  moveProject(key: string, shelfId: string, before?: string): void
}

/** Shelves arrange projects; they never move or modify project folders. */
export const useBookshelfStore = create<BookshelfState>((set, get) => {
  const save = (library: Omit<Library, 'shelfOrder'> & { shelfOrder?: string[] }): void => {
    const next = { ...library, shelfOrder: shelfOrder(library.shelves, library.shelfOrder ?? get().shelfOrder) }
    localStorage.setItem(KEY, JSON.stringify(next))
    set(next)
  }
  return {
    ...readLibrary(),
    registerProjects: (keys) => {
      const known = new Set([...get().unshelved, ...get().shelves.flatMap((s) => s.projects)])
      const added = keys.filter((key) => !known.has(key))
      if (added.length) save({ shelves: get().shelves, unshelved: [...get().unshelved, ...added] })
    },
    addShelf: (name) => {
      if (!name.trim()) return
      save({ shelves: [...get().shelves, { id: crypto.randomUUID(), name: name.trim(), projects: [] }], unshelved: get().unshelved })
    },
    renameShelf: (id, name) => {
      if (!name.trim()) return
      save({ shelves: get().shelves.map((s) => s.id === id ? { ...s, name: name.trim() } : s), unshelved: get().unshelved })
    },
    removeShelf: (id) => save({
      shelves: get().shelves.filter((s) => s.id !== id),
      unshelved: [...get().unshelved, ...(get().shelves.find((s) => s.id === id)?.projects ?? [])]
    }),
    moveShelf: (id, delta) => {
      const order = [...get().shelfOrder]
      const index = order.indexOf(id)
      const target = index + delta
      if (index < 0 || target < 0 || target >= order.length) return
      ;[order[index], order[target]] = [order[target], order[index]]
      save({ shelves: get().shelves, unshelved: get().unshelved, shelfOrder: order })
    },
    moveProject: (key, shelfId, before) => {
      if (key === before) return
      const shelves = get().shelves.map((s) => ({ ...s, projects: s.projects.filter((id) => id !== key) }))
      const unshelved = get().unshelved.filter((id) => id !== key)
      const target = shelves.find((s) => s.id === shelfId)?.projects ?? unshelved
      const index = before ? target.indexOf(before) : -1
      target.splice(index < 0 ? target.length : index, 0, key)
      save({ shelves, unshelved })
    }
  }
})
