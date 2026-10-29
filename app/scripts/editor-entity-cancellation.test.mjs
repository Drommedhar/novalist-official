import assert from 'node:assert/strict'
import test from 'node:test'
import { loadRendererSource } from './renderer-source-loader.mjs'

for (const reason of ['unmount', 'foreground read']) test(`${reason} cancels a mobile editor's queued entity scans`, async () => {
  const calls = []
  let cleanup
  const rpc = { request(method, params, options) {
    calls.push({ method, params, options })
    return new Promise((_, reject) => options?.signal?.addEventListener('abort', () => reject(options.signal.reason), { once: true }))
  } }
  const project = { useProjectStore: Object.assign(selector => selector({ chapters: [] }), { getState: () => ({}) }),
    editorPane: () => ({ sceneId: 'scene' }) }
  const names = loadRendererSource('src/views/editor/editorEntities.ts', {
    '../../rpc/client': { rpc }, '../../stores/projectStore': project
  })
  const window = { novalist: { isMobile: true } }
  const background = loadRendererSource('src/rpc/backgroundReads.ts', {}, { window })
  const peek = loadRendererSource('src/views/editor/useEditorPeek.ts', {
    './editorEntities': names,
    react: { useRef: value => ({ current: value }), useEffect: effect => { cleanup = effect() } },
    '../../stores/projectStore': project, '../../stores/shellStore': {}, '../../stores/wikiStore': {},
    '../../rpc/backgroundReads': background,
    './PeekCard': { useEntityPeek: () => ({}) }
  }, { window, AbortController })
  const loading = peek.useEditorPeek('pane', 'scene').pushEntityNames({})
  assert.equal(calls.length, 1)
  assert.ok(calls[0].options?.signal, 'mobile entity scans must be cancellable')
  if (reason === 'unmount') cleanup()
  else background.cancelBackgroundReads()
  await loading
  assert.equal(calls[0].options.signal.aborted, true)
  assert.equal(calls.length, 1, 'unmounted editors must not queue more type scans')
})
