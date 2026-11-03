import assert from 'node:assert/strict'
import test from 'node:test'
import { buildSync } from 'esbuild'
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'

const output = fileURLToPath(new URL('../out/mobile-collaboration-tests/workspace.cjs', import.meta.url))
buildSync({ stdin: { contents: "export * from './src/renderer/src/mobile/backend'; export * from './src/shared/rpcFraming'", resolveDir: fileURLToPath(new URL('..', import.meta.url)) },
  bundle: true, platform: 'node', format: 'cjs', outfile: output })
const { MobileBackend, RpcFrameDecoder, encodeRpcFrame } = createRequire(import.meta.url)(output)
const turn = () => new Promise(resolve => setImmediate(resolve))

test('mobile freezes and flushes before a peer joins, then applies the workspace before resuming', async () => {
  const outgoing = []
  const decoder = new RpcFrameDecoder()
  const phases = []
  const backend = new MobileBackend(frame => outgoing.push(...decoder.push(frame)), error => { throw new Error(error) })
  const port = { postMessage() {}, close() {} }
  backend.attach(port)
  let save
  backend.subscribe(async event => {
    phases.push(event.phase)
    if (event.phase === 'prepare') await new Promise(resolve => { save = resolve })
  })
  const receive = (method, params) => backend.receive(encodeRpcFrame({ jsonrpc: '2.0', method, params: [params] }))
  receive('workspace/prepare', { token: 'join', epoch: 0, reason: 'collaboration/hostSync' })
  await turn()
  assert.deepEqual(phases, ['prepare'])
  assert.equal(outgoing.length, 0)
  save()
  await turn()
  assert.equal(outgoing[0].method, 'workspace/prepared')
  assert.deepEqual(outgoing[0].params, ['join', true, null])
  backend.receive(encodeRpcFrame({ jsonrpc: '2.0', id: outgoing[0].id, result: null }))
  receive('workspace/changed', { token: 'join', epoch: 1, state: { isLoaded: true }, draftId: null })
  await turn()
  assert.deepEqual(phases, ['prepare', 'changed'])
  assert.equal(outgoing[1].method, 'workspace/applied')
  backend.receive(encodeRpcFrame({ jsonrpc: '2.0', id: outgoing[1].id, result: null }))
  await turn()
  assert.deepEqual(phases, ['prepare', 'changed', 'resumed'])
  backend.send(port, encodeRpcFrame({ jsonrpc: '2.0', id: 1, method: 'scenes/read', params: [] }))
  assert.match(outgoing[2].id, /^nl\/1\/1\//)
  backend.dispose()
})
