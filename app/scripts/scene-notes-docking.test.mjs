import { test } from 'node:test'
import assert from 'node:assert/strict'
import { loadRendererSource } from './renderer-source-loader.mjs'
const panes = loadRendererSource('src/stores/shellPanes.ts', {}, { localStorage: { getItem: () => null } })

for (const edge of ['left', 'right', 'top', 'bottom']) test(`notes dock ${edge} of a nested pane without replacing its content`, () => {
  const editor = panes.newLeaf('write')
  const root = panes.splitPane(editor, editor.id, 'row').root
  const target = panes.paneLeaves(root)[1]
  const result = panes.dockNotesPane(root, target.id, edge)
  const split = result.children[1]
  assert.equal(split.direction, ['left', 'right'].includes(edge) ? 'row' : 'column')
  assert.equal(split.children[['left', 'top'].includes(edge) ? 0 : 1].view, 'sceneNotes')
  assert.equal(panes.paneLeaves(result).filter(p => p.view === 'write').length, 2)
})

test('moving notes reuses one leaf, collapses its old split and preserves the target', () => {
  const editor = panes.newLeaf('write')
  const first = panes.dockNotesPane(editor, editor.id, 'right')
  const note = panes.paneLeaves(first).find(p => p.view === 'sceneNotes')
  const next = panes.dockNotesPane(first, editor.id, 'top')
  assert.equal(next.direction, 'column')
  assert.equal(next.children[0].id, note.id)
  assert.equal(panes.paneLeaves(next).length, 2)
  assert.equal(panes.dockNotesPane(next, 'missing', 'left'), next)
  assert.equal(panes.dockNotesPane(next, note.id, 'left'), next)
})

test('hiding notes collapses only their slot and saved layouts retain the full arrangement', () => {
  const editor = panes.newLeaf('write')
  const root = panes.dockNotesPane(editor, editor.id, 'top')
  assert.equal(panes.withoutNotesPane(root).id, editor.id)
  assert.equal(panes.sameArrangement(root, panes.reidentify(root)), true)
  assert.equal(panes.paneLeaves(root).length, 2)
})

test('notes do not count as a surviving content pane when closing the last editor', () => {
  const editor = panes.newLeaf('write')
  const root = panes.dockNotesPane(editor, editor.id, 'right')
  assert.equal(panes.canClosePane(root, editor.id), false)
  const split = panes.splitPane(root, editor.id, 'column')
  assert.equal(panes.canClosePane(split.root, editor.id), true)
  assert.equal(panes.canClosePane(root, 'missing'), false)
})

test('filtering notes from a multi-child split preserves the matching size proportions', () => {
  const a = panes.newLeaf('write')
  const b = panes.newLeaf('codex')
  const note = panes.newLeaf('sceneNotes')
  const root = { kind: 'split', id: 'multi', direction: 'row', children: [note, a, b], sizes: [20, 30, 50] }
  const hidden = panes.withoutNotesPane(root)
  assert.deepEqual(Array.from(hidden.sizes), [37.5, 62.5])
})
