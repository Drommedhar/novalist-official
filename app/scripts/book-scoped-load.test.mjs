import assert from 'node:assert/strict'
import test from 'node:test'
import { loadBookScoped } from '../src/renderer/src/stores/bookScopedLoad.ts'

function harness() {
  let scope = {
    isLoaded: true,
    projectPath: '/projects/first',
    activeBookId: 'book-a',
    activeDraftId: 'draft-a',
    workspaceEpoch: 1,
    workspaceBusy: false
  }
  let resolve, reject
  const response = new Promise((yes, no) => { resolve = yes; reject = no })
  const applied = []
  let requests = 0
  return {
    load: () => loadBookScoped(() => scope, () => { requests++; return response }, value => applied.push(value)),
    change: patch => { scope = { ...scope, ...patch } },
    resolve: value => resolve(value),
    reject: error => reject(error),
    applied,
    requests: () => requests
  }
}

test('current book read publishes its result', async () => {
  const h = harness()
  const loaded = h.load()
  h.resolve(['stage'])
  await loaded
  assert.deepEqual(h.applied, [['stage']])
})

for (const change of [
  { projectPath: '/projects/second' },
  { activeBookId: 'book-b' },
  { activeDraftId: 'draft-b' },
  { workspaceEpoch: 2 },
  { workspaceBusy: true },
  { isLoaded: false }
]) {
  test(`old read is retired when ${Object.keys(change)[0]} changes`, async () => {
    const h = harness()
    const loaded = h.load()
    h.change(change)
    h.resolve(['old stage'])
    await loaded
    assert.deepEqual(h.applied, [])
  })
}

test('transition rejection is retired without an unhandled failure', async () => {
  const h = harness()
  const loaded = h.load()
  h.change({ workspaceBusy: true })
  h.reject(new Error('old epoch'))
  await loaded
  assert.deepEqual(h.applied, [])
})

test('failure in the current book still reaches its caller', async () => {
  const h = harness()
  const loaded = h.load()
  h.reject(new Error('disk unavailable'))
  await assert.rejects(loaded, /disk unavailable/)
})

test('busy or unloaded workspace does not start a request', async () => {
  const h = harness()
  h.change({ workspaceBusy: true })
  await h.load()
  h.change({ workspaceBusy: false, isLoaded: false })
  await h.load()
  assert.equal(h.requests(), 0)
})

test('resume starts a fresh read under the acknowledged epoch', async () => {
  const h = harness()
  h.change({ workspaceBusy: true })
  await h.load()
  h.change({ workspaceBusy: false, workspaceEpoch: 2 })
  const loaded = h.load()
  h.resolve(['new stage'])
  await loaded
  assert.equal(h.requests(), 1)
  assert.deepEqual(h.applied, [['new stage']])
})
