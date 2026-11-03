import { CollaborationDocumentBridge } from './CollaborationDocumentBridge'

export function installCollaborationHost(
  window: Window & typeof globalThis,
  reply: (id: number, ok: boolean, result: unknown) => Promise<void>
): () => void {
  const documents = new CollaborationDocumentBridge(window)
  window.__novalistCollaborationRequest = (id, parameters) => {
    void documents.handle('collaboration/documents', parameters)
      .then(result => reply(id, true, result), () => reply(id, false, null))
      .catch(() => window.__novalistBridgeFailed?.())
    return 'accepted'
  }
  return () => {
    delete window.__novalistCollaborationRequest
    void documents.reset()
  }
}
