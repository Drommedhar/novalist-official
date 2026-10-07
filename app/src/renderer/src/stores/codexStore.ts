import { create } from 'zustand'
import { rpc } from '../rpc/client'
import { useProjectStore } from './projectStore'

export type EntityType = string

export interface EntitySummary {
  id: string
  name: string
  detail: string
  isWorldBible: boolean
  imagePath: string | null
  group: string | null
  gender: string | null
  parent: string | null
  /** True for a place that is a world: top of the tree, never inside anything. */
  isWorld?: boolean
  /** True when the entry is settled: the save path refuses writes to it. */
  locked?: boolean
}

interface CodexState {
  entityType: EntityType
  entities: EntitySummary[]
  selectedId: string | null
  selectedRecord: Record<string, unknown> | null
  restoredRevision: number
  setType(type: EntityType): Promise<void>
  refresh(): Promise<void>
  select(id: string): Promise<void>
  updateField(key: string, value: string): Promise<void>
  create(name: string, templateId?: string | null): Promise<void>
  remove(id: string, isWorldBible: boolean): Promise<void>
  moveWorldBible(id: string, toWorldBible: boolean): Promise<void>
}

let listRevision = 0
let selectionRevision = 0

function bookScope(): string {
  const { projectPath, activeBookId } = useProjectStore.getState()
  return `${projectPath ?? ''}|${activeBookId ?? ''}`
}

export const useCodexStore = create<CodexState>((set, get) => ({
  entityType: 'character',
  entities: [],
  selectedId: null,
  selectedRecord: null,
  restoredRevision: 0,

  setType: async (entityType) => {
    selectionRevision++
    set({ entityType, selectedId: null, selectedRecord: null })
    await get().refresh()
  },

  refresh: async () => {
    const revision = ++listRevision
    const entityType = get().entityType
    const scope = bookScope()
    const entities = await rpc.request<EntitySummary[]>('entities/list', [entityType])
    if (revision === listRevision && get().entityType === entityType && bookScope() === scope) {
      set({ entities })
    }
  },

  select: async (id) => {
    const revision = ++selectionRevision
    const entityType = get().entityType
    const scope = bookScope()
    const record = await rpc.request<Record<string, unknown>>('entities/get', [
      entityType,
      id
    ])
    if (revision === selectionRevision && get().entityType === entityType && bookScope() === scope) {
      set({ selectedId: id, selectedRecord: record })
    }
  },

  updateField: async (key, value) => {
    const { entityType, selectedId } = get()
    if (!selectedId) return
    const scope = bookScope()
    const record = await rpc.request<Record<string, unknown>>('entities/update', [
      entityType,
      selectedId,
      { [key]: value }
    ])
    if (get().entityType === entityType && get().selectedId === selectedId && bookScope() === scope) {
      set({ selectedRecord: record })
    }
    await get().refresh()
  },

  create: async (name, templateId = null) => {
    const revision = ++selectionRevision
    const entityType = get().entityType
    const scope = bookScope()
    const record = await rpc.request<Record<string, unknown>>('entities/create', [
      entityType,
      name,
      templateId
    ])
    await get().refresh()
    if (revision === selectionRevision && get().entityType === entityType && bookScope() === scope) {
      set({ selectedId: String(record.id), selectedRecord: record })
    }
  },

  remove: async (id, isWorldBible) => {
    await rpc.request('entities/delete', [get().entityType, id, isWorldBible])
    if (get().selectedId === id) set({ selectedId: null, selectedRecord: null })
    await get().refresh()
  },

  moveWorldBible: async (id, toWorldBible) => {
    await rpc.request(toWorldBible ? 'entities/moveToWorldBible' : 'entities/moveToBook', [
      get().entityType,
      id
    ])
    await get().refresh()
    if (get().selectedId === id) await get().select(id)
  }
}))
