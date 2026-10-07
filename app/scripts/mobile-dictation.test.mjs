import assert from 'node:assert/strict'
import test from 'node:test'
import { webcrypto } from 'node:crypto'
import { readFileSync } from 'node:fs'
import { transformWithEsbuild } from 'vite'
import { create } from 'zustand'
import { HostCallChannel } from '../src/renderer/src/mobile/hostCalls.ts'
import { loadRendererSource } from './renderer-source-loader.mjs'

const turn = () => new Promise(resolve => setImmediate(resolve))
const store = state => ({ getState: () => state, subscribe: () => () => {} })
const audioSource = (await transformWithEsbuild(
  readFileSync(new URL('../src/renderer/src/dictation/audio.ts', import.meta.url), 'utf8'),
  'audio.ts', { loader: 'ts', target: 'es2022' }
)).code

function fixture(t) {
  const requests = []
  const listeners = new Map()
  const window = {
    HybridWebView: { SendRawMessage: message => requests.push(JSON.parse(message)) },
    addEventListener: (name, callback) => listeners.set(name, callback)
  }
  const globals = { window, document: { visibilityState: 'visible' }, navigator: {},
    TextDecoder, Uint8Array, atob, btoa, DOMException, AbortController, crypto: webcrypto,
    setInterval: () => 1, clearInterval() {} }
  loadRendererSource('src/mobile/shim.ts', {
    i18next: { default: { t: key => key } },
    './projectImages': { installProjectImageLoader() {}, clearProjectImageCache() {} },
    './hostCalls': { HostCallChannel }
  }, globals)
  const audio = loadRendererSource('src/dictation/audio.ts', {}, globals, audioSource)
  const options = loadRendererSource('src/dictation/dictationOptions.ts', {}, {
    localStorage: { getItem: () => null, setItem() {} }
  })
  const format = loadRendererSource('src/dictation/formatDictation.ts')
  let anchors = 0
  const provider = { id: 'fixture', available: true, automaticDialogue: false }
  const dictation = loadRendererSource('src/dictation/dictationStore.ts', {
    zustand: { create },
    '../rpc/client': { rpc: { request: async method => {
      assert.equal(method, 'dictation/providers')
      return [provider]
    } } },
    '../stores/editorBridgeStore': { useEditorBridge: store({ sceneId: 'scene', editor: {
      captureDictationAnchor: () => { anchors++; return true }, flushPendingContentChange() {}
    } }) },
    '../stores/projectStore': { useProjectStore: store({ projectPath: '/fixture', activeBookId: 'book', drafts: [{ id: 'draft', isActive: true }] }) },
    '../stores/settingsStore': { useSettingsStore: store({ view: { effective: {}, global: {} } }) },
    '../stores/shellStore': { useShellStore: store({ mainView: 'write' }) },
    '../stores/pendingWrites': { registerPendingWrite: () => () => {} },
    './audio': audio, './dictationOptions': options, './formatDictation': format
  }, globals)
  const reply = (request, fields) => {
    assert.ok(request, 'native request was sent')
    assert.equal(window.__novalistHostResult(Buffer.from(JSON.stringify({ id: request.id, ...fields })).toString('base64')), 'accepted')
  }
  t.after(async () => {
    listeners.get('pagehide')()
    await dictation.discardDictation()
  })
  return { requests, reply, dictation, anchors: () => anchors }
}

test('native microphone denial reaches dictation permission state through the shim and can retry', async t => {
  const f = fixture(t)
  const { showDictation, startDictation, stopDictation, useDictation } = f.dictation
  await showDictation()
  const denied = startDictation()
  assert.equal(f.requests[0].method, 'microphoneStart')
  f.reply(f.requests[0], { ok: false, error: 'Microphone access was denied.', errorCode: 'permission-denied' })
  await denied
  assert.equal(useDictation.getState().error, 'dictation.permission')
  assert.equal(useDictation.getState().recording, false)
  assert.equal(useDictation.getState().starting, false)
  assert.deepEqual(f.requests.map(request => request.method), ['microphoneStart'])

  const retry = startDictation()
  assert.equal(f.requests[1].method, 'microphoneStart')
  f.reply(f.requests[1], { ok: true, result: null })
  await retry
  assert.equal(useDictation.getState().error, null)
  assert.equal(useDictation.getState().recording, true)
  assert.equal(useDictation.getState().starting, false)
  assert.equal(f.anchors(), 2)
  assert.equal(f.requests[2].method, 'microphoneRead')
  f.reply(f.requests[2], { ok: true, result: { clips: [], ended: false } })
  await turn()
  const stopping = stopDictation()
  await turn()
  assert.equal(f.requests[3].method, 'microphoneStop')
  f.reply(f.requests[3], { ok: true, result: { clips: [], ended: true } })
  await stopping
  assert.equal(useDictation.getState().recording, false)
  assert.equal(useDictation.getState().error, null)
  assert.deepEqual(f.requests.map(request => request.method), ['microphoneStart', 'microphoneStart', 'microphoneRead', 'microphoneStop'])
})
