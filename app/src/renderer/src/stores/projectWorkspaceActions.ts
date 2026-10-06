import { rpc } from '../rpc/client'
import { useShellStore } from './shellStore'
import { useSettingsStore } from './settingsStore'
import { useCodexStore } from './codexStore'
import { flushPendingWrites } from './pendingWrites'
import i18n from '../i18n'
import { type ProjectStateDto, type ProjectState, type ProjectSlice } from './projectTypes'
import { autosaveTimers, stripHtml } from './projectPersistence'
import { runSceneContext } from './projectSceneActions'
import { clearedEditorState, mirror, mapEditors, reconcileSceneEditors } from './projectStore'

export const createProjectWorkspaceActions: ProjectSlice<'applyWorkspaceSnapshot' | 'refreshWorkspaceEditors' | 'applyState' | 'switchBook' | 'createBook' | 'loadDrafts' | 'createDraft' | 'switchDraft' | 'deleteDraft'> = (set, get) => ({
  applyWorkspaceSnapshot: (snapshot, resetEditors = false) => {
    const state = snapshot.state as ProjectStateDto
    const current = get()
    const changed = resetEditors || current.projectPath !== state.projectPath || current.activeBookId !== state.activeBookId || current.activeDraftId !== snapshot.draftId
    get().applyState(state, changed, true, true)
    set((current) => ({
      workspaceEpoch: snapshot.epoch,
      activeDraftId: snapshot.draftId,
      ...(!changed ? reconcileSceneEditors(current, state.chapters) : {})
    }))
  },

  refreshWorkspaceEditors: async (preserveDirty = false) => {
    const epoch = get().workspaceEpoch
    const isDirty = (state: ProjectState, id: string): boolean => !!state.dirtyMap[id] || Object.values(state.editors).some((editor) => editor.sceneId === id && editor.isDirty)
    const scenes = new Map(Object.values(get().editors).filter((editor) => !preserveDirty || !editor.sceneId || !isDirty(get(), editor.sceneId)).flatMap((editor) =>
      editor.chapterGuid && editor.sceneId ? [[editor.sceneId, { chapterGuid: editor.chapterGuid, sceneId: editor.sceneId }] as const] : []))
    // These are deliberately fresh reads. A coalesced pre-change read could
    // be waiting behind the original RPC's all-window resume barrier.
    const contents = new Map(await Promise.all([...scenes.values()].map(async (scene) => {
      const content = await rpc.request<{ html: string; hash: string }>('scenes/read', [scene.chapterGuid, scene.sceneId])
      return [scene.sceneId, content] as const
    })))
    if (get().workspaceEpoch !== epoch) return
    set((state) => {
      const editors = mapEditors(state.editors, (editor) => {
        const content = editor.sceneId ? contents.get(editor.sceneId) : null
        return content && (!preserveDirty || !isDirty(state, editor.sceneId!)) ? { ...editor, html: content.html, plainText: stripHtml(content.html), isDirty: false } : editor
      })
      return { editors, ...mirror(editors, state.activeEditorPaneId), sceneHashes: {
        ...state.sceneHashes, ...Object.fromEntries([...contents].filter(([id]) => !preserveDirty || !isDirty(state, id)).map(([id, content]) => [id, content.hash]))
      } }
    })
  },

  applyState: (state, resetEditors = false, applicationStateReady = false, deferHydration = false) => {
    deferHydration ||= get().workspaceBusy
    const prevPath = get().projectPath
    const prevBookId = get().activeBookId
    const prevName = get().projectName
    const projectChanged = state.projectPath !== prevPath
    if (projectChanged || resetEditors) {
      useShellStore.setState({ focusMode: false, focusPaneId: null, focusPanel: null, focusPanelTransient: false, focusToolsVisible: false })
      // All create/open/close paths meet here. Pane state survives ordinary
      // view navigation, but must never survive a project change or restore.
      for (const timer of autosaveTimers.values()) clearTimeout(timer)
      autosaveTimers.clear()
    }
    set({
      ...(projectChanged || resetEditors ? { ...clearedEditorState(), drafts: [] } : {}),
      isLoaded: state.isLoaded,
      projectName: state.projectName,
      projectPath: state.projectPath,
      activeBookId: state.activeBookId,
      books: state.books,
      chapters: state.chapters
    })
    window.novalist.setProjectRoot(state.projectPath)
    if (!deferHydration && !applicationStateReady && (projectChanged || resetEditors || state.projectName !== prevName)) void get().loadRecents().catch(() => {})
    if (!deferHydration && state.isLoaded) void get().loadDrafts().catch(() => {})
    // The effective language/theme can carry a per-project override, so re-apply
    // settings whenever the active project changes - otherwise a project opened
    // with a non-default language stays on the global language until Settings is
    // opened (which reloads settings as a side effect).
    if (!deferHydration && !applicationStateReady && (projectChanged || resetEditors)) void useSettingsStore.getState().load().catch(() => {})
    // The Codex is the active book's, and its entry count is shown outside the
    // Codex view, so it cannot wait for that view to be mounted again. Anything
    // selected belonged to the book being left, so the selection goes with it.
    if (state.activeBookId !== prevBookId || resetEditors) {
      useCodexStore.setState({ selectedId: null, selectedRecord: null })
      if (!deferHydration && state.isLoaded) void useCodexStore.getState().refresh().catch(() => {})
    }
  },

  switchBook: (bookId) => runSceneContext(async () => {
    await flushPendingWrites()
    await get().flushPendingSave()
    get().applyState(await rpc.request<ProjectStateDto>('project/switchBook', [bookId]))
  }),

  createBook: (name) => runSceneContext(async () => {
    get().applyState(await rpc.request<ProjectStateDto>('project/createBook', [name]))
  }),

  loadDrafts: async () => {
    set({ drafts: await rpc.request<{ id: string; name: string; isActive: boolean }[]>('project/drafts') })
  },

  createDraft: (name) => runSceneContext(async () => {
    // The backend clones the draft's files, so include prose still held in
    // mounted editors or their save queues before taking that snapshot.
    await flushPendingWrites()
    await get().flushPendingSave()
    const { useManuscriptStore } = await import('./manuscriptStore')
    await useManuscriptStore.getState().flushPendingSave()
    if (get().sceneConflict || Object.values(get().dirtyMap).some(Boolean)) {
      throw new Error(i18n.t('update.sceneWriteConflict'))
    }
    const active = get().drafts.find((d) => d.isActive)
    set({ drafts: await rpc.request<{ id: string; name: string; isActive: boolean }[]>('project/createDraft', [name, active?.id ?? null]) })
  }),

  switchDraft: (draftId) => runSceneContext(async () => {
    await flushPendingWrites()
    await get().flushPendingSave()
    get().applyState(await rpc.request<ProjectStateDto>('project/switchDraft', [draftId]))
  }),

  deleteDraft: (draftId) => runSceneContext(async () => {
    const wasActive = get().drafts.find((d) => d.id === draftId)?.isActive ?? false
    if (wasActive) {
      await flushPendingWrites()
      await get().flushPendingSave()
    }
    const drafts = await rpc.request<{ id: string; name: string; isActive: boolean }[]>(
      'project/deleteDraft',
      [draftId]
    )
    set({ drafts })
    // Deleting the active draft makes the backend switch to another draft and
    // reload its chapters/scenes, so refresh project state and reset the editor.
    if (wasActive) {
      set({ ...clearedEditorState() })
      get().applyState(await rpc.request<ProjectStateDto>('project/getState'))
    }
  })
})
