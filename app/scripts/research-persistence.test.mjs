import test from 'node:test'
import assert from 'node:assert/strict'
import { loadRendererSource } from './renderer-source-loader.mjs'

const turn = () => new Promise(resolve => setImmediate(resolve))
const note = id => ({ id, title: id, type: 'Note', content: 'base', tags: [], entityRefs: [], status: 'None', rating: 0, relatedIds: [], fileSize: '', modified: '' })

function fixture(request, recovery = {}) {
  const state = [], cleanups = [], timers = new Map()
  const react = {
    useState(initial) {
      const index = state.length
      state.push(initial)
      return [initial, next => { state[index] = typeof next === 'function' ? next(state[index]) : next }]
    },
    useRef: current => ({ current }), useCallback: fn => fn,
    useEffect: fn => { const cleanup = fn(); if (cleanup) cleanups.push(cleanup) }
  }
  const pending = loadRendererSource('src/stores/pendingWrites.ts')
  const module = loadRendererSource('src/views/library/useResearchDrafts.ts', {
    react, '../../rpc/client': { rpc: { request } }, '../../stores/pendingWrites': pending,
    '../../stores/projectStore': { useProjectStore: { getState: () => ({ projectPath: '/project' }) } },
    '../../mobile/recovery': { mobileRecoveryScope: () => recovery.scope ?? null, registerResearchRecovery: () => () => {} },
    '../../mobile/recoveryJournal': recovery.journal ?? { researchValue: item => item }
  }, { setTimeout: fn => { const id = Symbol(); timers.set(id, fn); return id }, clearTimeout: id => timers.delete(id) })
  return { hook: module.useResearchDrafts(), items: () => state[0], error: () => state[1], unmount: () => cleanups.forEach(fn => fn()), pending }
}

test('a delayed note acknowledgement preserves later status, rating and links', async () => {
  let release
  const gate = new Promise(resolve => { release = resolve })
  const original = note('A')
  const f = fixture(async (_method, args) => { await gate; return [{ ...original, content: args[3] }] })
  f.hook.setItems([original])
  f.hook.patchItem('A', { content: 'draft' })
  const saving = f.hook.save(f.items()[0])
  await turn()
  f.hook.setItems([{ ...f.items()[0], status: 'Open', rating: 4, relatedIds: ['B'] }])
  release()
  await saving
  assert.equal(f.items()[0].content, 'draft')
  assert.equal(f.items()[0].status, 'Open')
  assert.equal(f.items()[0].rating, 4)
  assert.deepEqual(f.items()[0].relatedIds, ['B'])
  f.unmount()
})

test('unmount retains every dirty note when the first write fails', async () => {
  let failing = true
  const persisted = new Map()
  const f = fixture(async (_method, args) => {
    if (failing) throw new Error('disk unavailable')
    persisted.set(args[0], { title: args[1], content: args[3] })
    return [{ ...note(args[0]), title: args[1], content: args[3] }]
  })
  f.hook.setItems([note('A'), note('B')])
  f.hook.patchItem('A', { title: 'First title', content: 'draft-A' })
  f.hook.patchItem('B', { title: 'Second title', content: 'draft-B' })
  f.unmount()
  await turn()
  await assert.rejects(f.pending.flushPendingWrites(), /disk unavailable/)
  failing = false
  await f.pending.flushPendingWrites()
  assert.deepEqual([...persisted], [['A', { title: 'First title', content: 'draft-A' }], ['B', { title: 'Second title', content: 'draft-B' }]])
})

test('one successful note does not clear the error from a failed sibling', async () => {
  const f = fixture(async (_method, args) => {
    if (args[0] === 'A') throw new Error('first note failed')
    await turn()
    return [{ ...note('B'), content: args[3] }]
  })
  f.hook.setItems([note('A'), note('B')])
  f.hook.patchItem('A', { content: 'draft-A' })
  f.hook.patchItem('B', { content: 'draft-B' })
  await assert.rejects(f.hook.retrySave(), /first note failed/)
  assert.match(f.error(), /first note failed/)
  f.unmount()
  await turn()
})

test('an old hook acknowledgement cannot erase the remounted note recovery journal', async () => {
  const storage = new Map()
  const journal = loadRendererSource('src/mobile/recoveryJournal.ts', {}, { localStorage: {
    getItem: key => storage.get(key) ?? null, setItem: (key, value) => storage.set(key, value), removeItem: key => storage.delete(key)
  } })
  const recovery = { journal, scope: { projectPath: '/project', bookId: 'book', draftId: 'draft' } }
  let release
  const gate = new Promise(resolve => { release = resolve })
  const first = fixture(async (_method, args) => { await gate; return [{ ...note('A'), content: args[3] }] }, recovery)
  first.hook.setItems([note('A')])
  first.hook.patchItem('A', { content: 'old pending save' })
  const saving = first.hook.save(first.items()[0])
  await turn()
  first.unmount()
  const second = fixture(async (_method, args) => [{ ...note('A'), content: args[3] }], recovery)
  second.hook.setItems([note('A')])
  second.hook.patchItem('A', { content: 'new remounted draft' })
  release()
  await saving
  await first.pending.flushPendingWrites()
  assert.equal(journal.readRecoveryJournal()[0]?.draft.content, 'new remounted draft')
  await second.hook.retrySave()
  assert.equal(journal.readRecoveryJournal().length, 0)
  second.unmount()
})
