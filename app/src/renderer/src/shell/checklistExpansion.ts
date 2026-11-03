import type { SceneNotesContext } from '../../../shared/sceneNotesProtocol'
import type { ChecklistBlock } from './sceneChecklist'

/** Session epochs and scene titles change across restarts without changing the notes. */
export function checklistExpansionScope(context: SceneNotesContext): string {
  return 'nl.notes.expanded.' + JSON.stringify([context.projectPath, context.bookId, context.draftId, context.sceneId])
}

/** Headings keep their disclosure state when lines are inserted or checked off.
 * Duplicate sibling headings are distinguished by their occurrence. */
export function checklistItemKeys(blocks: ChecklistBlock[]): Map<number, string> {
  const keys = new Map<number, string>()
  const visit = (items: ChecklistBlock[], ancestors: [string, number][]): void => {
    const occurrences = new Map<string, number>()
    for (const item of items) {
      if (item.kind !== 'item') continue
      const occurrence = occurrences.get(item.title) ?? 0
      occurrences.set(item.title, occurrence + 1)
      const path: [string, number][] = [...ancestors, [item.title, occurrence]]
      keys.set(item.line, JSON.stringify(path))
      visit(item.children, path)
    }
  }
  visit(blocks, [])
  return keys
}

export function readChecklistExpansion(scope: string): Set<string> {
  try {
    const value: unknown = JSON.parse(localStorage.getItem(scope) ?? '[]')
    return new Set(Array.isArray(value) ? value.filter((key): key is string => typeof key === 'string') : [])
  } catch { return new Set() }
}

export function saveChecklistExpansion(scope: string, expanded: Set<string>): void {
  try { localStorage.setItem(scope, JSON.stringify([...expanded])) }
  catch { /* Disclosure preferences must never block editing or saving notes. */ }
}
