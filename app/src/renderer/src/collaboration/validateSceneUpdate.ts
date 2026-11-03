import * as Y from 'yjs'
import type { SceneMarkup } from './sceneMarkup'

export class InvalidSceneUpdate extends Error {
  constructor() { super('The shared edit is not valid scene content.') }
}

export function validateSceneUpdate(doc: Y.Doc, update: Uint8Array, markup: SceneMarkup | null, requireComplete = false): boolean {
  let complete: boolean
  try { complete = validate(doc, update, markup) }
  catch { throw new InvalidSceneUpdate() }
  if (requireComplete && !complete) throw new Error('The shared scene checkpoint has incomplete editing history.')
  return complete
}

function validate(doc: Y.Doc, update: Uint8Array, markup: SceneMarkup | null): boolean {
  if (update.byteLength > 64 * 1024 * 1024) throw new Error('The shared scene update is too large.')
  for (const structure of Y.decodeUpdate(update).structs) {
    if (!(structure instanceof Y.Item)) continue
    if (typeof structure.parent === 'string' && structure.parent !== 'prose')
      throw new Error('Unsupported shared scene field.')
    const content = structure.content
    if (!markup && content instanceof Y.ContentFormat) throw new Error('Plain prose cannot contain rich formatting.')
    if (!(content instanceof Y.ContentString || content instanceof Y.ContentFormat || content instanceof Y.ContentDeleted))
      throw new Error('Unsupported shared prose content.')
    if (content instanceof Y.ContentFormat && content.value !== null && typeof content.value !== 'string')
      throw new Error('Invalid shared prose attribute.')
    if (content instanceof Y.ContentFormat && typeof content.value === 'string')
      markup?.validateAttribute(content.key, content.value)
  }
  const candidate = new Y.Doc()
  try {
    Y.applyUpdate(candidate, Y.encodeStateAsUpdate(doc))
    Y.applyUpdate(candidate, update)
    if ([...candidate.share.keys()].some(key => key !== 'prose')) throw new Error('Unsupported shared scene field.')
    if (candidate.store.pendingStructs || candidate.store.pendingDs) {
      return false
    }
    markup?.prepare(candidate.getText('prose').toDelta())
    return true
  } finally {
    candidate.destroy()
  }
}
