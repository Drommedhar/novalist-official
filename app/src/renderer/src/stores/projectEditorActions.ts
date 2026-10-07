import { rpc } from '../rpc/client'
import { paneLeaves, useShellStore } from './shellStore'
import { EMPTY_EDITOR, editorPane, mirror, mapEditors, patchEditor, writePaneIds, targetEditorPane } from './projectStore'
import { autosaveTimers, autosaveWrites, flushEditor, stripHtml, scheduleSave } from './projectPersistence'
import { runSceneContext } from './projectSceneActions'
import { type ProjectSlice } from './projectTypes'
import { capturePendingWrites } from './pendingWrites'
import { enqueueSceneWrite } from './sceneWriteQueue'
import { retainMobileSceneRecovery } from '../mobile/recovery'

export const createProjectEditorActions: ProjectSlice<'openScene' | 'openSceneIn' | 'openSceneInSplit' | 'resolveSceneConflict' | 'dismissSceneConflict' | 'closeTab' | 'moveTabToOtherPane' | 'onEditorContentChanged' | 'syncEditorPanes' | 'flushPane' | 'flushPendingSave' | 'applyManuscriptSceneWrite'> = (set, get) => ({
  openScene: async (chapterGuid, sceneId) => {
    await get().openSceneIn(targetEditorPane(), chapterGuid, sceneId)
  },

  openSceneIn: (paneId, chapterGuid, sceneId) => runSceneContext(async () => {
    const epoch = get().workspaceEpoch
    const shell = useShellStore.getState()
    // A scene opening is a screen closing. The binder, a search hit and a link
    // all come through here, so a screen holding unsaved edits gets its say
    // before the editor takes the pane.
    if (Object.values(shell.unsavedGuards).some((g) => g.isDirty())) {
      shell.guardLeave(() => void get().openSceneIn(paneId, chapterGuid, sceneId))
      return
    }
    // The pane holds the editor from here on, and it is the one the writer is
    // now in: opening a scene somewhere the caret is not is how the old split
    // pane lost people.
    shell.setPaneView(paneId, 'write')
    shell.setActivePane(paneId)
    capturePendingWrites()
    await get().flushPane(paneId)
    if (get().sceneConflict || get().editors[paneId]?.isDirty) return
    const content = await rpc.request<{ sceneId: string; html: string; hash: string }>(
      'scenes/read',
      [chapterGuid, sceneId]
    )
    if (get().workspaceEpoch !== epoch) return
    set((s) => {
      const previous = editorPane(s, paneId)
      const editors = {
        ...s.editors,
        [paneId]: {
          chapterGuid,
          sceneId,
          html: content.html,
          hash: content.hash,
          plainText: stripHtml(content.html),
          tabs: previous.tabs.some((t) => t.sceneId === sceneId)
            ? previous.tabs
            : [...previous.tabs, { chapterGuid, sceneId }],
          isDirty: false
        }
      }
      return {
        editors,
        activeEditorPaneId: paneId,
        sceneHashes: { ...s.sceneHashes, [sceneId]: content.hash },
        ...mirror(editors, paneId)
      }
    })
  }),

  openSceneInSplit: async (chapterGuid, sceneId) => {
    // "Open in split" now means a real second pane rather than the editor's own
    // two-slot arrangement, so the scene can sit beside the Codex or a third
    // scene just as easily as beside another editor.
    const shell = useShellStore.getState()
    const target = shell.splitPaneById(targetEditorPane(), 'row')
    if (target) await get().openSceneIn(target, chapterGuid, sceneId)
  },

  resolveSceneConflict: async (html) => {
    const conflict = get().sceneConflict
    if (!conflict) return
    for (const [paneId, editor] of Object.entries(get().editors)) {
      if (editor.sceneId !== conflict.sceneId) continue
      const timer = autosaveTimers.get(paneId)
      if (timer) clearTimeout(timer)
      autosaveTimers.delete(paneId)
    }
    const result = await enqueueSceneWrite(conflict.sceneId, () => rpc.request<{ wordCount: number; hash: string }>(
      'scenes/resolveConflict',
      [conflict.chapterGuid, conflict.sceneId, html, stripHtml(html), conflict.mine]
      )
    )
    set((state) => {
      // The resolved text belongs to every pane holding that scene, not just the
      // one the writer resolved it from.
      const editors = mapEditors(state.editors, (editor) =>
        editor.sceneId === conflict.sceneId && (!editor.isDirty || editor.html === conflict.mine)
          ? { ...editor, html, plainText: stripHtml(html), hash: result.hash, isDirty: false } : editor
      )
      return {
        sceneConflict: null,
        sceneHashes: { ...state.sceneHashes, [conflict.sceneId]: result.hash },
        editors,
        dirtyMap: { ...state.dirtyMap, [conflict.sceneId]: Object.values(editors).some((editor) => editor.sceneId === conflict.sceneId && editor.isDirty) },
        ...mirror(editors, state.activeEditorPaneId),
        chapters: state.chapters.map((c) =>
          c.guid === conflict.chapterGuid
            ? {
                ...c,
                scenes: c.scenes.map((sc) =>
                  sc.id === conflict.sceneId ? { ...sc, wordCount: result.wordCount } : sc
                )
              }
            : c
        )
      }
    })
    if (window.novalist.isMobile) {
      const { acknowledgeMobileSceneRecovery } = await import('../mobile/recovery')
      acknowledgeMobileSceneRecovery(conflict.sceneId, html, conflict.mine)
    }
    // manuscriptStore imports this store, so load it lazily here rather than
    // creating an eager module cycle. A resolved manuscript payload must be
    // retired or its old version would immediately conflict again on shutdown.
    const { useManuscriptStore } = await import('./manuscriptStore')
    useManuscriptStore
      .getState()
      .acceptResolvedScene(conflict.sceneId, html, result.wordCount, result.hash)
  },

  dismissSceneConflict: () => set({ sceneConflict: null }),

  closeTab: (paneId, sceneId) => runSceneContext(async () => {
    const editor = editorPane(get(), paneId)
    const idx = editor.tabs.findIndex((t) => t.sceneId === sceneId)
    if (idx < 0) return
    const isActive = editor.sceneId === sceneId
    if (isActive) {
      capturePendingWrites()
      await get().flushPane(paneId)
      if (get().sceneConflict || get().editors[paneId]?.isDirty) return
    }
    const remaining = editorPane(get(), paneId).tabs.filter((t) => t.sceneId !== sceneId)

    if (!isActive) {
      set((s) => patchEditor(s, paneId, { tabs: remaining }))
      return
    }
    if (remaining.length === 0) {
      set((s) => patchEditor(s, paneId, { ...EMPTY_EDITOR }))
      // Nothing left in this pane. With another editor still open the writer is
      // mid-work elsewhere, so only a shell with no scene open anywhere falls
      // back to the dashboard.
      if (!Object.values(get().editors).some((e) => e.sceneId)) {
        useShellStore.getState().setMainView('dashboard')
      }
      return
    }
    const next = remaining[Math.min(idx, remaining.length - 1)]
    set((s) => patchEditor(s, paneId, { tabs: remaining }))
    await get().openSceneIn(paneId, next.chapterGuid, next.sceneId)
  }),

  moveTabToOtherPane: async (paneId, sceneId) => {
    const tab = editorPane(get(), paneId).tabs.find((t) => t.sceneId === sceneId)
    if (!tab) return
    const others = writePaneIds().filter((id) => id !== paneId)
    await get().closeTab(paneId, sceneId)
    if (editorPane(get(), paneId).tabs.some((entry) => entry.sceneId === sceneId)) return
    // With no second pane to move to, "the other pane" is one that has to exist
    // first - which is what a writer asking for this means anyway.
    if (others.length === 0) await get().openSceneInSplit(tab.chapterGuid, tab.sceneId)
    else await get().openSceneIn(others[0], tab.chapterGuid, tab.sceneId)
  },

  onEditorContentChanged: (paneId, html, plainText) => {
    const editor = editorPane(get(), paneId)
    const { chapterGuid, sceneId } = editor
    if (!chapterGuid || !sceneId) return
    set((s) => ({
      ...patchEditor(s, paneId, { html, plainText, isDirty: true }),
      dirtyMap: { ...s.dirtyMap, [sceneId]: true }
    }))
    retainMobileSceneRecovery({ source: `pane:${paneId}`, chapterGuid, sceneId, html, plainText, hash: editor.hash ?? '' })
    scheduleSave(paneId, chapterGuid, sceneId, html, plainText)
  },

  syncEditorPanes: () => {
    const shell = useShellStore.getState()
    // A pane that has gone away and a pane that is showing something else are
    // two different things, and treating them as one is how a trip to the
    // Timeline closed the scene the writer was in. Only the first forgets an
    // editor; the second is a writer looking at their outline for a moment.
    const leaves = paneLeaves(shell.panes)
    const present = leaves.map((leaf) => leaf.id)
    const showingEditor = leaves.filter((leaf) => leaf.view === 'write').map((leaf) => leaf.id)
    const s = get()
    let editors = s.editors
    const stale = Object.keys(editors).filter((id) => !present.includes(id))
    if (stale.length > 0) {
      editors = { ...editors }
      for (const id of stale) {
        // A pane that goes away takes its editor with it, but not the writer's
        // last keystrokes: closing a split must never be a way to lose words.
        if (editors[id].isDirty) {
          void flushEditor(editors[id], id).catch(() => {})
          continue
        }
        delete editors[id]
      }
    }
    // A pane that has turned into something else has no editor on screen to
    // finish the pending save, so it is written out now rather than left to a
    // timer nobody can see. The scene stays open behind it either way.
    for (const id of Object.keys(editors)) {
      if (!showingEditor.includes(id)) void get().flushPane(id)
    }

    // The shell follows the pane the writer is in when that pane is an editor,
    // and otherwise stays on the editor they were last in - which, while every
    // pane is showing something else, is what the inspector and the status bar
    // go on describing.
    const active = showingEditor.includes(shell.activePaneId)
      ? shell.activePaneId
      : s.activeEditorPaneId && present.includes(s.activeEditorPaneId)
        ? s.activeEditorPaneId
        : (showingEditor[0] ?? null)
    if (editors === s.editors && active === s.activeEditorPaneId) return
    set({ editors, activeEditorPaneId: active, ...mirror(editors, active) })
  },

  flushPane: async (paneId) => {
    const timer = autosaveTimers.get(paneId)
    if (timer) clearTimeout(timer)
    autosaveTimers.delete(paneId)
    const inFlight = autosaveWrites.get(paneId)
    if (inFlight) await inFlight
    await flushEditor(get().editors[paneId], paneId)
  },

  flushPendingSave: async () => {
    const paneIds = new Set([
      ...Object.keys(get().editors),
      ...autosaveTimers.keys(),
      ...autosaveWrites.keys()
    ])
    for (const paneId of paneIds) await get().flushPane(paneId)
  },

  applyManuscriptSceneWrite: (chapterGuid, sceneId, html, plainText, wordCount, hash) => {
    set((state) => {
      let hasDivergentDirtyEditor = false
      const editors = mapEditors(state.editors, (editor) => {
        if (editor.sceneId !== sceneId) return editor
        const matches = editor.html === html && (editor.plainText ?? '') === plainText
        if (editor.isDirty && !matches) {
          hasDivergentDirtyEditor = true
          return editor
        }
        // A clean frame must follow the version just written elsewhere. An
        // exact dirty match is now acknowledged too; there is no conflict to
        // ask the writer to resolve.
        return { ...editor, html, plainText, hash, isDirty: false }
      })
      const stillDirty = Object.values(editors).some(
        (editor) => editor.sceneId === sceneId && editor.isDirty
      )
      return {
        // A divergent editor still owns the hash it read. Keeping that base
        // makes its later checked write conflict with this manuscript save;
        // advancing it here would authorize a silent overwrite.
        sceneHashes: hasDivergentDirtyEditor
          ? state.sceneHashes
          : { ...state.sceneHashes, [sceneId]: hash },
        editors,
        ...mirror(editors, state.activeEditorPaneId),
        dirtyMap:
          state.dirtyMap[sceneId] !== stillDirty
            ? { ...state.dirtyMap, [sceneId]: stillDirty }
            : state.dirtyMap,
        chapters: state.chapters.map((chapter) =>
          chapter.guid === chapterGuid
            ? {
                ...chapter,
                scenes: chapter.scenes.map((scene) =>
                  scene.id === sceneId ? { ...scene, wordCount } : scene
                )
              }
            : chapter
        )
      }
    })
  }
})
