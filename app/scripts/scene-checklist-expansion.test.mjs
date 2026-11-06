import { test } from 'node:test'
import assert from 'node:assert/strict'
import { loadRendererSource } from './renderer-source-loader.mjs'
import { parseChecklist, normalizeChecklist } from '../src/renderer/src/shell/sceneChecklist.ts'

const context = { projectPath: '/book', bookId: 'one', draftId: 'draft', chapterGuid: 'chapter', sceneId: 'scene', epoch: 1, title: 'Scene' }
function harness(storage = new Map()) {
  const source = loadRendererSource('src/shell/checklistExpansion.ts', {}, { localStorage: {
    getItem: key => storage.get(key) ?? null,
    setItem: (key, value) => storage.set(key, value)
  } })
  return { ...source, storage }
}

test('expanded details survive a fresh reader and workspace epoch while staying scoped to their scene', () => {
  const f = harness()
  const scope = f.checklistExpansionScope(context)
  f.saveChecklistExpansion(scope, new Set(['parent', 'child', 'completed']))
  const restarted = harness(f.storage)
  assert.deepEqual([...restarted.readChecklistExpansion(restarted.checklistExpansionScope({ ...context, epoch: 8, title: 'Renamed' }))], ['parent', 'child', 'completed'])
  for (const patch of [{ projectPath: '/other' }, { bookId: 'two' }, { draftId: 'revision' }, { sceneId: 'other' }]) {
    assert.equal(restarted.readChecklistExpansion(restarted.checklistExpansionScope({ ...context, ...patch })).size, 0)
  }
  f.saveChecklistExpansion(scope, new Set(['child']))
  assert.deepEqual([...restarted.readChecklistExpansion(scope)], ['child'])
})

test('disclosure keys follow headings across normalization, checking, and unrelated inserted notes', () => {
  const f = harness()
  const old = parseChecklist('- Parent\n  - Child\n    Details\n- Other\n  Details')
  const changed = parseChecklist(normalizeChecklist('Preface\n- New item\n- [x] Parent\n  - Child\n    Edited details\n- Other\n  Details'))
  const first = f.checklistItemKeys(old)
  const second = f.checklistItemKeys(changed)
  assert.equal(first.get(0), second.get(2))
  assert.equal(first.get(1), second.get(3))
  assert.equal(first.get(3), second.get(5))
})

test('identical titles get independent keys across siblings and parents', () => {
  const f = harness()
  const keys = f.checklistItemKeys(parseChecklist('- Same\n  - Same\n- Same\n  - Same'))
  assert.equal(new Set(keys.values()).size, 4)
})

test('invalid or unavailable preferences never prevent the checklist from opening', () => {
  const f = harness()
  const scope = f.checklistExpansionScope(context)
  for (const value of ['invalid', '{}', 'null', '[true, 42, "valid"]']) {
    f.storage.set(scope, value)
    assert.deepEqual([...f.readChecklistExpansion(scope)], value.startsWith('[') ? ['valid'] : [])
  }
  const blocked = loadRendererSource('src/shell/checklistExpansion.ts', {}, { localStorage: {
    getItem() { throw new Error('Unavailable') }, setItem() { throw new Error('Full') }
  } })
  assert.equal(blocked.readChecklistExpansion(scope).size, 0)
  assert.doesNotThrow(() => blocked.saveChecklistExpansion(scope, new Set(['parent'])))
})
