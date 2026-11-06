import assert from 'node:assert/strict'
import test from 'node:test'
import { encodeRpcFrame, RpcFrameDecoder } from '../src/shared/rpcFraming.ts'
import { loadRendererSource } from './renderer-source-loader.mjs'

function client() {
  const errors = []
  const { RpcClient } = loadRendererSource('src/rpc/client.ts', {
    '../../../shared/rpcFraming': { encodeRpcFrame, RpcFrameDecoder }
  }, { window: new EventTarget(), console: { error: (...args) => errors.push(args) } })
  const messages = []
  const decoder = new RpcFrameDecoder()
  const port = { postMessage: data => messages.push(...decoder.push(data)), close() {} }
  const rpc = new RpcClient()
  rpc.attach(port)
  return { rpc, messages, errors,
    respond: (id, result) => port.onmessage({ data: encodeRpcFrame({ jsonrpc: '2.0', id, result }) }),
    reject: id => port.onmessage({ data: encodeRpcFrame({ jsonrpc: '2.0', id, error: { code: -1, message: 'failed' } }) }) }
}

test('aborting a pending read cancels its backend request without affecting the next read', async () => {
  const h = client()
  const controller = new AbortController()
  const pending = h.rpc.request('dashboard/get', [1], { signal: controller.signal })
  const cancelled = assert.rejects(pending, { name: 'AbortError' })
  controller.abort()
  await cancelled
  assert.deepEqual(h.messages[1], { jsonrpc: '2.0', method: '$/cancelRequest', params: { id: 1 } })
  const scene = h.rpc.request('scenes/read', ['chapter', 'scene'])
  h.respond(1, 'late cancelled result')
  h.respond(2, 'scene text')
  assert.equal(await scene, 'scene text')
})

test('completed and pre-aborted requests never emit a spurious cancellation', async () => {
  const h = client()
  const controller = new AbortController()
  const pending = h.rpc.request('scenes/read', [], { signal: controller.signal })
  h.respond(1, 'ready')
  assert.equal(await pending, 'ready')
  controller.abort()
  await assert.rejects(h.rpc.request('dashboard/get', [1], { signal: controller.signal }), { name: 'AbortError' })
  assert.equal(h.messages.length, 1)
})

test('completion observers see successful requests only and can unsubscribe', async () => {
  const h = client()
  const completed = []
  const unsubscribe = h.rpc.onRequestCompleted(method => completed.push(method))
  const success = h.rpc.request('entities/update', [])
  assert.deepEqual(completed, [])
  h.respond(1, 'saved')
  assert.equal(await success, 'saved')
  assert.deepEqual(completed, ['entities/update'])
  const failed = assert.rejects(h.rpc.request('entities/delete', []), /failed/)
  h.reject(2)
  await failed
  unsubscribe()
  const ignored = h.rpc.request('entities/create', [])
  h.respond(3, 'created')
  await ignored
  assert.deepEqual(completed, ['entities/update'])
})

test('a failing observer cannot reject a successful RPC or prevent another observer running', async () => {
  const h = client()
  const completed = []
  h.rpc.onRequestCompleted(() => { throw new Error('observer failed') })
  h.rpc.onRequestCompleted(method => completed.push(method))
  const pending = h.rpc.request('entities/update', [])
  h.respond(1, 'saved')
  assert.equal(await pending, 'saved')
  assert.deepEqual(completed, ['entities/update'])
  assert.match(h.errors[0][1].message, /observer failed/)
})
