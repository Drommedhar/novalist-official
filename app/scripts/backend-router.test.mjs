import assert from 'node:assert/strict'
import test from 'node:test'
import { fileURLToPath } from 'node:url'
import { buildSync } from 'esbuild'
import { encodeRpcFrame, RpcFrameDecoder } from '../src/shared/rpcFraming.ts'

// Compile the same source the desktop build uses; no Electron runtime is
// needed to exercise stream routing and connection ownership.
const compiled = buildSync({
  stdin: { contents: "export * from './src/main/backend-router'; export * from './src/main/workspace-windows'", resolveDir: fileURLToPath(new URL('..', import.meta.url)) },
  bundle: true,
  platform: 'node',
  format: 'esm',
  write: false
}).outputFiles[0].text
const { BackendRouter, WorkspaceWindows } = await import(`data:text/javascript;base64,${Buffer.from(compiled).toString('base64')}`)

function port() {
  return {
    received: [],
    closed: false,
    postMessage(message) { this.received.push(message) },
    close() { this.closed = true },
    messages() {
      const decoder = new RpcFrameDecoder()
      return this.received.filter(value => value instanceof Uint8Array).flatMap(value => decoder.push(value))
    }
  }
}

function harness(notification) {
  const requests = []
  const decoder = new RpcFrameDecoder()
  const router = new BackendRouter(frame => requests.push(...decoder.push(frame)), notification)
  return { router, requests }
}

test('windows with identical request IDs receive only their own replies', () => {
  const { router, requests } = harness()
  const main = port(), detached = port()
  router.attach(1, main)
  router.attach(2, detached)
  router.receive(1, main, encodeRpcFrame({ jsonrpc: '2.0', id: 1, method: 'system/ping' }))
  router.receive(2, detached, encodeRpcFrame({ jsonrpc: '2.0', id: 1, method: 'system/ping' }))
  assert.notEqual(requests[0].id, requests[1].id)
  for (const request of requests.toReversed()) {
    router.receiveBackend(encodeRpcFrame({ jsonrpc: '2.0', id: request.id, result: request.id }))
  }
  assert.deepEqual(main.messages(), [{ jsonrpc: '2.0', id: 1, result: requests[0].id }])
  assert.deepEqual(detached.messages(), [{ jsonrpc: '2.0', id: 1, result: requests[1].id }])
  assert.equal(main.closed, false)
})

test('newly attached windows do not receive another client partial response', () => {
  const { router, requests } = harness()
  const main = port(), detached = port()
  router.attach(1, main)
  router.receive(1, main, encodeRpcFrame({ jsonrpc: '2.0', id: 7, method: 'system/ping' }))
  const response = encodeRpcFrame({ jsonrpc: '2.0', id: requests[0].id, result: 'complete' })
  router.receiveBackend(response.subarray(0, response.length - 3))
  router.attach(2, detached)
  router.receiveBackend(response.subarray(response.length - 3))
  assert.equal(main.messages()[0].result, 'complete')
  assert.deepEqual(detached.messages(), [])
})

test('port replacement ignores old replies and late old-port messages', () => {
  const { router, requests } = harness()
  const old = port(), current = port()
  router.attach(1, old)
  router.receive(1, old, encodeRpcFrame({ jsonrpc: '2.0', id: 1, method: 'system/ping' }))
  router.attach(1, current)
  router.detach(1, old)
  router.receive(1, old, encodeRpcFrame({ jsonrpc: '2.0', id: 2, method: 'project/close' }))
  router.receiveBackend(encodeRpcFrame({ jsonrpc: '2.0', id: requests[0].id, result: 'old' }))
  assert.equal(old.closed, true)
  assert.equal(router.hasClient(1), true)
  assert.equal(requests.length, 1)
  assert.deepEqual(current.messages(), [])
})

test('data notifications broadcast while interactive UI has one owner', () => {
  const { router } = harness(message => message.method === 'workspace/prepare')
  const main = port(), detached = port()
  router.attach(1, main)
  router.attach(2, detached)
  for (const method of ['entities/changed', 'ui/pick/open', 'workspace/prepare']) {
    router.receiveBackend(encodeRpcFrame({ jsonrpc: '2.0', method, params: ['value'] }))
  }
  assert.deepEqual(main.messages().map(message => message.method), ['entities/changed', 'ui/pick/open'])
  assert.deepEqual(detached.messages().map(message => message.method), ['entities/changed'])
  router.setDialogOwner(2)
  router.receiveBackend(encodeRpcFrame({ jsonrpc: '2.0', method: 'ui/wizard/open' }))
  assert.equal(detached.messages().at(-1).method, 'ui/wizard/open')
})

test('epochs change only after client acknowledgement and drain tokens are scoped', () => {
  const { router, requests } = harness()
  const main = port(), detached = port()
  router.attach(1, main)
  router.attach(2, detached)
  router.prepare(1, 'save-token')
  router.receive(1, main, encodeRpcFrame({ jsonrpc: '2.0', id: 1, method: 'scenes/write' }))
  router.setEpoch(4)
  router.receive(2, detached, encodeRpcFrame({ jsonrpc: '2.0', id: 1, method: 'scenes/write' }))
  router.settle(2, 4)
  router.receive(2, detached, encodeRpcFrame({ jsonrpc: '2.0', id: 2, method: 'system/ping' }))
  assert.match(requests[0].id, /^nl\/1\/0\/\d+\/save-token$/)
  assert.match(requests[1].id, /^nl\/2\/0\/\d+\/-$/)
  assert.match(requests[2].id, /^nl\/2\/4\/\d+\/-$/)
})

test('malformed client input closes only that client', () => {
  const { router, requests } = harness()
  const broken = port(), healthy = port()
  router.attach(1, broken)
  router.attach(2, healthy)
  router.receive(1, broken, Buffer.from('Content-Length: invalid\r\n\r\n'))
  router.receive(2, healthy, encodeRpcFrame({ jsonrpc: '2.0', id: 1, method: 'system/ping' }))
  assert.equal(broken.closed, true)
  assert.equal(healthy.closed, false)
  assert.equal(requests.length, 1)
})

test('restart rejects internal calls and clears incomplete old backend frames', async () => {
  const { router, requests } = harness()
  const main = port()
  router.attach(1, main)
  const pending = router.request('workspace/snapshot')
  const rejection = assert.rejects(pending, /restarted/)
  router.receiveBackend(Buffer.from('Content-Length: 500\r\n\r\n{'))
  router.restart()
  await rejection
  assert.equal(main.received[0].novalistControl, 'backend-restarted')
  router.receiveBackend(encodeRpcFrame({ jsonrpc: '2.0', method: 'entities/changed' }))
  assert.equal(main.messages()[0].method, 'entities/changed')
  assert.match(requests[0].id, /^nl\/0\/0\//)
})

test('recovery rejects writes without replay and resumes independent clients only when restored', async () => {
  const { router, requests } = harness()
  const main = port(), detached = port()
  router.attach(1, main)
  router.attach(2, detached)
  router.beginRecovery()
  router.receive(1, main, encodeRpcFrame({ jsonrpc: '2.0', id: 1, method: 'scenes/write', params: ['old prose'] }))
  assert.equal(requests.length, 0)
  assert.match(main.messages()[0].error.message, /restoring/)
  const restore = router.request('project/open', ['/book'])
  router.receiveBackend(encodeRpcFrame({ jsonrpc: '2.0', id: requests[0].id, result: true }))
  await restore
  router.failRecovery('disk unavailable')
  assert.equal(detached.received.at(-1).error, 'disk unavailable')
  router.finishRecovery()
  router.receive(2, detached, encodeRpcFrame({ jsonrpc: '2.0', id: 2, method: 'system/ping' }))
  assert.equal(requests.length, 2)
  assert.equal(requests[1].method, 'system/ping')
  assert.equal(detached.received.at(-1).novalistControl, 'backend-restarted')
})

test('real broker holds old replies until every resume ACK while fresh scene reads remain live', async () => {
  const requests = [], decoder = new RpcFrameDecoder(), events = []
  let windows
  const router = new BackendRouter(frame => requests.push(...decoder.push(frame)), message => windows.receive(message))
  windows = new WorkspaceWindows(router, 1000)
  const main = port(), detached = port()
  for (const [owner, client] of [[1, main], [2, detached]]) {
    router.attach(owner, client)
    windows.register(owner, { send(event) { events.push({ owner, event }) } })
  }
  router.receive(1, main, encodeRpcFrame({ jsonrpc: '2.0', id: 1, method: 'project/switchBook' }))
  router.receive(1, main, encodeRpcFrame({ jsonrpc: '2.0', id: 2, method: 'project/recent' }))
  const original = requests[0].id, priorRead = requests[1].id
  router.receiveBackend(encodeRpcFrame({ jsonrpc: '2.0', method: 'workspace/changed', params: [{ token: 'a', epoch: 1, state: {}, draftId: null }] }))
  windows.acknowledge(1, 'a', 'changed', true)
  windows.acknowledge(2, 'a', 'changed', true)
  await new Promise(resolve => setImmediate(resolve))
  const applied = requests.find(request => request.method === 'workspace/applied')
  router.receiveBackend(encodeRpcFrame({ jsonrpc: '2.0', id: applied.id, result: null }))
  router.receiveBackend(encodeRpcFrame({ jsonrpc: '2.0', id: original, result: 'changed' }))
  router.receiveBackend(encodeRpcFrame({ jsonrpc: '2.0', id: priorRead, result: 'old read' }))
  await new Promise(resolve => setImmediate(resolve))
  assert.equal(events.filter(({ event }) => event.phase === 'resumed').length, 2)
  router.receive(2, detached, encodeRpcFrame({ jsonrpc: '2.0', id: 3, method: 'scenes/read' }))
  router.receiveBackend(encodeRpcFrame({ jsonrpc: '2.0', id: requests.at(-1).id, result: 'fresh scene' }))
  assert.deepEqual(detached.messages().map(message => message.result), ['fresh scene'])
  windows.acknowledge(1, 'a', 'resumed', true)
  await new Promise(resolve => setImmediate(resolve))
  assert.deepEqual(main.messages(), [])
  windows.acknowledge(2, 'a', 'resumed', true)
  await new Promise(resolve => setImmediate(resolve))
  assert.deepEqual(main.messages().map(message => [message.id, message.result]), [[1, 'changed'], [2, 'old read']])
})

test('a failed resume rejects held requests and ignores their eventual stale successes', async () => {
  const { router, requests } = harness()
  const client = port()
  router.attach(1, client)
  router.receive(1, client, encodeRpcFrame({ jsonrpc: '2.0', id: 1, method: 'project/switchDraft' }))
  const id = requests[0].id
  router.holdClientReplies()
  router.rejectHeldReplies('Could not resume a window')
  router.receiveBackend(encodeRpcFrame({ jsonrpc: '2.0', id, result: 'too late' }))
  assert.equal(client.messages().length, 1)
  assert.match(client.messages()[0].error.message, /Could not resume/)
})
