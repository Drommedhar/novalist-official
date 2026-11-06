import { mirror, useProjectStore } from '../stores/projectStore'
import type { EditorWindow } from '../views/editor/editorBridge'
import type { SharedSceneIdentity } from '../../../shared/sharedScene'
import { SharedEditorSession, type SharedEditorStatus } from './SharedEditorSession'
import { SharedEditorPresence } from './SharedEditorPresence'
import { sceneEditorSurface } from './richEditingSurface'
import { decodeState, sharedDocumentConnection, receiveRetainedDocument, flushRetainedDocument, retainSharedDocument, hasRetainedDocument, observeRetainedDocuments } from './sharedDocuments'
export { subscribeSharedDocument } from './sharedDocuments'

interface Binding {
  generation: string
  editor: EditorWindow
  identity: SharedSceneIdentity
  session: SharedEditorSession
  presence?: SharedEditorPresence
}

const bindings = new Map<string, Binding>()
observeRetainedDocuments(() => { for (const binding of bindings.values()) binding.session.capture() })

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
  const session = new SharedEditorSession(sceneEditorSurface(editor), decodeState(identity.state), sharedDocumentConnection(identity),
    status => applyStatus(pane, identity, generation, status))
  bindings.set(pane, { editor, identity, session, generation,
    presence: identity.presence ? new SharedEditorPresence(identity, session) : undefined })
  receiveRetainedDocument(identity, session)
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
  await flushRetainedDocument(binding.identity)
  await binding.session.flush()
}

export function detachSharedEditor(pane: string): void {
  const binding = bindings.get(pane)
  if (!binding) return
  binding.presence?.close()
  retainSharedDocument(binding.identity, binding.generation, binding.session)
  bindings.delete(pane)
}

function applyStatus(pane: string, identity: SharedSceneIdentity, generation: string, status: SharedEditorStatus): void {
  if (bindings.get(pane)?.generation !== generation) return
  useProjectStore.setState(state => {
    const editor = state.editors[pane]
    if (editor?.shared?.sessionId !== identity.sessionId || editor.shared.itemId !== identity.itemId || !editor.sceneId) return state
    const editors = { ...state.editors, [pane]: { ...editor, html: status.html, plainText: status.plainText,
      isDirty: status.pending || hasRetainedDocument(identity), collaborationError: status.error !== null } }
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
