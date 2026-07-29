import { create } from 'zustand'
import { rpc } from '../rpc/client'
import { useProjectStore, type ProjectStateDto } from './projectStore'

export interface SceneStage {
  key: string
  label: string
  color: string
  countsAsWritten: boolean
}

/**
 * The revision stages this book's scenes can be at.
 *
 * Loaded once per project rather than per binder row: the binder paints a dot
 * for every scene, and asking the backend for the stage list per row would be
 * one round trip per scene in a book with hundreds of them.
 */
interface StageState {
  stages: SceneStage[]
  load: () => Promise<void>
  save: (stages: SceneStage[]) => Promise<void>
  setSceneStage: (chapterGuid: string, sceneId: string, key: string | null) => Promise<void>
  /** The stage a scene is at, or undefined when it has none or the key is stale. */
  find: (key: string | null) => SceneStage | undefined
}

export const useStageStore = create<StageState>((set, get) => ({
  stages: [],

  load: async () => {
    set({ stages: await rpc.request<SceneStage[]>('stages/list') })
  },

  save: async (stages) => {
    set({ stages: await rpc.request<SceneStage[]>('stages/set', [stages]) })
    // A removed stage leaves scenes pointing at a key that no longer resolves,
    // so the binder has to repaint from fresh state rather than its own copy.
    useProjectStore.getState().applyState(await rpc.request<ProjectStateDto>('project/getState'))
  },

  setSceneStage: async (chapterGuid, sceneId, key) => {
    const state = await rpc.request<ProjectStateDto>('stages/setSceneStage', [
      chapterGuid,
      sceneId,
      key
    ])
    useProjectStore.getState().applyState(state)
  },

  find: (key) => (key ? get().stages.find((s) => s.key === key) : undefined)
}))
