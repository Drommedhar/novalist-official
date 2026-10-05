import assert from 'node:assert/strict'
import test from 'node:test'
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'
import { runInNewContext } from 'node:vm'
import { build } from 'esbuild'

// Exercise the production Zustand store with controllable transport and time.
// Each test gets its own module and clock, without launching an Electron window.
const compiled = (await build({
  entryPoints: [fileURLToPath(new URL('../src/renderer/src/stores/audiobookStore.ts', import.meta.url))],
  bundle: true,
  platform: 'node',
  format: 'cjs',
  write: false,
  plugins: [{
    name: 'poll-boundaries',
    setup(builder) {
      builder.onResolve({ filter: /^\.\.\/rpc\/client$/ }, () => ({ path: 'rpc', namespace: 'poll' }))
      builder.onResolve({ filter: /^\.\/projectStore$/ }, () => ({ path: 'project', namespace: 'poll' }))
      builder.onLoad({ filter: /.*/, namespace: 'poll' }, ({ path }) => ({
        contents: path === 'rpc'
          ? 'export const rpc = harness.rpc'
          : 'export const useProjectStore = { getState: () => harness.scope }',
        loader: 'js'
      }))
    }
  }]
})).outputFiles[0].text

function harness() {
  const clock = new Map()
  const requests = []
  let nextTimer = 0
  const state = {
    scope: { workspaceBusy: false, workspaceEpoch: 1 },
    rpc: { request(method) {
      return new Promise((resolve, reject) => requests.push({ method, resolve, reject }))
    } }
  }
  const module = { exports: {} }
  runInNewContext(compiled, {
    module,
    exports: module.exports,
    require: createRequire(import.meta.url),
    process,
    harness: state,
    setInterval(callback) { const id = ++nextTimer; clock.set(id, callback); return id },
    clearInterval(id) { clock.delete(id) }
  })
  return {
    store: module.exports.useAudiobookStore,
    requests,
    clock,
    change(patch) { state.scope = { ...state.scope, ...patch } },
    tick() { for (const callback of clock.values()) callback() },
    async settle() { await Promise.resolve(); await Promise.resolve() }
  }
}

test('polls pause while busy and resume in the acknowledged epoch', async () => {
  const h = harness()
  h.store.getState().watch()
  h.requests[0].resolve({ phase: 'rendering' })
  await h.settle()
  h.change({ workspaceBusy: true })
  h.tick()
  assert.equal(h.requests.length, 1)
  h.change({ workspaceBusy: false, workspaceEpoch: 2 })
  h.tick()
  assert.equal(h.requests.length, 2)
  h.requests[1].resolve({ phase: 'packaging' })
  await h.settle()
  assert.equal(h.store.getState().status.phase, 'packaging')
  assert.equal(h.clock.size, 1)
})

test('stale rejected poll cannot permanently stop the render watcher', async () => {
  const h = harness()
  h.store.getState().watch()
  h.change({ workspaceBusy: true })
  h.requests[0].reject(new Error('old epoch'))
  await h.settle()
  assert.equal(h.clock.size, 1)
  h.change({ workspaceBusy: false, workspaceEpoch: 2 })
  h.tick()
  h.requests[1].resolve({ phase: 'done' })
  await h.settle()
  assert.equal(h.store.getState().status.phase, 'done')
  assert.equal(h.clock.size, 0)
})

test('late terminal response from the old epoch cannot stop current polling', async () => {
  const h = harness()
  h.store.getState().watch()
  h.change({ workspaceEpoch: 2 })
  h.requests[0].resolve({ phase: 'done' })
  await h.settle()
  assert.equal(h.store.getState().status, null)
  assert.equal(h.clock.size, 1)
  h.tick()
  h.requests[1].resolve({ phase: 'rendering' })
  await h.settle()
  assert.equal(h.store.getState().status.phase, 'rendering')
})

test('a rejected token after an aborted transition still retries', async () => {
  const h = harness()
  h.store.getState().watch()
  h.change({ workspaceBusy: true })
  h.change({ workspaceBusy: false })
  h.requests[0].reject(new Error('save transaction expired'))
  await h.settle()
  h.tick()
  assert.equal(h.requests.length, 2)
  h.requests[1].resolve({ phase: 'stopped' })
  await h.settle()
  assert.equal(h.clock.size, 0)
})

test('refresh skips busy workspace and starts watching after resume', async () => {
  const h = harness()
  h.change({ workspaceBusy: true })
  await h.store.getState().refresh()
  assert.equal(h.requests.length, 0)
  h.change({ workspaceBusy: false, workspaceEpoch: 2 })
  const refreshed = h.store.getState().refresh()
  h.requests[0].resolve({ phase: 'rendering' })
  await refreshed
  assert.equal(h.clock.size, 1)
})

test('render status is global and continues without an open project', async () => {
  const h = harness()
  h.change({ isLoaded: false, projectPath: null })
  const refreshed = h.store.getState().refresh()
  h.requests[0].resolve({ phase: 'packaging' })
  await refreshed
  assert.equal(h.store.getState().status.phase, 'packaging')
  assert.equal(h.clock.size, 1)
})
