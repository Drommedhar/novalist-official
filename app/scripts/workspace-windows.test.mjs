import assert from 'node:assert/strict'
import test from 'node:test'
import { fileURLToPath } from 'node:url'
import { buildSync } from 'esbuild'

const source = buildSync({
  stdin: { contents: "export * from './src/main/workspace-windows'; export * from './src/main/window-close'", resolveDir: fileURLToPath(new URL('..', import.meta.url)) },
  bundle: true, platform: 'node', format: 'esm', write: false
}).outputFiles[0].text
const { WorkspaceWindows, WindowCloseCoordinator } = await import(`data:text/javascript;base64,${Buffer.from(source).toString('base64')}`)
const tick = () => new Promise(resolve => setImmediate(resolve))

function setup(timeout = 1000, autoResume = true) {
  const events = new Map([[1, []], [2, []]])
  const calls = [], settled = [], prepared = []
  const router = {
    held: false,
    holdClientReplies() { this.held = true },
    releaseClientReplies() { this.held = false },
    rejectHeldReplies(error) { this.held = false; this.rejected = error },
    prepare(owner, token) { prepared.push([owner, token]) },
    settle(owner, epoch) { settled.push([owner, epoch]) },
    setEpoch(epoch) { this.epoch = epoch },
    async request(method, params) { calls.push({ method, params }); return { epoch: 7, state: {}, draftId: null } }
  }
  const windows = new WorkspaceWindows(router, timeout)
  for (const [owner, sent] of events) windows.register(owner, { send(event) {
    sent.push(event)
    if (autoResume && ['resumed', 'aborted'].includes(event.phase)) queueMicrotask(() => windows.acknowledge(owner, event.token, event.phase, true))
  } })
  const send = (method, payload) => windows.receive({ jsonrpc: '2.0', method, params: [payload] })
  return { windows, router, events, calls, settled, prepared, send }
}

test('prepare requires every owner and ignores a forged or stale acknowledgement', async () => {
  const h = setup()
  h.send('workspace/prepare', { token: 'a', epoch: 0, reason: 'project/open' })
  assert.deepEqual(h.prepared, [[1, 'a'], [2, 'a']])
  h.windows.acknowledge(1, 'a', 'prepare', true)
  h.windows.acknowledge(2, 'wrong', 'prepare', true)
  h.windows.acknowledge(9, 'a', 'prepare', true)
  await tick()
  assert.equal(h.calls.length, 0)
  h.windows.acknowledge(2, 'a', 'prepare', true)
  await tick()
  assert.deepEqual(h.calls, [{ method: 'workspace/prepared', params: ['a', true, null] }])
})

test('one refused save rejects prepare and abort resumes every original epoch', async () => {
  const h = setup()
  h.send('workspace/prepare', { token: 'a', epoch: 4, reason: 'project/close' })
  h.windows.acknowledge(1, 'a', 'prepare', true)
  h.windows.acknowledge(2, 'a', 'prepare', false, 'disk full')
  await tick()
  assert.equal(h.calls[0].params[1], false)
  assert.match(h.calls[0].params[2], /disk full/)
  h.send('workspace/aborted', { token: 'a', epoch: 4 })
  assert.deepEqual(h.settled, [[1, 4], [2, 4]])
  assert.equal(h.events.get(1).at(-1).phase, 'aborted')
})

test('apply advances only acknowledged owners and resumes after all are applied', async () => {
  const h = setup()
  h.send('workspace/changed', { token: 'a', epoch: 5, state: {}, draftId: null })
  h.windows.acknowledge(1, 'a', 'changed', true)
  await tick()
  assert.deepEqual(h.settled, [[1, 5]])
  assert.equal(h.calls.length, 0)
  assert.equal(h.events.get(1).length, 1)
  h.windows.acknowledge(2, 'a', 'changed', true)
  await tick()
  assert.deepEqual(h.calls, [{ method: 'workspace/applied', params: ['a'] }])
  assert.equal(h.events.get(1).at(-1).phase, 'resumed')
  assert.equal(h.events.get(2).at(-1).phase, 'resumed')
})

test('failed apply keeps every surface frozen and never acknowledges stale UI', async () => {
  const h = setup()
  h.send('workspace/changed', { token: 'a', epoch: 5, state: {}, draftId: null })
  h.windows.acknowledge(1, 'a', 'changed', true)
  h.windows.acknowledge(2, 'a', 'changed', false)
  await tick()
  assert.deepEqual(h.settled, [[1, 5]])
  assert.equal(h.calls.length, 0)
  assert.equal(h.events.get(1).length, 1)
})

test('unresponsive save fails within the deadline', async () => {
  const h = setup(10)
  h.send('workspace/prepare', { token: 'a', epoch: 0, reason: 'project/open' })
  await new Promise(resolve => setTimeout(resolve, 30))
  assert.equal(h.calls[0].params[1], false)
})

test('destroyed window cannot strand a remaining window or consume another ACK', async () => {
  const h = setup()
  h.send('workspace/prepare', { token: 'a', epoch: 0, reason: 'project/open' })
  h.windows.remove(2)
  h.windows.acknowledge(1, 'a', 'prepare', true)
  await tick()
  assert.equal(h.calls[0].params[1], true)
  h.send('workspace/aborted', { token: 'a', epoch: 0 })
  assert.deepEqual(await h.windows.snapshot(1), { epoch: 7, state: {}, draftId: null })
  assert.deepEqual(h.settled.at(-1), [1, 7])
})

test('close flushes all windows before backup and accepts only owner-matched replies', async () => {
  const close = new WindowCloseCoordinator(1000)
  const sent = []
  for (const owner of [1, 2]) close.register(owner, { send(token, stage) { sent.push({ owner, token, stage }) }, focus() {} })
  const operation = close.prepare([1, 2], 1)
  assert.deepEqual(sent.map(item => item.stage), ['flush', 'flush'])
  close.acknowledge(1, sent[0].token, true)
  await tick()
  assert.equal(sent.length, 2)
  close.acknowledge(2, sent[1].token, true)
  await tick()
  assert.equal(sent[2].stage, 'backup')
  close.acknowledge(1, sent[2].token, true)
  await operation
})

test('failed close resumes every window and focuses the failed owner', async () => {
  const close = new WindowCloseCoordinator(1000)
  const sent = [], focused = []
  for (const owner of [1, 2]) close.register(owner, { send(token, stage) { sent.push({ owner, token, stage }) }, focus() { focused.push(owner) } })
  const operation = close.prepare([1, 2], 1)
  const rejected = assert.rejects(operation, /could not save/)
  close.acknowledge(1, sent[0].token, true)
  close.acknowledge(2, sent[1].token, false)
  await rejected
  assert.deepEqual(focused, [2])
  assert.deepEqual(sent.map(item => item.stage), ['flush', 'flush', 'abort', 'abort'])
})

test('renderer crash resolves its impossible close wait without skipping other windows', async () => {
  const close = new WindowCloseCoordinator(1000)
  const sent = []
  for (const owner of [1, 2]) close.register(owner, { send(token, stage) { sent.push({ owner, token, stage }) }, focus() {} })
  const operation = close.prepare([1, 2], 1)
  close.remove(1)
  close.acknowledge(2, sent[1].token, true)
  await operation
  assert.equal(sent.length, 2)
})

test('global quit queued behind detached close gets a new complete preflight', async () => {
  const close = new WindowCloseCoordinator(1000)
  const sent = []
  for (const owner of [1, 2]) close.register(owner, { send(token, stage) { sent.push({ owner, token, stage }) }, focus() {} })
  const detached = close.prepare([2])
  const global = close.prepare([1, 2], 1)
  close.acknowledge(2, sent[0].token, true)
  await detached
  await tick()
  assert.deepEqual(sent.map(item => [item.owner, item.stage]), [[2, 'flush'], [1, 'flush'], [2, 'flush']])
  close.acknowledge(1, sent[1].token, true)
  close.acknowledge(2, sent[2].token, true)
  await tick()
  assert.equal(sent[3].stage, 'backup')
  close.acknowledge(1, sent[3].token, true)
  await global
})

test('window joining during prepare receives no stale bootstrap snapshot', async () => {
  const h = setup()
  h.send('workspace/prepare', { token: 'a', epoch: 0, reason: 'project/open' })
  const joined = []
  h.windows.register(3, { send(event) { joined.push(event) } })
  const snapshot = h.windows.snapshot(3)
  await tick()
  assert.equal(h.calls.length, 0)
  h.windows.acknowledge(1, 'a', 'prepare', true)
  h.windows.acknowledge(2, 'a', 'prepare', true)
  await tick()
  h.send('workspace/changed', { token: 'a', epoch: 7, state: {}, draftId: null })
  h.windows.acknowledge(1, 'a', 'changed', true)
  h.windows.acknowledge(2, 'a', 'changed', true)
  await snapshot
  assert.deepEqual(joined, [])
  assert.equal(h.calls.at(-1).method, 'workspace/snapshot')
  assert.deepEqual(h.settled.at(-1), [3, 7])
})

test('backend recovery restores the acknowledged project, book and draft before snapshot', async () => {
  const h = setup()
  const state = { projectPath: '/project', activeBookId: 'book' }
  h.router.request = async function (method, params) {
    h.calls.push({ method, params })
    if (method === 'workspace/snapshot') return { epoch: 0, state, draftId: 'draft' }
  }
  await h.windows.snapshot(1)
  h.calls.length = 0
  await h.windows.recover()
  assert.deepEqual(h.calls.map(call => [call.method, call.params]), [
    ['project/open', ['/project', 'book']], ['project/switchDraft', ['draft']], ['workspace/snapshot', undefined]
  ])
})

test('failed backend restoration does not replace the acknowledged recovery target', async () => {
  const h = setup()
  let refuse = false
  const state = { projectPath: '/project', activeBookId: 'book' }
  h.router.request = async (method, params) => {
    h.calls.push({ method, params })
    if (method === 'project/open' && refuse) throw new Error('unavailable')
    if (method === 'workspace/snapshot') return { epoch: 0, state, draftId: 'draft' }
  }
  await h.windows.snapshot(1)
  refuse = true
  await assert.rejects(h.windows.recover(), /unavailable/)
  await assert.rejects(h.windows.snapshot(2), /restoring/)
  refuse = false
  await h.windows.recover()
  assert.deepEqual(h.calls.filter(call => call.method === 'project/open').map(call => call.params), [['/project', 'book'], ['/project', 'book']])
})

test('crash cannot release a waiting bootstrap snapshot into partially restored draft state', async () => {
  const h = setup()
  const state = { projectPath: '/project', activeBookId: 'book' }
  let releaseOpen, refuseDraft = true
  h.router.request = async (method, params) => {
    h.calls.push({ method, params })
    if (method === 'project/open') await new Promise(resolve => { releaseOpen = resolve })
    if (method === 'project/switchDraft' && refuseDraft) throw new Error('draft unavailable')
    if (method === 'workspace/snapshot') return { epoch: 0, state, draftId: 'required-draft' }
  }
  await h.windows.snapshot(1)
  h.send('workspace/prepare', { token: 'a', epoch: 0, reason: 'project/open' })
  const waiting = assert.rejects(h.windows.snapshot(2), /changed while connecting/)
  const recovery = assert.rejects(h.windows.recover(), /draft unavailable/)
  await tick()
  assert.equal(h.calls.filter(call => call.method === 'workspace/snapshot').length, 1)
  releaseOpen()
  await recovery
  await waiting
  refuseDraft = false
  const retry = h.windows.recover()
  releaseOpen()
  await retry
  assert.deepEqual(h.calls.filter(call => call.method === 'project/switchDraft').map(call => call.params), [['required-draft'], ['required-draft']])
})

test('snapshot reply followed immediately by prepare cannot settle a client into old context', async () => {
  const h = setup()
  let reply
  h.router.request = async (method, params) => {
    h.calls.push({ method, params })
    if (method === 'workspace/snapshot') return await new Promise(resolve => { reply = resolve })
  }
  const joining = h.windows.snapshot(1)
  reply({ epoch: 0, state: {}, draftId: 'old' })
  h.send('workspace/prepare', { token: 'a', epoch: 0, reason: 'project/open' })
  await tick()
  assert.deepEqual(h.settled, [])
  h.windows.acknowledge(1, 'a', 'prepare', true)
  h.windows.acknowledge(2, 'a', 'prepare', true)
  await tick()
  h.send('workspace/changed', { token: 'a', epoch: 1, state: {}, draftId: 'new' })
  h.windows.acknowledge(1, 'a', 'changed', true)
  h.windows.acknowledge(2, 'a', 'changed', true)
  await tick()
  reply({ epoch: 1, state: {}, draftId: 'new' })
  assert.equal((await joining).draftId, 'new')
  assert.ok(h.settled.every(([, epoch]) => epoch === 1))
})

test('a new transition during resume is refused without consuming the old ACKs', async () => {
  const h = setup(1000, false)
  h.send('workspace/changed', { token: 'a', epoch: 1, state: {}, draftId: null })
  h.windows.acknowledge(1, 'a', 'changed', true)
  h.windows.acknowledge(2, 'a', 'changed', true)
  await tick()
  h.send('workspace/prepare', { token: 'b', epoch: 1, reason: 'project/open' })
  await tick()
  assert.deepEqual(h.calls.at(-1).params.slice(0, 2), ['b', false])
  h.send('workspace/aborted', { token: 'b', epoch: 1 })
  assert.equal(h.router.held, true)
  h.windows.acknowledge(1, 'a', 'resumed', true)
  h.windows.acknowledge(2, 'a', 'resumed', true)
  await tick()
  assert.equal(h.router.held, false)
})

test('coordination retry only reads current snapshot without reopening a project', async () => {
  const h = setup()
  await h.windows.resync()
  assert.deepEqual(h.calls.map(call => call.method), ['workspace/snapshot'])
})
