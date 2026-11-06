import assert from 'node:assert/strict'
import { EventEmitter } from 'node:events'
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'
import { runInNewContext } from 'node:vm'
import test from 'node:test'
import { buildSync } from 'esbuild'
import { encodeRpcFrame, RpcFrameDecoder } from '../src/shared/rpcFraming.ts'

const require = createRequire(import.meta.url)
const compiled = buildSync({
  entryPoints: [fileURLToPath(new URL('../src/main/backend-process.ts', import.meta.url))],
  bundle: true,
  platform: 'node',
  format: 'cjs',
  external: ['electron'],
  write: false
}).outputFiles[0].text

function fixture() {
  const children = []
  const errors = []
  const module = { exports: {} }
  runInNewContext(compiled, {
    module, exports: module.exports, Buffer, TextEncoder, TextDecoder, Uint8Array, __dirname: '/app/out/main',
    process: { platform: 'linux', env: { NOVALIST_SETTINGS_DIR: '/test-settings' } },
    console: { error: (...args) => errors.push(args) },
    require(name) {
      if (name === 'electron') return { app: { isPackaged: false } }
      if (name !== 'node:child_process') return require(name)
      return { spawn() {
        const child = Object.assign(new EventEmitter(), {
          kills: 0,
          frames: [],
          stdin: Object.assign(new EventEmitter(), { destroyed: false }),
          stdout: new EventEmitter(),
          stderr: new EventEmitter(),
          kill() { this.kills++; return true }
        })
        const decoder = new RpcFrameDecoder()
        child.stdin.write = frame => { child.frames.push(...decoder.push(frame)); return true }
        children.push(child)
        return child
      } }
    }
  })
  const backend = new module.exports.BackendProcess()
  backend.start()
  return { backend, children, errors }
}

const brokenPipe = () => Object.assign(new Error('write EPIPE'), { code: 'EPIPE' })

test('a backend input pipe error is contained and supervised recovery restores RPC', async () => {
  const { backend, children, errors } = fixture()
  let recoveries = 0
  backend.onRestart(async () => { recoveries++ })
  const rejected = assert.rejects(backend.router.request('system/ping'), /Backend restarted/)
  const child = children[0]
  assert.doesNotThrow(() => child.stdin.emit('error', brokenPipe()))
  assert.equal(child.kills, 1)
  child.emit('exit', 1)
  await rejected
  await backend.recover()
  assert.equal(recoveries, 1)
  assert.equal(children.length, 2)
  assert.ok(errors.some(args => args.join(' ').includes('EPIPE')))
  const reply = backend.router.request('system/ping')
  const replacement = children[1]
  replacement.stdout.emit('data', encodeRpcFrame({ jsonrpc: '2.0', id: replacement.frames.at(-1).id, result: 'pong' }))
  assert.equal(await reply, 'pong')
  backend.dispose()
})

test('a late pipe error from the exited backend cannot stop its replacement', async () => {
  const { backend, children } = fixture()
  const original = children[0]
  original.emit('exit', 0)
  await backend.recover()
  assert.doesNotThrow(() => original.stdin.emit('error', brokenPipe()))
  assert.equal(original.kills, 0)
  assert.equal(children[1].kills, 0)
  assert.equal(children.length, 2)
  backend.dispose()
})

test('a pipe error during app disposal is contained without restarting the backend', () => {
  const { backend, children } = fixture()
  backend.dispose()
  assert.doesNotThrow(() => children[0].stdin.emit('error', brokenPipe()))
  children[0].emit('exit', 0)
  assert.equal(children.length, 1)
  assert.equal(children[0].kills, 1)
})
