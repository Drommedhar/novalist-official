import { create } from 'zustand'
import { rpc } from '../rpc/client'
import { useProjectStore, type ProjectStateDto } from './projectStore'

export interface ManuscriptSceneDto {
  sceneId: string
  title: string
  html: string
  wordCount: number
  synopsis: string | null
  pov: string | null
}

export interface ManuscriptSectionDto {
  chapterGuid: string
  chapterTitle: string
  status: string
  act: string
  scenes: ManuscriptSceneDto[]
}

export type ManuscriptMode = 'manuscript' | 'corkboard' | 'outliner' | 'board'

// Matches the Avalonia ManuscriptViewModel autosave debounce.
const MANUSCRIPT_AUTOSAVE_MS = 800

const saveTimers = new Map<string, ReturnType<typeof setTimeout>>()

interface ManuscriptState {
  mode: ManuscriptMode
  /** What the board groups by: 'chapter', 'stage', 'pov', or 'prop:<key>'. */
  groupBy: string
  filterStatus: string
  sections: ManuscriptSectionDto[]
  loaded: boolean
  setMode(mode: ManuscriptMode): void
  setGroupBy(groupBy: string): void
  setFilter(status: string): Promise<void>
  load(): Promise<void>
  onSceneContentChanged(sceneId: string, html: string, plainText: string, wordCount: number): void
  cycleStatus(chapterGuid: string): Promise<void>
  setSynopsis(chapterGuid: string, sceneId: string, synopsis: string): Promise<void>
  setPov(chapterGuid: string, sceneId: string, pov: string): Promise<void>
}

export const useManuscriptStore = create<ManuscriptState>((set, get) => ({
  mode: 'manuscript',
  groupBy: 'stage',
  filterStatus: 'All',
  sections: [],
  loaded: false,

  setMode: (mode) => set({ mode }),

  setGroupBy: (groupBy) => set({ groupBy }),

  setFilter: async (filterStatus) => {
    set({ filterStatus })
    await get().load()
  },

  load: async () => {
    const sections = await rpc.request<ManuscriptSectionDto[]>('manuscript/get', [
      get().filterStatus
    ])
    set({ sections, loaded: true })
  },

  onSceneContentChanged: (sceneId, html, plainText, wordCount) => {
    const section = get().sections.find((s) => s.scenes.some((sc) => sc.sceneId === sceneId))
    if (!section) return
    set({
      sections: get().sections.map((s) => ({
        ...s,
        scenes: s.scenes.map((sc) => (sc.sceneId === sceneId ? { ...sc, html, wordCount } : sc))
      }))
    })
    const existing = saveTimers.get(sceneId)
    if (existing) clearTimeout(existing)
    saveTimers.set(
      sceneId,
      setTimeout(() => {
        saveTimers.delete(sceneId)
        void rpc.request('scenes/write', [section.chapterGuid, sceneId, html, plainText])
      }, MANUSCRIPT_AUTOSAVE_MS)
    )
  },

  cycleStatus: async (chapterGuid) => {
    const cycle = ['Outline', 'FirstDraft', 'Revised', 'Edited', 'Final']
    const section = get().sections.find((s) => s.chapterGuid === chapterGuid)
    if (!section) return
    const next = cycle[(cycle.indexOf(section.status) + 1) % cycle.length]
    const state = await rpc.request<ProjectStateDto>('project/setChapterStatus', [
      chapterGuid,
      next
    ])
    useProjectStore.getState().applyState(state)
    await get().load()
  },

  setSynopsis: async (chapterGuid, sceneId, synopsis) => {
    await rpc.request('scenes/setSynopsis', [chapterGuid, sceneId, synopsis])
    set({
      sections: get().sections.map((s) =>
        s.chapterGuid === chapterGuid
          ? {
              ...s,
              scenes: s.scenes.map((sc) =>
                sc.sceneId === sceneId ? { ...sc, synopsis: synopsis || null } : sc
              )
            }
          : s
      )
    })
  },

  setPov: async (chapterGuid, sceneId, pov) => {
    await rpc.request('scenes/setPov', [chapterGuid, sceneId, pov])
    set({
      sections: get().sections.map((s) =>
        s.chapterGuid === chapterGuid
          ? {
              ...s,
              scenes: s.scenes.map((sc) => (sc.sceneId === sceneId ? { ...sc, pov: pov || null } : sc))
            }
          : s
      )
    })
  }
}))
