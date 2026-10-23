import assert from 'node:assert/strict'
import test from 'node:test'
import { HostCallChannel } from '../src/renderer/src/mobile/hostCalls.ts'
import { loadRendererSource } from './renderer-source-loader.mjs'
import { buildSync } from 'esbuild'
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'

const output = fileURLToPath(new URL('../out/mobile-collaboration-tests/shim.cjs', import.meta.url))
buildSync({ stdin: { contents: "export * from './src/renderer/src/mobile/backend'; export * from './src/shared/rpcFraming'", resolveDir: fileURLToPath(new URL('..', import.meta.url)) },
  bundle: true, platform: 'node', format: 'cjs', outfile: output })
const { MobileBackend, encodeRpcFrame, RpcFrameDecoder } = createRequire(import.meta.url)(output)
const frame = method => encodeRpcFrame({ jsonrpc: '2.0', id: 1, method })
const message = bytes => new RpcFrameDecoder().push(bytes)[0]

const turn = () => new Promise((resolve) => setImmediate(resolve))
const encode = (value) => Buffer.from(JSON.stringify(value)).toString('base64')

function fixture(t) {
  const sent = []
  const channels = []
  const listeners = new Map()
  class Port {
    messages = []
    closed = false
    postMessage(value) { this.messages.push(value) }
    close() { this.closed = true }
    start() {}
  }
  class Channel {
    port1 = new Port()
    port2 = new Port()
    constructor() { channels.push(this) }
  }
  const window = {
    HybridWebView: { SendRawMessage: (message) => sent.push(message) },
    postMessage() {},
    addEventListener: (name, callback) => listeners.set(name, callback)
  }
  loadRendererSource('src/mobile/shim.ts', {
    i18next: { default: { t: (key) => key } },
    './projectImages': { installProjectImageLoader() {}, clearProjectImageCache() {} },
    './hostCalls': { HostCallChannel },
    './collaboration': { installCollaborationHost: () => () => {} },
    './backend': { MobileBackend }
  }, {
    window, document: { visibilityState: 'visible' }, MessageChannel: Channel,
    TextDecoder, Uint8Array, btoa, atob,
    setInterval: () => 1, clearInterval() {}, console: { error() {} }
  })
  t.after(() => listeners.get('pagehide')())
  const reply = (request, result = null) => window.__novalistHostResult(encode({ id: request.id, ok: true, result }))
  const hostRequests = () => sent.filter((message) => message.startsWith('{')).map((message) => JSON.parse(message))
  return { window, channels, sent, reply, hostRequests }
}

test('mobile RPC waits for native readiness and acknowledges byte delivery', async (t) => {
  const f = fixture(t)
  assert.equal(f.window.__novalistRecv(btoa('early')), 'unavailable')
  f.window.novalist.requestBackendPort()
  f.channels[0].port2.onmessage({ data: frame('system/ping') })
  await turn()
  assert.equal(f.sent.length, 1)
  assert.equal(f.reply(f.hostRequests()[0]), 'accepted')
  await turn()
  const request = message(Buffer.from(f.sent[1], 'base64'))
  assert.equal(request.method, 'system/ping')
  assert.match(request.id, /^nl\/1\/0\//)
  const response = encodeRpcFrame({ jsonrpc: '2.0', id: request.id, result: 'connected' })
  assert.equal(f.window.__novalistRecv(Buffer.from(response).toString('base64')), 'accepted')
  assert.deepEqual(message(f.channels[0].port2.messages[0]), { jsonrpc: '2.0', id: 1, result: 'connected' })
})

test('replaced mobile ports cannot forward writes queued before readiness', async (t) => {
  const f = fixture(t)
  f.window.novalist.requestBackendPort()
  f.channels[0].port2.onmessage({ data: frame('old') })
  f.window.novalist.requestBackendPort()
  f.channels[1].port2.onmessage({ data: frame('current') })
  f.reply(f.hostRequests()[0])
  await turn()
  assert.equal(f.channels[0].port2.closed, true)
  assert.equal(f.hostRequests().length, 1)
  const requests = f.sent.filter(value => !value.startsWith('{')).map(value => message(Buffer.from(value, 'base64')))
  assert.equal(requests.length, 1)
  assert.equal(requests[0].method, 'current')
})

test('bridge failure rejects pending and future calls and pauses the renderer', async (t) => {
  const f = fixture(t)
  f.window.novalist.requestBackendPort()
  f.reply(f.hostRequests()[0])
  const pending = assert.rejects(f.window.novalist.saveFile('book'), /connection stopped/)
  f.window.__novalistBridgeFailed()
  await pending
  const count = f.sent.length
  await assert.rejects(f.window.novalist.saveFile('retry'), /connection stopped/)
  assert.equal(f.sent.length, count)
  assert.equal(f.window.__novalistRecv(btoa('late')), 'unavailable')
  assert.equal(f.channels[0].port2.messages[0].novalistControl, 'backend-recovery-failed')
})

test('native sharing allows a human decision but keeps a bounded deadline', async (t) => {
  t.mock.timers.enable({ apis: ['setTimeout'] })
  const f = fixture(t)
  let settled = false
  const sharing = f.window.novalist.shareExport('synthetic.epub')
  sharing.then(() => { settled = true }, () => { settled = true })
  const ordinary = assert.rejects(f.window.novalist.saveFile('synthetic.epub'), /result is unknown/)
  t.mock.timers.tick(35_000)
  await ordinary
  assert.equal(settled, false)
  f.reply(f.hostRequests()[0], true)
  assert.equal(await sharing, true)

  const abandoned = assert.rejects(f.window.novalist.shareExport('synthetic.epub'), /result is unknown/)
  t.mock.timers.tick(600_000)
  await abandoned
  assert.equal(f.hostRequests().length, 3)
})

for (const fails of [false, true]) {
  test(`background save acknowledgement reports ${fails ? 'failure' : 'durable completion'}`, async (t) => {
    const f = fixture(t)
    let release
    f.window.novalist.onBackgroundSave(() => new Promise((resolve, reject) => {
      release = () => fails ? reject(new Error('write failed')) : resolve()
    }))
    f.window.__novalistLifecycleSave(42)
    await turn()
    assert.equal(f.sent.length, 0)
    release()
    await turn()
    const completion = f.hostRequests()[0]
    assert.equal(completion.method, 'backgroundSaveCompleted')
    assert.deepEqual(completion.args, [42, !fails])
    f.reply(completion)
    await turn()
  })
}
