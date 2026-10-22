import { rpc } from '../rpc/client'
import { retainPendingWrite } from '../stores/pendingWrites'
import type { SharedSceneIdentity, SharedSceneUpdate } from '../../../shared/sharedScene'
import type { SharedEditorConnection, SharedEditorSession } from './SharedEditorSession'

interface RetainedScene { state: Uint8Array; flush(): Promise<void> }
const retainedStates = new Map<string, Map<string, RetainedScene>>()
const listeners = new Set<(event: SharedSceneUpdate) => void>()
const retainedListeners = new Set<() => void>()

export function subscribeSharedDocument(identity: { sessionId: string; itemId: string }, receive: (update: Uint8Array) => void): () => void {
  const listener = (event: SharedSceneUpdate): void => {
    if (event.sessionId === identity.sessionId && event.itemId === identity.itemId) receive(decodeState(event.update))
  }
  listeners.add(listener)
  return () => { listeners.delete(listener) }
}

rpc.onNotification('collaboration/sceneUpdated', params => {
  const payload = Array.isArray(params) ? params[0] : params
  if (!payload || typeof payload !== 'object') return
  const event = payload as SharedSceneUpdate
  if (typeof event.sessionId !== 'string' || typeof event.itemId !== 'string' || typeof event.update !== 'string') return
  for (const listener of listeners) listener(event)
})

export function sharedDocumentConnection(identity: SharedSceneIdentity): SharedEditorConnection {
  return {
    write: update => rpc.request('collaboration/writeScene', [identity.sessionId, identity.itemId, encodeState(update)]),
    read: async () => decodeState(await rpc.request<string>('collaboration/readScene', [identity.sessionId, identity.itemId])),
    subscribe: receive => subscribeSharedDocument(identity, receive)
  }
}

export function hasRetainedDocument(identity: SharedSceneIdentity): boolean { return retainedStates.has(sceneKey(identity)) }

export function receiveRetainedDocument(identity: SharedSceneIdentity, session: SharedEditorSession): void {
  for (const retained of retainedStates.get(sceneKey(identity))?.values() ?? []) session.receive(retained.state)
}

export async function flushRetainedDocument(identity: SharedSceneIdentity): Promise<void> {
  for (const retained of retainedStates.get(sceneKey(identity))?.values() ?? []) await retained.flush()
}

export function observeRetainedDocuments(changed: () => void): () => void {
  retainedListeners.add(changed)
  return () => { retainedListeners.delete(changed) }
}

export function retainSharedDocument(identity: SharedSceneIdentity, generation: string, session: SharedEditorSession): void {
  const state = session.state()
  session.detach()
  const key = sceneKey(identity)
  const retained = retainedStates.get(key) ?? new Map<string, RetainedScene>()
  const flush = async (): Promise<void> => {
    await session.flush()
    retained.delete(generation)
    if (!retained.size && retainedStates.get(key) === retained) retainedStates.delete(key)
    for (const changed of retainedListeners) changed()
  }
  retained.set(generation, { state, flush })
  retainedStates.set(key, retained)
  retainPendingWrite(`collaboration:${key}:${generation}`, flush)
}

function sceneKey(identity: SharedSceneIdentity): string { return `${identity.sessionId}:${identity.itemId}` }

export function decodeState(value: string): Uint8Array {
  if (value.length > Math.ceil(64 * 1024 * 1024 / 3) * 4) throw new Error('The shared scene is too large.')
  return Uint8Array.from(atob(value), character => character.charCodeAt(0))
}

function encodeState(value: Uint8Array): string {
  let binary = ''
  for (let offset = 0; offset < value.length; offset += 8192) binary += String.fromCharCode(...value.subarray(offset, offset + 8192))
  return btoa(binary)
}
