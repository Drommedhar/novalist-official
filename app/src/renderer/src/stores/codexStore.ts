import { create } from 'zustand'
import { rpc } from '../rpc/client'

export type EntityType = 'character' | 'location' | 'item' | 'lore'

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
  }
}))
