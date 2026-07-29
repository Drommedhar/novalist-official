import { create } from 'zustand'
import { rpc } from '../rpc/client'

export type PropertyScope = 'Scene' | 'Chapter' | 'Plotline' | 'Event' | 'Research'
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
 * The fields the writer added to scenes, chapters, plotlines, timeline events
 * and research items.
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
  /**
   * Values for one plotline, event or research item, fetched on demand. Unlike
   * scenes these are read one editor at a time, so there is nothing to gain
   * from a project-wide cache and something to lose in staleness.
   */
  valuesFor: (scope: PropertyScope, id: string) => Promise<Record<string, string>>
  setValueFor: (
    scope: PropertyScope, id: string, key: string, value: string | null
  ) => Promise<Record<string, string>>
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

  // Written out one route per scope rather than built from the scope name: a
  // method name assembled at runtime is invisible to the RPC check, and a
  // renamed backend method would then go unnoticed until someone opened the
  // editor that used it.
  valuesFor: async (scope, id) => {
    switch (scope) {
      case 'Plotline':
        return rpc.request<Record<string, string>>('manuscriptProps/plotlineValues', [id])
      case 'Event':
        return rpc.request<Record<string, string>>('manuscriptProps/eventValues', [id])
      case 'Research':
        return rpc.request<Record<string, string>>('manuscriptProps/researchValues', [id])
      case 'Chapter':
        return rpc.request<Record<string, string>>('manuscriptProps/chapterValues', [id])
      default:
        return get().sceneValues[id] ?? {}
    }
  },

  setValueFor: async (scope, id, key, value) => {
    switch (scope) {
      case 'Plotline':
        return rpc.request<Record<string, string>>(
          'manuscriptProps/setPlotlineValue', [id, key, value])
      case 'Event':
        return rpc.request<Record<string, string>>(
          'manuscriptProps/setEventValue', [id, key, value])
      case 'Research':
        return rpc.request<Record<string, string>>(
          'manuscriptProps/setResearchValue', [id, key, value])
      case 'Chapter':
        return rpc.request<Record<string, string>>(
          'manuscriptProps/setChapterValue', [id, key, value])
      default:
        await get().setSceneValue(id, key, value)
        return get().sceneValues[id] ?? {}
    }
  },

  forScene: (sceneId) => get().sceneValues[sceneId] ?? {},

  scoped: (scope) => get().definitions.filter((d) => d.scope === scope)
}))
