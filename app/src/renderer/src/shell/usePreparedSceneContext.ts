import { useEffect, useSyncExternalStore } from 'react'
import { useProjectStore } from '../stores/projectStore'
import { useManuscriptStore } from '../stores/manuscriptStore'
import { useShellStore } from '../stores/shellStore'
import { useSharedProjectRevision } from '../collaboration/sharedProjectNotifications'
import { cancelPendingSceneContext, prepareSceneContext, readSceneContext, sceneContextScope, selectSceneContext, subscribeSceneContext } from './sceneContextData'

function isManuscriptView(): boolean {
  const shell = useShellStore.getState()
  // The phone's Manuscript tab hosts an EditorFrame, not the composed manuscript.
  return !(window.novalist.isMobile && shell.mobileLayout === 'phone') && shell.mainView === 'manuscript'
}

export function currentSceneContextScope(chapterGuid?: string, sceneId?: string) {
  const state = useProjectStore.getState()
  const chapter = chapterGuid ?? state.openChapterGuid
  const scene = sceneId ?? state.openSceneId
  const html = isManuscriptView()
    ? useManuscriptStore.getState().sections.find(section => section.chapterGuid === chapter)
      ?.scenes.find(candidate => candidate.sceneId === scene)?.html ?? null
    : undefined
  return sceneContextScope(state, chapter, scene, html, useSharedProjectRevision.getState())
}

export function useSceneContext(chapterGuid?: string, sceneId?: string, immediate = true) {
  const sharedRevision = useSharedProjectRevision()
  const writing = useShellStore(state => window.novalist.isMobile && state.mobileLayout === 'phone'
    ? state.mobileTab === 'manuscript' : state.mainView === 'write' || state.mainView === 'manuscript')
  const manuscript = useShellStore(state => !(window.novalist.isMobile && state.mobileLayout === 'phone') &&
    state.mainView === 'manuscript')
  const chapter = useProjectStore(state => chapterGuid ?? state.openChapterGuid)
  const scene = useProjectStore(state => sceneId ?? state.openSceneId)
  const manuscriptHtml = useManuscriptStore(state => manuscript
    ? state.sections.find(section => section.chapterGuid === chapter)
      ?.scenes.find(candidate => candidate.sceneId === scene)?.html ?? null
    : null)
  // Subscribe to primitive values: creating a new object in a Zustand selector
  // would make useSyncExternalStore repeatedly render an unchanged selection.
  const key = useProjectStore(state => sceneContextScope(state, chapter, scene,
    manuscript ? manuscriptHtml : undefined, sharedRevision)?.key ?? null)
  const scope = currentSceneContextScope(chapterGuid, sceneId)
  const snapshot = useSyncExternalStore(subscribeSceneContext, readSceneContext)
  useEffect(() => {
    const currentScope = currentSceneContextScope(chapterGuid, sceneId)
    // A retained panel can render once with old props during a scene switch. It
    // must not clear the new scene's selection, which the shared preparer owns.
    const state = useProjectStore.getState()
    if ((chapterGuid && chapterGuid !== state.openChapterGuid) ||
        (sceneId && sceneId !== state.openSceneId)) return
    selectSceneContext(currentScope, writing)
    if (!writing) { cancelPendingSceneContext(); return }
    const current = readSceneContext()
    if (!currentScope || current.status !== 'idle') return
    const timer = setTimeout(() => {
      // Recheck after the debounce: workspace and editor switches can happen
      // before React cleans up the timer from the previous render.
      const latest = currentSceneContextScope(chapterGuid, sceneId)
      if (latest?.key === currentScope.key) void prepareSceneContext(latest)
    }, Math.max(current.retryAt - Date.now(), immediate ? 0 : 250))
    return () => clearTimeout(timer)
  }, [key, snapshot.key, snapshot.status, snapshot.retryAt, chapterGuid, sceneId, immediate, writing])
  return snapshot.key === key ? snapshot : { ...snapshot, key, selectionKey: scope?.selectionKey ?? null,
    context: writing && scope?.selectionKey === snapshot.selectionKey ? snapshot.context : null,
    status: 'idle' as const, error: null }
}

export function usePrepareSceneContext(): void {
  useSceneContext(undefined, undefined, false)
}
