import { create } from 'zustand'
import { rpc } from '../rpc/client'

export type EntityType = string

export interface EntitySummary {
  id: string
  name: string
  detail: string
  isWorldBible: boolean
  imagePath: string | null
}

interface CodexState {
  entityType: EntityType
  entities: EntitySummary[]
  selectedId: string | null
  selectedRecord: Record<string, unknown> | null
  setType(type: EntityType): Promise<void>
  refresh(): Promise<void>
  select(id: string): Promise<void>
  updateField(key: string, value: string): Promise<void>
  create(name: string, templateId?: string | null): Promise<void>
  remove(id: string, isWorldBible: boolean): Promise<void>
}

export const useCodexStore = create<CodexState>((set, get) => ({
  entityType: 'character',
  entities: [],
  selectedId: null,
  selectedRecord: null,

  setType: async (entityType) => {
    set({ entityType, selectedId: null, selectedRecord: null })
    await get().refresh()
  },

  refresh: async () => {
    const entities = await rpc.request<EntitySummary[]>('entities/list', [get().entityType])
    set({ entities })
  },

  select: async (id) => {
    const record = await rpc.request<Record<string, unknown>>('entities/get', [
      get().entityType,
      id
    ])
    set({ selectedId: id, selectedRecord: record })
  },

  updateField: async (key, value) => {
    const { entityType, selectedId } = get()
    if (!selectedId) return
    const record = await rpc.request<Record<string, unknown>>('entities/update', [
      entityType,
      selectedId,
      { [key]: value }
    ])
    set({ selectedRecord: record })
    await get().refresh()
  },

  create: async (name, templateId = null) => {
    const record = await rpc.request<Record<string, unknown>>('entities/create', [
      get().entityType,
      name,
      templateId
    ])
    await get().refresh()
    set({ selectedId: String(record.id), selectedRecord: record })
  },

  remove: async (id, isWorldBible) => {
    await rpc.request('entities/delete', [get().entityType, id, isWorldBible])
    if (get().selectedId === id) set({ selectedId: null, selectedRecord: null })
    await get().refresh()
  }
}))
