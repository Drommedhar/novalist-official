import { rpc } from '../rpc/client'
import { mirror, useProjectStore } from '../stores/projectStore'
import { retainPendingWrite } from '../stores/pendingWrites'
import type { EditorWindow } from '../views/editor/editorBridge'
import type { SharedSceneIdentity, SharedSceneUpdate } from '../../../shared/sharedScene'
import { SharedEditorSession, type SharedEditorStatus } from './SharedEditorSession'
import { SharedEditorPresence } from './SharedEditorPresence'

interface Binding {
  generation: string
  editor: EditorWindow
  identity: SharedSceneIdentity
  session: SharedEditorSession
  presence?: SharedEditorPresence
}

const bindings = new Map<string, Binding>()
interface RetainedScene { state: Uint8Array; flush(): Promise<void> }
const retainedStates = new Map<string, Map<string, RetainedScene>>()
const listeners = new Set<(event: SharedSceneUpdate) => void>()

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

export function syncSharedEditor(pane: string, editor: EditorWindow): 'attached' | 'active' | false {
  const identity = useProjectStore.getState().editors[pane]?.shared
  const previous = bindings.get(pane)
  if (previous?.editor === editor && identity && previous.identity.sessionId === identity.sessionId && previous.identity.itemId === identity.itemId) {
    if (previous.identity.state !== identity.state) {
      previous.identity = identity
      previous.session.receive(decodeState(identity.state))
    }
    return 'active'
  }
  detachSharedEditor(pane)
  if (!identity) return false
  const generation = crypto.randomUUID()
  const session = new SharedEditorSession(editor, decodeState(identity.state), {
    write: update => rpc.request('collaboration/writeScene', [identity.sessionId, identity.itemId, encodeState(update)]),
    read: async () => decodeState(await rpc.request<string>('collaboration/readScene', [identity.sessionId, identity.itemId])),
    subscribe: receive => subscribeSharedDocument(identity, receive)
  }, status => applyStatus(pane, identity, generation, status))
  bindings.set(pane, { editor, identity, session, generation,
    presence: identity.presence ? new SharedEditorPresence(identity, session) : undefined })
  for (const retained of retainedStates.get(sceneKey(identity))?.values() ?? []) session.receive(retained.state)
  void session.refresh().catch(() => applyError(pane, identity, generation))
  return 'attached'
}

export function captureSharedEditor(pane: string): boolean {
  const binding = bindings.get(pane)
  if (!binding) return false
  binding.session.capture()
  return true
}

export async function flushSharedEditor(pane: string, required: boolean): Promise<void> {
  const binding = bindings.get(pane)
  if (!binding) {
    if (required) throw new Error('The shared editor is not ready to save.')
    return
  }
  const pending = [...retainedStates.get(sceneKey(binding.identity))?.values() ?? []]
  for (const retained of pending) await retained.flush()
  await binding.session.flush()
}

export function detachSharedEditor(pane: string): void {
  const binding = bindings.get(pane)
  if (!binding) return
  const state = binding.session.state()
  binding.presence?.close()
  binding.session.detach()
  bindings.delete(pane)
  const key = sceneKey(binding.identity)
  const retained = retainedStates.get(key) ?? new Map<string, RetainedScene>()
  const flush = async (): Promise<void> => {
    await binding.session.flush()
    retained.delete(binding.generation)
    if (!retained.size && retainedStates.get(key) === retained) retainedStates.delete(key)
    for (const active of bindings.values()) if (sceneKey(active.identity) === key) active.session.capture()
  }
  retained.set(binding.generation, { state, flush })
  retainedStates.set(key, retained)
  retainPendingWrite(`collaboration:${key}:${binding.generation}`, flush)
}

function applyStatus(pane: string, identity: SharedSceneIdentity, generation: string, status: SharedEditorStatus): void {
  if (bindings.get(pane)?.generation !== generation) return
  useProjectStore.setState(state => {
    const editor = state.editors[pane]
    if (editor?.shared?.sessionId !== identity.sessionId || editor.shared.itemId !== identity.itemId || !editor.sceneId) return state
    const editors = { ...state.editors, [pane]: { ...editor, html: status.html, plainText: status.plainText,
      isDirty: status.pending || retainedStates.has(sceneKey(identity)), collaborationError: status.error !== null } }
    const dirty = Object.values(editors).some(candidate => candidate.sceneId === editor.sceneId && candidate.isDirty)
    return { editors, ...mirror(editors, state.activeEditorPaneId), dirtyMap: { ...state.dirtyMap, [editor.sceneId]: dirty } }
  })
}

function applyError(pane: string, identity: SharedSceneIdentity, generation: string): void {
  if (bindings.get(pane)?.generation !== generation) return
  useProjectStore.setState(state => {
    const editor = state.editors[pane]
    if (editor?.shared?.sessionId !== identity.sessionId || editor.shared.itemId !== identity.itemId) return state
    return { editors: { ...state.editors, [pane]: { ...editor, collaborationError: true } } }
  })
}

function sceneKey(identity: SharedSceneIdentity): string { return `${identity.sessionId}:${identity.itemId}` }

function decodeState(value: string): Uint8Array {
  if (value.length > Math.ceil(64 * 1024 * 1024 / 3) * 4) throw new Error('The shared scene is too large.')
  return Uint8Array.from(atob(value), character => character.charCodeAt(0))
}

function encodeState(value: Uint8Array): string {
  let binary = ''
  for (let offset = 0; offset < value.length; offset += 8192) binary += String.fromCharCode(...value.subarray(offset, offset + 8192))
  return btoa(binary)
}
