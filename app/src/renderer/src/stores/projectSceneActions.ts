import { rpc } from '../rpc/client'
import { flushPendingWrites } from './pendingWrites'
import i18n from '../i18n'
import { type ProjectStateDto, type ProjectSlice } from './projectTypes'
import { useProjectStore, reconcileSceneEditors } from './projectStore'

let pendingSceneContexts = 0

export async function runSceneContext(action: () => Promise<void>): Promise<void> {
  ensureSceneStructureReady()
  pendingSceneContexts++
  try {
    await action()
  } finally {
    pendingSceneContexts--
  }
}

function ensureSceneStructureReady(): void {
  if (useProjectStore.getState().changingSceneStructure || useProjectStore.getState().workspaceBusy) {
    throw new Error(i18n.t('update.workspaceBusy'))
  }
}

export const createProjectSceneActions: ProjectSlice<'createChapter' | 'createScene' | 'renameChapter' | 'renameScene' | 'deleteChapter' | 'deleteScene' | 'setChapterStatus' | 'setChapterAct' | 'reorderChapter' | 'reorderScene' | 'moveScenes' | 'mutateSceneStructure'> = (set, get) => ({
  createChapter: async (title, insertAtOrder) => {
    const state = await rpc.request<ProjectStateDto>('project/createChapter', [
      title,
      insertAtOrder ?? null
    ])
    get().applyState(state)
  },

  createScene: async (chapterGuid, title) => {
    const state = await rpc.request<ProjectStateDto>('project/createScene', [chapterGuid, title])
    get().applyState(state)
  },

  renameChapter: async (chapterGuid, title) => {
    get().applyState(
      await rpc.request<ProjectStateDto>('project/renameChapter', [chapterGuid, title])
    )
  },

  renameScene: async (chapterGuid, sceneId, title) => {
    get().applyState(
      await rpc.request<ProjectStateDto>('project/renameScene', [chapterGuid, sceneId, title])
    )
  },

  deleteChapter: async (chapterGuid) => {
    await get().mutateSceneStructure('project/deleteChapter', [chapterGuid])
  },

  deleteScene: async (chapterGuid, sceneId) => {
    await get().mutateSceneStructure('project/deleteScene', [chapterGuid, sceneId])
  },

  setChapterStatus: async (chapterGuid, status) => {
    get().applyState(
      await rpc.request<ProjectStateDto>('project/setChapterStatus', [chapterGuid, status])
    )
  },

  setChapterAct: async (chapterGuid, act) => {
    get().applyState(
      await rpc.request<ProjectStateDto>('project/setChapterAct', [chapterGuid, act])
    )
  },

  reorderChapter: async (chapterGuid, newOrder) => {
    get().applyState(
      await rpc.request<ProjectStateDto>('project/reorderChapter', [chapterGuid, newOrder])
    )
  },

  reorderScene: async (chapterGuid, sceneId, newOrder) => {
    get().applyState(
      await rpc.request<ProjectStateDto>('project/reorderScene', [chapterGuid, sceneId, newOrder])
    )
  },

  moveScenes: async (sceneIds, targetChapterGuid, targetIndex) => {
    await get().mutateSceneStructure('project/moveScenes', [sceneIds, targetChapterGuid, targetIndex])
  },

  mutateSceneStructure: async (method, args) => {
    ensureSceneStructureReady()
    if (pendingSceneContexts > 0) throw new Error(i18n.t('update.workspaceBusy'))
    set({ changingSceneStructure: true })
    try {
      // Archive/delete must capture the live prose before moving its file;
      // a moved scene must finish writes that still address its old chapter.
      await flushPendingWrites()
      await get().flushPendingSave()
      const { useManuscriptStore } = await import('./manuscriptStore')
      await useManuscriptStore.getState().flushPendingSave()
      if (get().sceneConflict || Object.values(get().dirtyMap).some(Boolean)) {
        throw new Error(i18n.t('update.sceneWriteConflict'))
      }
      const result = await rpc.request<ProjectStateDto | { state: ProjectStateDto }>(method, args)
      const state = 'state' in result ? result.state : result
      // Only retire ownership after the backend accepted the operation. A
      // failed archive leaves every tab and its prose available for retry.
      get().applyState(state)
      set((current) => reconcileSceneEditors(current, state.chapters))
      useManuscriptStore.getState().reconcileSceneStructure(state.chapters)
    } catch (error) {
      const { useHostBridgeStore } = await import('./hostBridgeStore')
      const message = error instanceof Error ? error.message : String(error)
      useHostBridgeStore.getState().pushToast(i18n.t('toast.saveFailed').replace('{0}', message))
      throw error
    } finally {
      set({ changingSceneStructure: false })
    }
  }
})
