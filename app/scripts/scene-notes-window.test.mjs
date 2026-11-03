import { test } from 'node:test'
import assert from 'node:assert/strict'
import { clampNotesBounds, selectNotesContext } from '../src/shared/sceneNotesProtocol.ts'

const context = { projectPath: '/book', bookId: 'book', draftId: 'draft', epoch: 1, chapterGuid: 'chapter', sceneId: 'A', title: 'A' }
test('holds a scene while researching but releases it when the workspace changes', () => {
  const next = { ...context, sceneId: 'B' }
  assert.equal(selectNotesContext(context, next, false), context)
  assert.equal(selectNotesContext(context, next, true), next)
  for (const field of ['projectPath', 'bookId', 'draftId', 'epoch']) {
    const changed = { ...next, [field]: 'new' }
    assert.equal(selectNotesContext(context, changed, false), changed)
  }
  assert.equal(selectNotesContext(context, null, false), null)
})

test('recovers window bounds after a monitor disappears or shrinks', () => {
  assert.deepEqual(clampNotesBounds({ x: 2500, y: -900, width: 450, height: 600 }, { x: 0, y: 0, width: 1000, height: 700 }),
    { x: 550, y: 0, width: 450, height: 600 })
  assert.deepEqual(clampNotesBounds({ x: 0, y: 0, width: 450, height: 600 }, { x: -300, y: 0, width: 300, height: 200 }),
    { x: -300, y: 0, width: 300, height: 200 })
})
