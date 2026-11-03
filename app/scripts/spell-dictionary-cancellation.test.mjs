import assert from 'node:assert/strict'
import test from 'node:test'
import { loadRendererSource } from './renderer-source-loader.mjs'

const turn = () => new Promise(resolve => setImmediate(resolve))

function hookLifecycle() {
  const slots = []
  let cursor = 0
  let pending = []
  return {
    react: {
      useRef(value) { const index = cursor++; return slots[index] ??= { current: value } },
      useEffect(effect, deps) {
        const index = cursor++, previous = slots[index]
        if (previous && deps.every((value, i) => Object.is(value, previous.deps[i]))) return
        pending.push(() => {
          previous?.cleanup?.()
          slots[index] = { deps, cleanup: effect() }
        })
      }
    },
    render(hook) {
      cursor = 0
      pending = []
      hook()
      pending.forEach(effect => effect())
      return () => { slots.forEach(slot => slot.cleanup?.()); slots.length = 0 }
    }
  }
}

function fixture() {
  const calls = [], applied = [], lifecycle = hookLifecycle()
  const retries = new Map()
  let shell = { mobileLayout: 'phone', mobileTab: 'manuscript', mainView: 'write' }
  let project = { workspaceBusy: false, workspaceEpoch: 1, openSceneId: 'scene' }
  const window = { novalist: {
    isMobile: true,
    applySpellCheck: async (...args) => applied.push(args),
    setSpellCheckMenuLabels() {}, onSpellCheckWordAdded() {}
  } }
  const background = loadRendererSource('src/rpc/backgroundReads.ts', {}, { window })
  const rpc = { request(method, params, options) {
    return new Promise((resolve, reject) => {
      calls.push({ method, params, signal: options?.signal, resolve, reject })
      options?.signal.addEventListener('abort', () => reject(options.signal.reason), { once: true })
    })
  } }
  const dictionary = loadRendererSource('src/proofing/spellDictionary.ts', {
    '../rpc/client': { rpc }, '../rpc/backgroundReads': background
  }, { AbortController })
  const hook = loadRendererSource('src/shell/useSpellCheck.ts', {
    react: lifecycle.react,
    'react-i18next': { useTranslation: () => ({ t: value => value }) },
    '../rpc/client': { rpc }, '../proofing/spellDictionary': dictionary,
    '../stores/settingsStore': { useSettingsStore: select => select({ view: { effective: {} } }) },
    '../stores/projectStore': { useProjectStore: select => select(project) },
    '../stores/shellStore': { useShellStore: select => select(shell) }
  }, { window, AbortController,
    setTimeout: (callback, delay) => { assert.equal(delay, 1000); retries.set(callback, callback); return callback },
    clearTimeout: timer => retries.delete(timer) })
  return { calls, applied, background, dictionary, setShell: value => { shell = { ...shell, ...value } },
    setProject: value => { project = { ...project, ...value } },
    retry: () => { const callbacks = [...retries.values()]; retries.clear(); callbacks.forEach(callback => callback()) },
    render: () => lifecycle.render(hook.useSpellCheck) }
}

test('navigation cancels a background dictionary request and completion unregisters it', async () => {
  const f = fixture()
  const pending = f.dictionary.loadSpellDictionary()
  assert.ok(f.calls[0].signal)
  const cancelled = assert.rejects(pending)
  f.background.cancelBackgroundReads()
  await cancelled
  assert.equal(f.calls[0].signal.aborted, true)
  const completed = f.dictionary.loadSpellDictionary()
  f.calls[1].resolve(['Mira'])
  assert.deepEqual(await completed, ['Mira'])
  f.background.cancelBackgroundReads()
  assert.equal(f.calls[1].signal.aborted, false)
})

test('shell cleanup aborts dictionary work without applying an obsolete response', async () => {
  const f = fixture()
  const cleanup = f.render()
  assert.equal(f.calls.length, 1)
  cleanup()
  await turn()
  assert.equal(f.calls[0].signal.aborted, true)
  assert.equal(f.applied.length, 0)
})

test('mobile dictionary waits until the first scene has opened', async () => {
  const f = fixture()
  f.setProject({ openSceneId: null })
  f.render()()
  assert.equal(f.calls.length, 0)

  f.setProject({ openSceneId: 'first-scene' })
  const cleanup = f.render()
  assert.equal(f.calls.length, 1)
  f.calls[0].resolve(['Mira'])
  await turn()
  assert.equal(f.applied.length, 1)
  cleanup()
})

test('a dictionary cancelled by scene navigation retries only after the next scene opens', async () => {
  const f = fixture()
  f.render()
  f.background.cancelBackgroundReads()
  await turn()
  f.render()
  assert.equal(f.calls.length, 1)
  assert.equal(f.calls[0].signal.aborted, true)

  f.setProject({ openSceneId: 'next-scene' })
  const cleanup = f.render()
  assert.equal(f.calls.length, 2)
  f.calls[1].resolve(['Mira'])
  await turn()
  assert.equal(f.applied.length, 1)
  assert.deepEqual(f.applied[0][2], ['Mira'])
  cleanup()
})

test('an applied dictionary survives scene switches but refreshes in a new workspace epoch', async () => {
  const f = fixture()
  f.render()
  f.calls[0].resolve(['Mira'])
  await turn()
  f.setProject({ openSceneId: 'next-scene' })
  f.render()
  assert.equal(f.calls.length, 1)

  f.setProject({ workspaceEpoch: 2 })
  const cleanup = f.render()
  assert.equal(f.calls.length, 2)
  f.calls[1].resolve(['Vance'])
  await turn()
  assert.equal(f.applied.length, 2)
  cleanup()
})

test('autosave cancellation retries the current scene dictionary after a delay', async () => {
  const f = fixture()
  const cleanup = f.render()
  f.background.cancelBackgroundReads()
  await turn()
  assert.equal(f.calls.length, 1)

  f.retry()
  assert.equal(f.calls.length, 2)
  f.calls[1].resolve(['Mira'])
  await turn()
  assert.equal(f.applied.length, 1)
  cleanup()
})

test('cleanup removes a delayed dictionary retry', async () => {
  const f = fixture()
  const cleanup = f.render()
  f.background.cancelBackgroundReads()
  await turn()
  cleanup()
  f.retry()
  assert.equal(f.calls.length, 1)
  assert.equal(f.applied.length, 0)
})

test('a hidden mobile editor does not start a dictionary scan and returning starts a fresh request', async () => {
  const f = fixture()
  f.setShell({ mobileTab: 'codex' })
  f.render()()
  assert.equal(f.calls.length, 0)
  f.setShell({ mobileTab: 'manuscript' })
  const cleanup = f.render()
  f.calls[0].resolve(['Mira'])
  await turn()
  assert.equal(f.applied.length, 1)
  assert.deepEqual(f.applied[0][2], ['Mira'])
  cleanup()
})
