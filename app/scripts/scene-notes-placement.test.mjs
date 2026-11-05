import { test } from 'node:test'
import assert from 'node:assert/strict'
import { loadRendererSource } from './renderer-source-loader.mjs'

function harness(storage = new Map()) {
  const localStorage = { getItem: key => storage.get(key) ?? null, setItem: (key, value) => storage.set(key, value), removeItem: key => storage.delete(key) }
  const panes = loadRendererSource('src/stores/shellPanes.ts', {}, { localStorage })
  const placement = loadRendererSource('src/shell/sceneNotesPlacement.ts', { '../stores/shellPanes': panes }, { localStorage })
  return { panes, placement, storage }
}

test('automatic placement restores a resized nested tree with a valid active content pane', () => {
  const h = harness()
  const first = h.panes.newLeaf('write')
  const outer = h.panes.splitPane(first, first.id, 'row').root
  const target = h.panes.paneLeaves(outer)[1]
  const root = h.panes.dockNotesPane(outer, target.id, 'top')
  root.sizes = [35, 65]
  root.children[1].sizes = [42, 58]
  h.placement.saveNotesPlacement(root, target.id)
  const restarted = harness(h.storage)
  const restored = restarted.placement.readNotesPlacement()
  assert.ok(restored)
  assert.equal(h.panes.sameArrangement(root, restored.panes), true)
  assert.equal(restarted.panes.findPane(restored.panes, restored.activePaneId).view, 'write')
  const next = restarted.panes.newLeaf('research')
  assert.ok(!restarted.panes.findPane(restored.panes, next.id), 'restored node ids must not collide with later splits')
})

test('removing notes from the layout clears automatic placement rather than resurrecting an old dock', () => {
  const h = harness()
  const editor = h.panes.newLeaf('write')
  h.placement.saveNotesPlacement(h.panes.dockNotesPane(editor, editor.id, 'right'), editor.id)
  h.placement.saveNotesPlacement(editor, editor.id)
  assert.equal(h.placement.readNotesPlacement(), null)
})

test('corrupt or incomplete placement cannot replace the reachable main content area', () => {
  const h = harness()
  const key = 'nl.shell.sceneNotesPlacement'
  for (const value of ['{', 'null', '{}', JSON.stringify({ root: { kind: 'leaf', id: 'notes', view: 'sceneNotes' } }),
    JSON.stringify({ root: { kind: 'split', id: 'broken', direction: 'row', sizes: [100], children: [] } })]) {
    h.storage.set(key, value)
    assert.equal(h.placement.readNotesPlacement(), null)
  }
})
