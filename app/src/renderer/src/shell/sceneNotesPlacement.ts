import type { PaneNode, PaneView } from '../stores/shellTypes'
import { paneLeaves, reidentify } from '../stores/shellPanes'

const STORAGE_KEY = 'nl.shell.sceneNotesPlacement'
const views: Record<PaneView, true> = {
  write: true, dashboard: true, manuscript: true, drafts: true, narration: true,
  timeline: true, plotGrid: true, calendar: true, relationships: true, dialogue: true,
  style: true, canvas: true, codex: true, wiki: true, maps: true, languages: true,
  series: true, research: true, gallery: true, expose: true, export: true, git: true,
  extensions: true, settings: true, about: true, sceneNotes: true
}

function validTree(value: unknown, depth = 0): value is PaneNode {
  if (!value || typeof value !== 'object' || depth > 32) return false
  const node = value as PaneNode
  if (typeof node.id !== 'string') return false
  if (node.kind === 'leaf') return Object.hasOwn(views, node.view)
  return node.kind === 'split' && (node.direction === 'row' || node.direction === 'column') &&
    Array.isArray(node.children) && node.children.length >= 2 && node.children.length <= 32 &&
    node.children.every((child) => validTree(child, depth + 1)) &&
    Array.isArray(node.sizes) && node.sizes.length === node.children.length &&
    node.sizes.every((size) => Number.isFinite(size) && size > 0 && size <= 100)
}

/** Machine-local geometry only: no project path, scene identity or note text. */
export function saveNotesPlacement(panes: PaneNode, activePaneId: string): void {
  try {
    const leaves = paneLeaves(panes)
    if (!leaves.some((pane) => pane.view === 'sceneNotes')) {
      localStorage.removeItem(STORAGE_KEY)
      return
    }
    localStorage.setItem(STORAGE_KEY, JSON.stringify({ root: panes, activePane: leaves.findIndex((pane) => pane.id === activePaneId) }))
  } catch { /* Unavailable preferences must not interrupt writing. */ }
}

export function readNotesPlacement(): { panes: PaneNode; activePaneId: string } | null {
  try {
    const stored = JSON.parse(localStorage.getItem(STORAGE_KEY) ?? 'null')
    if (!stored || !validTree(stored.root)) return null
    const leaves = paneLeaves(stored.root)
    if (leaves.filter((pane) => pane.view === 'sceneNotes').length !== 1 || leaves.length < 2) return null
    const panes = reidentify(stored.root)
    const restored = paneLeaves(panes)
    const selected = Number.isInteger(stored.activePane) ? restored[stored.activePane] : undefined
    const active = selected && selected.view !== 'sceneNotes' ? selected : restored.find((pane) => pane.view !== 'sceneNotes')!
    return { panes, activePaneId: active.id }
  } catch { return null }
}
