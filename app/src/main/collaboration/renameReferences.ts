import type * as Y from 'yjs'
import type { SceneMarkup } from '../../renderer/src/collaboration/sceneMarkup'
import { updateRichText } from '../../renderer/src/collaboration/richText'

export function renameReferences(scene: { doc: Y.Doc; root: HTMLElement; markup: SceneMarkup | null },
  entityId: string, oldName: string, newName: string): boolean {
  const text = scene.doc.getText('prose')
  if (!scene.markup) {
    const previous = text.toString()
    const escaped = oldName.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
    const pattern = new RegExp('\\[\\[\\s*' + escaped + '\\s*(\\|[^\\]]*)?\\]\\]', 'gi')
    const next = previous.replace(pattern, (_, label: string | undefined) => `[[${newName}${label ?? ''}]]`)
    if (next === previous) return false
    scene.doc.transact(() => updateRichText(text, [{ insert: next }]))
    return true
  }
  let changed = false
  for (const mention of scene.root.querySelectorAll('.nv-entity-mention')) {
    if (mention.getAttribute('data-entity-id') !== entityId) continue
    if (['alias', 'manual'].includes(mention.getAttribute('data-mention-source') ?? '')) continue
    if (mention.textContent === newName) continue
    mention.textContent = newName
    changed = true
  }
  if (changed) {
    const runs = scene.markup.read()
    scene.doc.transact(() => updateRichText(text, runs))
  }
  return changed
}
