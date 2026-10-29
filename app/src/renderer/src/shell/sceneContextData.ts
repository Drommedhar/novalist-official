import { rpc } from '../rpc/client'
import { registerBackgroundRead } from '../rpc/backgroundReads'
import type { ProjectState } from '../stores/projectTypes'
import type { SceneContext } from './contextTypes'

interface ContextScope { key: string; chapterGuid: string; sceneId: string }
interface ContextSnapshot {
  key: string | null
  context: SceneContext | null
  status: 'idle' | 'loading' | 'ready' | 'error'
  error: string | null
  retryAt: number
}

let snapshot: ContextSnapshot = { key: null, context: null, status: 'idle', error: null, retryAt: 0 }
let pending: { controller: AbortController; promise: Promise<void> } | null = null
const listeners = new Set<() => void>()
const contextMutations = new Set([
  'entities/create', 'entities/update', 'entities/delete', 'entities/moveToBook', 'entities/moveToWorldBible',
  'entities/setOverride', 'entities/removeOverride', 'entities/setStateOverrides', 'entities/setMatchSettings',
  'entities/setOverrideImages', 'entities/addOverrideImage', 'entities/addOverrideImageFromUrl',
  'entities/setOverrideRelationships', 'entities/setOverrideSections', 'entities/updateLists',
  'entities/appendToSection', 'entities/setRelationships', 'entities/setGroup', 'entities/setParent',
  'entities/setIsWorld', 'entities/setLocked', 'entities/setCustomProp', 'entities/restoreRevision',
  'entities/saveCustomType', 'entities/deleteCustomType', 'entities/addImage', 'entities/removeImage',
  'entities/setImageAlt', 'entities/renameImage', 'entities/replaceImage', 'entities/addImageFromUrl',
  'scenes/write', 'scenes/resolveConflict', 'scenes/setPov', 'scenes/setCast', 'scenes/setAnalysisOverride',
  'scenes/resetAnalysisOverride', 'scenes/setRelativeTime', 'scenes/setInactive', 'scenes/restoreArchived',
  'sceneBulk/delete', 'sceneBulk/archive', 'sceneBulk/setTags', 'sceneBulk/shiftDates', 'sceneBulk/moveToChapter',
  'project/createChapter', 'project/createScene', 'project/renameChapter', 'project/renameScene',
  'project/deleteChapter', 'project/deleteScene', 'project/restoreChapter', 'project/moveScenes',
  'project/reorderChapter', 'project/reorderScene', 'project/setChapterAct', 'project/setChapterDateRange',
  'project/setSceneDateRange', 'settings/updateProject', 'settings/updateGlobal', 'settings/clearSection',
  'premise/saveVoice', 'premise/scaffold', 'research/save', 'research/delete', 'research/import',
  'research/quickCapture', 'research/restoreRevision', 'research/link', 'research/setLifecycle'
])

rpc.onRequestCompleted(method => { if (contextMutations.has(method)) invalidateSceneContext() })

export function sceneContextScope(state: ProjectState,
  chapterGuid = state.openChapterGuid, sceneId = state.openSceneId): ContextScope | null {
  if (!state.isLoaded || state.closingProject || state.workspaceSuspended || !chapterGuid || !sceneId) return null
  return { chapterGuid, sceneId, key: JSON.stringify([state.projectPath, state.activeBookId,
    state.activeDraftId, state.workspaceEpoch, chapterGuid, sceneId, state.sceneHashes[sceneId] ?? null]) }
}

export function readSceneContext(): ContextSnapshot { return snapshot }
export function subscribeSceneContext(listener: () => void): () => void {
  listeners.add(listener)
  return () => { listeners.delete(listener) }
}

function publish(next: ContextSnapshot): void {
  snapshot = next
  for (const listener of listeners) listener()
}

export function selectSceneContext(scope: ContextScope | null): void {
  const key = scope?.key ?? null
  if (snapshot.key === key) return
  const previous = pending
  pending = null
  previous?.controller.abort()
  publish({ key, context: null, status: 'idle', error: null, retryAt: 0 })
}

export function cancelPendingSceneContext(): void { pending?.controller.abort() }

export function invalidateSceneContext(): void {
  const previous = pending
  pending = null
  previous?.controller.abort()
  if (snapshot.key !== null) publish({ ...snapshot, context: null, status: 'idle', error: null, retryAt: 0 })
}

export function prepareSceneContext(scope: ContextScope, refresh = false): Promise<void> {
  selectSceneContext(scope)
  if (!refresh && pending && !pending.controller.signal.aborted) return pending.promise
  if (!refresh && snapshot.status === 'ready') return Promise.resolve()
  pending?.controller.abort()
  const request = { controller: new AbortController(), promise: Promise.resolve() }
  pending = request
  const unregister = registerBackgroundRead(request.controller)
  publish({ ...snapshot, status: 'loading', error: null, retryAt: 0 })
  request.promise = rpc.request<SceneContext>('context/analyze', [scope.chapterGuid, scope.sceneId],
    { signal: request.controller.signal }).then(context => {
    if (pending !== request || request.controller.signal.aborted) return
    publish({ key: scope.key, context, status: 'ready', error: null, retryAt: 0 })
  }).catch(error => {
    if (pending !== request) return
    const cancelled = request.controller.signal.aborted
    publish({ ...snapshot, status: cancelled ? 'idle' : 'error',
      error: cancelled ? null : String(error), retryAt: cancelled ? Date.now() + 1000 : 0 })
  }).finally(() => {
    if (pending === request) pending = null
    unregister()
  })
  return request.promise
}
