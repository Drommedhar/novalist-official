import { useState } from 'react'
import { type EditorWindow } from './editorBridge'
import { rpc } from '../../rpc/client'

export function useEditorEntityRequests(editorRef: React.RefObject<EditorWindow | null>, pushEntityNames: (editor: EditorWindow) => Promise<void>) {
  // A name typed after `@` that matched no entity, waiting for the writer to pick
  // which kind of entity to create for it.
  const [pendingEntity, setPendingEntity] = useState<{ name: string; pendingId: string } | null>(
    null
  )
  // A selected passage on its way into a Codex entity's section.
  const [pendingAppend, setPendingAppend] = useState<string | null>(null)
  /** Creates the entity the writer just named in the editor and upgrades the
   *  pending placeholder into a real mention. */
  const createPendingEntity = async (typeKey: string): Promise<void> => {
    const request = pendingEntity
    setPendingEntity(null)
    const editor = editorRef.current
    if (!request || !editor) return
    try {
      const record = await rpc.request<Record<string, unknown>>('entities/create', [
        typeKey,
        request.name,
        null
      ])
      editor.resolvePendingMention(request.pendingId, String(record.id), typeKey, request.name)
      // The new name has to become recognisable for hover cards and future @-picks.
      await pushEntityNames(editor)
    } catch {
      // Creation failed — leave the typed text in place rather than a placeholder.
      editor.resolvePendingMention(request.pendingId, null, null, request.name)
    }
  }

  /** Copies the selected passage into a Codex entity's section. */
  const appendSelectionToEntity = async (target: {
    typeKey: string
    id: string
    sectionTitle: string
  }): Promise<void> => {
    const passage = pendingAppend
    setPendingAppend(null)
    if (!passage) return
    await rpc.request('entities/appendToSection', [
      target.typeKey,
      target.id,
      target.sectionTitle,
      passage
    ])
  }

  const cancelPendingEntity = (): void => {
    const request = pendingEntity
    setPendingEntity(null)
    if (request)
      editorRef.current?.resolvePendingMention(request.pendingId, null, null, request.name)
  }

  return { pendingEntity, setPendingEntity, pendingAppend, setPendingAppend, createPendingEntity, cancelPendingEntity, appendSelectionToEntity }
}
