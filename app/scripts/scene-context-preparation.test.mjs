import assert from 'node:assert/strict'
import test from 'node:test'
import { loadRendererSource } from './renderer-source-loader.mjs'

const turn = () => new Promise(resolve => setImmediate(resolve))
const initial = () => ({ isLoaded: true, closingProject: false, workspaceSuspended: false,
  projectPath: '/project', activeBookId: 'book', activeDraftId: 'draft', workspaceEpoch: 1,
  openChapterGuid: 'chapter', openSceneId: 'scene', sceneHashes: { scene: 'saved-1' },
  activeEditorPaneId: 'editor', editors: { editor: { chapterGuid: 'chapter', sceneId: 'scene', html: '<p>Saved</p>' } } })

function fixture({ ignoreAbort = false } = {}) {
  const calls = [], timers = [], effects = [], completed = []
  let state = initial()
  let manuscript = { sections: [] }
  let previousEffects = []
  let shell = { mainView: 'write', mobileLayout: 'phone', mobileTab: 'manuscript' }
  const background = loadRendererSource('src/rpc/backgroundReads.ts', {}, { window: { novalist: { isMobile: true } } })
  const data = loadRendererSource('src/shell/sceneContextData.ts', {
    '../rpc/client': { rpc: { onRequestCompleted: listener => completed.push(listener), request(method, params, options) {
      return new Promise((resolve, reject) => {
        calls.push({ method, params, signal: options.signal, resolve, reject })
        if (!ignoreAbort) options.signal.addEventListener('abort', () => reject(options.signal.reason), { once: true })
      })
    } } }, '../rpc/backgroundReads': background
  }, { AbortController })
  const hooks = loadRendererSource('src/shell/usePreparedSceneContext.ts', {
    './sceneContextData': data,
    react: { useEffect: (effect, deps) => effects.push({ effect, deps }), useSyncExternalStore: (_subscribe, read) => read() },
    '../stores/shellStore': { useShellStore: Object.assign(select => select(shell), { getState: () => shell }) },
    '../stores/manuscriptStore': { useManuscriptStore: Object.assign(select => select(manuscript), { getState: () => manuscript }) },
    '../stores/projectStore': { useProjectStore: Object.assign(select => select(state), { getState: () => state }) }
  }, { setTimeout: (fn, delay) => {
    const timer = { delay, cancelled: false, fn() { if (!timer.cancelled) fn() } }
    timers.push(timer)
    return timer
  }, clearTimeout(timer) { timer.cancelled = true }, window: { novalist: { isMobile: true } } })
  return { data, hooks, calls, timers, effects, background, state: () => state,
    complete: method => completed.forEach(listener => listener(method)),
    setState: patch => { state = { ...state, ...patch } },
    setShell: patch => { shell = { ...shell, ...patch } },
    edit: html => { state = { ...state, editors: { ...state.editors, editor: { ...state.editors.editor, html } } } },
    setManuscript: html => { manuscript = { sections: [{ chapterGuid: 'chapter', scenes: [{ sceneId: 'scene', html }] }] } },
    render(chapterGuid, sceneId, immediate = false) {
      effects.length = 0
      const result = hooks.useSceneContext(chapterGuid, sceneId, immediate)
      previousEffects = effects.map((entry, index) => {
        const previous = previousEffects[index]
        if (previous && entry.deps.every((value, i) => Object.is(value, previous.deps[i]))) return previous
        previous?.cleanup?.()
        return { ...entry, cleanup: entry.effect() }
      })
      return result
    } }

}

test('the hidden inspector prepares current context and shares one complete result with its consumer', async () => {
  const f = fixture()
  f.render()
  assert.equal(f.calls.length, 0, 'editor mounting must not wait for analysis')
  for (const timer of f.timers.splice(0)) timer.fn()
  assert.equal(f.calls.length, 1, 'preparation runs without mounting the inspector')
  const scope = f.data.sceneContextScope(f.state())
  const waiting = f.data.prepareSceneContext(scope)
  assert.equal(f.calls.length, 1, 'opening the panel shares the in-flight analysis')
  const published = []
  f.data.subscribeSceneContext(() => { const value = f.data.readSceneContext().context; if (value) published.push(value) })
  const complete = { characters: [{ id: 'character' }], locations: [{ id: 'location' }], analysis: { wordCount: 17 } }
  f.calls[0].resolve(complete)
  await waiting
  assert.equal(published.length, 1)
  assert.equal(published[0], complete)
  await f.data.prepareSceneContext(scope)
  assert.equal(f.calls.length, 1, 'a completed result is available immediately on reopening')
})

for (const patch of [
  { projectPath: '/another' }, { activeBookId: 'other' }, { activeDraftId: 'other' },
  { workspaceEpoch: 2 }, { openSceneId: 'other' }, { sceneHashes: { scene: 'saved-2' } }
]) test(`scope changes invalidate pending context: ${JSON.stringify(patch)}`, async () => {
  const f = fixture()
  const old = f.data.prepareSceneContext(f.data.sceneContextScope(f.state()))
  f.setState(patch)
  const scope = f.data.sceneContextScope(f.state())
  const current = f.data.prepareSceneContext(scope)
  assert.equal(f.calls[0].signal.aborted, true)
  f.calls[0].resolve({ stale: true })
  f.calls[1].resolve({ current: true })
  await Promise.all([old, current])
  assert.equal(f.data.readSceneContext().key, scope.key)
  assert.equal(f.data.readSceneContext().context.current, true)
})

test('foreground work cancels preparation without immediate retry', async () => {
  const f = fixture()
  const scope = f.data.sceneContextScope(f.state())
  const waiting = f.data.prepareSceneContext(scope)
  f.background.cancelBackgroundReads()
  await waiting
  assert.equal(f.calls[0].signal.aborted, true)
  assert.equal(f.calls.length, 1)
  f.render()
  assert.ok(f.timers.some(timer => timer.delay >= 900), 'cancelled work yields before retrying')
  f.setState({ isLoaded: false })
  f.render()
  assert.equal(f.data.readSceneContext().context, null)
  assert.equal(f.data.readSceneContext().key, null)
})

test('a failed request is visible as one failure and a deliberate refresh retries it', async () => {
  const f = fixture()
  const scope = f.data.sceneContextScope(f.state())
  const waiting = f.data.prepareSceneContext(scope)
  f.calls[0].reject(new Error('Unavailable'))
  await waiting
  assert.equal(f.data.readSceneContext().status, 'error')
  const retry = f.data.prepareSceneContext(scope, true)
  f.calls[1].resolve({ analysis: { wordCount: 2 } })
  await retry
  assert.equal(f.data.readSceneContext().status, 'ready')
  await turn()
})

for (const mobileLayout of ['phone', 'tablet']) test(`leaving the editor cancels and suspends hidden context in the ${mobileLayout} shell`, async () => {
  const f = fixture()
  f.setShell({ mobileLayout })
  const waiting = f.data.prepareSceneContext(f.data.sceneContextScope(f.state()))
  f.setShell({ mainView: 'codex', mobileTab: 'codex' })
  f.render()
  await turn()
  assert.equal(f.calls[0].signal.aborted, true)
  assert.equal(f.data.readSceneContext().key, f.data.sceneContextScope(f.state()).key)
  assert.equal(f.timers.length, 0, 'Codex navigation must not restart background context')
  await waiting
  f.setShell({ mainView: 'write', mobileTab: 'manuscript' })
  f.render()
  for (const timer of f.timers.splice(0)) timer.fn()
  assert.equal(f.calls.length, 2, 'returning to writing prepares fresh context')
  f.calls[1].resolve({ analysis: { wordCount: 2 } })
  await turn()
})

test('completed context survives other views but still invalidates when its scene revision changes there', async () => {
  const f = fixture()
  const ready = f.data.prepareSceneContext(f.data.sceneContextScope(f.state()))
  const context = { analysis: { wordCount: 12 } }
  f.calls[0].resolve(context)
  await ready
  f.setShell({ mainView: 'codex', mobileTab: 'codex' })
  f.render()
  assert.equal(f.data.readSceneContext().context, context)
  assert.equal(f.timers.length, 0)
  f.setShell({ mainView: 'write', mobileTab: 'manuscript' })
  f.render()
  assert.equal(f.data.readSceneContext().context, context)
  assert.equal(f.calls.length, 1)
  assert.equal(f.timers.length, 0, 'returning must reuse the completed analysis')
  f.setShell({ mainView: 'codex', mobileTab: 'codex' })
  f.setState({ sceneHashes: { scene: 'saved-2' } })
  f.render()
  assert.equal(f.data.readSceneContext().context, null)
  assert.equal(f.timers.length, 0, 'changed context stays suspended outside writing')
})

test('successful entity and scene metadata writes invalidate cached context while reads retain it', async () => {
  const f = fixture()
  const scope = f.data.sceneContextScope(f.state())
  const ready = f.data.prepareSceneContext(scope)
  const context = { analysis: { wordCount: 12 } }
  f.calls[0].resolve(context)
  await ready
  for (const method of ['context/analyze', 'entities/list', 'entities/get', 'entities/getStateOverrides', 'scenes/read', 'scenes/setEditing', 'scenes/setEditingMany', 'premise/get', 'research/list', 'research/history', 'research/fetchLinkTitle']) {
    f.complete(method)
    assert.equal(f.data.readSceneContext().context, context, method)
  }
  for (const method of ['entities/update', 'entities/setOverride', 'entities/setStateOverrides', 'entities/create', 'entities/delete', 'entities/moveToBook', 'scenes/setPov', 'project/setChapterAct', 'project/renameScene']) {
    const waiting = f.data.prepareSceneContext(scope)
    f.calls.at(-1).resolve(context)
    await waiting
    f.complete(method)
    assert.equal(f.data.readSceneContext().context, null, method)
    assert.equal(f.data.readSceneContext().key, scope.key, method)
  }
  const pending = f.data.prepareSceneContext(scope)
  f.data.invalidateSceneContext()
  assert.equal(f.calls.at(-1).signal.aborted, true)
  await pending
  assert.equal(f.data.readSceneContext().status, 'idle')
})

for (const method of ['premise/saveVoice', 'premise/scaffold', 'research/save', 'research/delete',
  'research/import', 'research/quickCapture', 'research/restoreRevision', 'research/link', 'research/setLifecycle']) {
  test(`${method} invalidates completed voice analysis or research suggestions`, async () => {
    const f = fixture()
    const scope = f.data.sceneContextScope(f.state())
    const ready = f.data.prepareSceneContext(scope)
    f.calls[0].resolve({ analysis: { narrativePerson: 'first' }, research: [{ id: 'research' }] })
    await ready
    assert.equal(f.data.readSceneContext().status, 'ready')
    f.complete(method)
    assert.equal(f.data.readSceneContext().context, null)
    assert.equal(f.data.readSceneContext().status, 'idle')
    assert.equal(f.data.readSceneContext().key, scope.key)
  })
}

for (const html of ['<p>Unsaved revision</p>', '']) {
  test(`live editor HTML invalidates cached analysis and reaches the backend: ${JSON.stringify(html)}`, async () => {
    const f = fixture()
    const scope = f.data.sceneContextScope(f.state())
    const ready = f.data.prepareSceneContext(scope)
    f.calls[0].resolve({ analysis: { wordCount: 1 } })
    await ready
    f.edit(html)
    const updated = f.data.sceneContextScope(f.state())
    assert.notEqual(updated.key, scope.key)
    const refresh = f.data.prepareSceneContext(updated)
    assert.equal(f.calls.length, 2)
    assert.deepEqual(Array.from(f.calls[1].params), ['chapter', 'scene', html])
    f.calls[1].resolve({ analysis: { wordCount: html === '' ? 0 : 2 } })
    await refresh
    assert.equal(f.data.readSceneContext().key, updated.key)
  })
}

test('typing debounces shared preparation and keeps displayed override fields mounted', async () => {
  const f = fixture()
  const ready = f.data.prepareSceneContext(f.data.sceneContextScope(f.state()))
  const context = { analysis: { wordCount: 1 } }
  f.calls[0].resolve(context)
  await ready
  f.render()
  f.edit('<p>First edit</p>')
  assert.equal(f.render().context, context, 'the render preceding the effect must retain the current fields')
  assert.equal(f.data.readSceneContext().context, context)
  assert.ok(f.timers.at(-1).delay > 250, 'live edits wait for a short typing pause')
  const earlier = f.timers.at(-1)
  f.edit('<p>Latest edit</p>')
  f.render()
  assert.equal(earlier.cancelled, true)
  earlier.fn()
  assert.equal(f.calls.length, 1)
  f.timers.at(-1).fn()
  assert.equal(f.calls.length, 2)
  assert.equal(f.calls[1].params[2], '<p>Latest edit</p>')
  assert.equal(f.data.readSceneContext().context, context)
  f.calls[1].resolve({ analysis: { wordCount: 2 } })
  await turn()
})

for (const html of ['<p>Composed manuscript edit</p>', '']) {
  test(`manuscript edits share the correct live-content scope: ${JSON.stringify(html)}`, async () => {
    const f = fixture()
    f.setShell({ mobileLayout: 'tablet', mainView: 'manuscript' })
    f.setManuscript('<p>Initial manuscript</p>')
    f.render()
    f.timers.at(-1).fn()
    const scope = f.hooks.currentSceneContextScope('chapter', 'scene')
    assert.equal(f.calls[0].params[2], '<p>Initial manuscript</p>')
    f.calls[0].resolve({ analysis: { wordCount: 2 } })
    await turn()
    f.setManuscript(html)
    f.render('chapter', 'scene', true)
    assert.notEqual(f.hooks.currentSceneContextScope().key, scope.key)
    f.timers.at(-1).fn()
    assert.equal(f.calls[1].params[2], html)
    const shared = f.data.prepareSceneContext(f.hooks.currentSceneContextScope())
    assert.equal(f.calls.length, 2, 'inspector and panel reuse the same manuscript request')
    f.calls[1].resolve({ analysis: { wordCount: html === '' ? 0 : 3 } })
    await shared
  })
}

test('the phone Manuscript tab uses its active EditorFrame even if the main-view field says manuscript', () => {
  const f = fixture()
  f.setShell({ mainView: 'manuscript' })
  f.setManuscript('<p>Inactive composed view</p>')
  f.edit('<p>Phone editor</p>')
  assert.equal(f.hooks.currentSceneContextScope().currentSceneHtml, '<p>Phone editor</p>')
})

test('a different active editor never supplies HTML for the selected manuscript scene', () => {
  const f = fixture()
  f.setState({ editors: { editor: { chapterGuid: 'other', sceneId: 'other', html: '<p>Other scene</p>' } } })
  assert.equal(f.data.sceneContextScope(f.state()).currentSceneHtml, null)
  assert.equal(f.data.sceneContextScope(f.state(), 'other', 'other'), null)
})

test('unchanged renders and revealing the panel retain the prepared analysis', async () => {
  const f = fixture()
  f.render()
  f.timers.at(-1).fn()
  f.calls[0].resolve({ analysis: { wordCount: 1 } })
  await turn()
  const ready = f.data.readSceneContext()
  let publications = 0
  f.data.subscribeSceneContext(() => publications++)
  for (let i = 0; i < 5; i++) assert.equal(f.render('chapter', 'scene', i % 2 === 0), ready)
  assert.equal(f.calls.length, 1)
  assert.equal(publications, 0, 'selection must not publish repeatedly or cause a render loop')
})

test('live edits cancel obsolete analysis and discard a response that ignores abort', async () => {
  const f = fixture({ ignoreAbort: true })
  const stale = f.data.prepareSceneContext(f.data.sceneContextScope(f.state()))
  f.edit('<p>New draft</p>')
  const current = f.data.prepareSceneContext(f.data.sceneContextScope(f.state()))
  assert.equal(f.calls[0].signal.aborted, true)
  f.calls[1].resolve({ current: true })
  await current
  f.calls[0].resolve({ stale: true })
  await stale
  assert.equal(f.data.readSceneContext().context.current, true)
})

test('workspace transitions cancel preparation and obsolete panel props cannot clear the next selection', async () => {
  const f = fixture()
  const pending = f.data.prepareSceneContext(f.data.sceneContextScope(f.state()))
  f.setState({ workspaceBusy: true })
  f.render()
  await pending
  assert.equal(f.calls[0].signal.aborted, true)
  assert.equal(f.data.readSceneContext().key, null)
  f.setState({ workspaceBusy: false, openSceneId: 'other' })
  const scope = f.data.sceneContextScope(f.state())
  f.data.selectSceneContext(scope)
  f.render('chapter', 'scene')
  assert.equal(f.data.readSceneContext().key, scope.key)
  assert.equal(f.calls.length, 1)
})

test('a delayed request rechecks its selection before issuing a backend read', () => {
  const f = fixture()
  f.render()
  f.setState({ workspaceEpoch: 2 })
  f.timers.at(-1).fn()
  assert.equal(f.calls.length, 0)
})
