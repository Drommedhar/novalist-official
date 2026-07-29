import { create } from 'zustand'
import { rpc } from '../rpc/client'

export type PropertyScope = 'Scene' | 'Chapter'
export type PropertyType = 'String' | 'Int' | 'Bool' | 'Date' | 'Enum'

export interface ManuscriptProperty {
  key: string
  label: string
  type: PropertyType
  enumOptions: string[]
  scope: PropertyScope
  showInOutliner: boolean
}

/**
 * The fields the writer added to scenes and chapters.
 *
 * Definitions are shared by every surface that shows them - the outliner
 * columns, the scene dock, the chapter dialog - so they are loaded once here
 * rather than fetched per component.
 */
interface ManuscriptPropsState {
  definitions: ManuscriptProperty[]
  /** Every scene's values, keyed by scene id. Only scenes that have any. */
  sceneValues: Record<string, Record<string, string>>
  load: () => Promise<void>
  save: (definitions: ManuscriptProperty[]) => Promise<void>
  setSceneValue: (sceneId: string, key: string, value: string | null) => Promise<void>
  setChapterValue: (chapterGuid: string, key: string, value: string | null) => Promise<void>
  /** Values for one scene, empty when it has none. */
  forScene: (sceneId: string) => Record<string, string>
  scoped: (scope: PropertyScope) => ManuscriptProperty[]
}

export const useManuscriptPropsStore = create<ManuscriptPropsState>((set, get) => ({
  definitions: [],
  sceneValues: {},

  load: async () => {
    const [definitions, sceneValues] = await Promise.all([
      rpc.request<ManuscriptProperty[]>('manuscriptProps/definitions'),
      rpc.request<Record<string, Record<string, string>>>('manuscriptProps/allSceneValues')
    ])
    set({ definitions, sceneValues })
  },

  save: async (definitions) => {
    set({ definitions: await rpc.request<ManuscriptProperty[]>(
      'manuscriptProps/setDefinitions', [definitions]) })
    // Deleting a definition drops its values, so the cache has to be rebuilt
    // rather than kept - otherwise a removed column reappears until reload.
    set({
      sceneValues: await rpc.request<Record<string, Record<string, string>>>(
        'manuscriptProps/allSceneValues')
    })
  },

  setSceneValue: async (sceneId, key, value) => {
    const values = await rpc.request<Record<string, string>>(
      'manuscriptProps/setSceneValue', [sceneId, key, value])
    set({ sceneValues: { ...get().sceneValues, [sceneId]: values } })
  },

  setChapterValue: async (chapterGuid, key, value) => {
    await rpc.request('manuscriptProps/setChapterValue', [chapterGuid, key, value])
  },

  forScene: (sceneId) => get().sceneValues[sceneId] ?? {},

  scoped: (scope) => get().definitions.filter((d) => d.scope === scope)
}))
