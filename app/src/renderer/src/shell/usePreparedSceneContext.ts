import { useEffect, useSyncExternalStore } from 'react'
import { useProjectStore } from '../stores/projectStore'
import { useShellStore } from '../stores/shellStore'
import { cancelPendingSceneContext, prepareSceneContext, readSceneContext, sceneContextScope, selectSceneContext, subscribeSceneContext } from './sceneContextData'

export function useSceneContext(chapterGuid?: string, sceneId?: string, immediate = true) {
  const writing = useShellStore(state => window.novalist.isMobile && state.mobileLayout === 'phone'
    ? state.mobileTab === 'manuscript' : state.mainView === 'write' || state.mainView === 'manuscript')
  const key = useProjectStore(state => sceneContextScope(state, chapterGuid, sceneId)?.key ?? null)
  const snapshot = useSyncExternalStore(subscribeSceneContext, readSceneContext)
  useEffect(() => {
    const scope = sceneContextScope(useProjectStore.getState(), chapterGuid, sceneId)
    selectSceneContext(scope)
    if (!writing) { cancelPendingSceneContext(); return }
    const current = readSceneContext()
    if (!scope || current.status !== 'idle') return
    const timer = setTimeout(() => { void prepareSceneContext(scope) },
      Math.max(current.retryAt - Date.now(), immediate ? 0 : 250))
    return () => clearTimeout(timer)
  }, [key, snapshot.key, snapshot.status, snapshot.retryAt, chapterGuid, sceneId, immediate, writing])
  return snapshot.key === key ? snapshot : { ...snapshot, context: null, status: 'idle' as const }
}

export function usePrepareSceneContext(): void {
  useSceneContext(undefined, undefined, false)
}
