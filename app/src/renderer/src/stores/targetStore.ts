import { create } from 'zustand'
import { rpc } from '../rpc/client'

export interface WordTarget {
  kind: 'scene' | 'chapter' | 'act'
  id: string
  title: string
  words: number
  target: number
  /** True when the writer set the target here; false when it came from below. */
  explicit: boolean
  remaining: number
  overrun: number
}

/**
 * Word targets for every part of the book that has one.
 *
 * Held as one flat list keyed by kind and id, because the binder needs a bar
 * per row and looking each one up individually would be a round trip per scene.
 */
interface TargetState {
  targets: WordTarget[]
  load: () => Promise<void>
  setScene: (chapterGuid: string, sceneId: string, target: number | null) => Promise<void>
  setChapter: (chapterGuid: string, target: number | null) => Promise<void>
  setAct: (actName: string, target: number | null) => Promise<void>
  find: (kind: WordTarget['kind'], id: string) => WordTarget | undefined
}

export const useTargetStore = create<TargetState>((set, get) => ({
  targets: [],

  load: async () => {
    set({ targets: await rpc.request<WordTarget[]>('targets/all') })
  },

  setScene: async (chapterGuid, sceneId, target) => {
    set({ targets: await rpc.request<WordTarget[]>('targets/setScene', [chapterGuid, sceneId, target]) })
  },

  setChapter: async (chapterGuid, target) => {
    set({ targets: await rpc.request<WordTarget[]>('targets/setChapter', [chapterGuid, target]) })
  },

  setAct: async (actName, target) => {
    set({ targets: await rpc.request<WordTarget[]>('targets/setAct', [actName, target]) })
  },

  find: (kind, id) => get().targets.find((t) => t.kind === kind && t.id === id)
}))
