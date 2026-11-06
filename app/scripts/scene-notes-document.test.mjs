import { test } from 'node:test'
import assert from 'node:assert/strict'
import { SceneNotesDocument } from '../src/renderer/src/shell/sceneNotesDocument.ts'

test('serializes writes and flushes edits made while a previous write is pending', async () => {
  let release
  const writes = []
  const doc = new SceneNotesDocument(async () => 'original', async (text) => {
    writes.push(text)
    if (text === 'first') await new Promise((resolve) => { release = resolve })
  })
  await doc.load()
  doc.update('first')
  const saving = doc.flush()
  await Promise.resolve()
  doc.update('second')
  release()
  await saving
  assert.deepEqual(writes, ['first', 'second'])
  assert.equal(doc.dirty, false)
})

test('failed saves retain the latest text and retry it', async () => {
  let failing = true
  const writes = []
  const doc = new SceneNotesDocument(async () => '', async (text) => {
    if (failing) throw new Error('offline')
    writes.push(text)
  })
  await doc.load()
  doc.update('Keep my edit')
  await assert.rejects(doc.flush(), /offline/)
  assert.equal(doc.state.text, 'Keep my edit')
  assert.equal(doc.dirty, true)
  assert.match(doc.state.error, /offline/)
  failing = false
  await doc.flush()
  assert.deepEqual(writes, ['Keep my edit'])
  assert.equal(doc.state.error, null)
})

test('a document keeps its original scene writer when another scene opens', async () => {
  const writes = []
  const first = new SceneNotesDocument(async () => 'A', async (text) => { writes.push(['A', text]) })
  const next = new SceneNotesDocument(async () => 'B', async (text) => { writes.push(['B', text]) })
  await first.load()
  first.update('A edited')
  await next.load()
  await first.flush()
  assert.deepEqual(writes, [['A', 'A edited']])
  assert.equal(next.state.text, 'B')
})

test('cancelled loads cannot introduce edits after a workspace has unmounted', async () => {
  let resolve
  const doc = new SceneNotesDocument(() => new Promise((r) => { resolve = r }), async () => {})
  const loading = doc.load()
  doc.cancelLoad()
  resolve('stale')
  await loading
  assert.equal(doc.state.loaded, false)
  assert.equal(doc.state.text, '')
})
