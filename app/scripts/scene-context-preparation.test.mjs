import assert from 'node:assert/strict'
import test from 'node:test'
import { loadRendererSource } from './renderer-source-loader.mjs'

const turn = () => new Promise(resolve => setImmediate(resolve))
const initial = () => ({ isLoaded: true, closingProject: false, workspaceSuspended: false,
  projectPath: '/project', activeBookId: 'book', activeDraftId: 'draft', workspaceEpoch: 1,
  openChapterGuid: 'chapter', openSceneId: 'scene', sceneHashes: { scene: 'saved-1' } })

function fixture() {
  const calls = [], timers = [], effects = [], completed = []
  let state = initial()
  let shell = { mainView: 'write', mobileLayout: 'phone', mobileTab: 'manuscript' }
  const background = loadRendererSource('src/rpc/backgroundReads.ts', {}, { window: { novalist: { isMobile: true } } })
  const data = loadRendererSource('src/shell/sceneContextData.ts', {
    '../rpc/client': { rpc: { onRequestCompleted: listener => completed.push(listener), request(method, params, options) {
      return new Promise((resolve, reject) => {
        calls.push({ method, params, signal: options.signal, resolve, reject })
        options.signal.addEventListener('abort', () => reject(options.signal.reason), { once: true })
      })
    } } }, '../rpc/backgroundReads': background
  }, { AbortController })
  const hooks = loadRendererSource('src/shell/usePreparedSceneContext.ts', {
    './sceneContextData': data,
    react: { useEffect: effect => effects.push(effect), useSyncExternalStore: (_subscribe, read) => read() },
    '../stores/shellStore': { useShellStore: select => select(shell) },
    '../stores/projectStore': { useProjectStore: Object.assign(select => select(state), { getState: () => state }) }
  }, { setTimeout: (fn, delay) => (timers.push({ fn, delay }), timers.length), clearTimeout() {}, window: { novalist: { isMobile: true } } })
  return { data, hooks, calls, timers, effects, background, state: () => state,
    complete: method => completed.forEach(listener => listener(method)),
    setState: patch => { state = { ...state, ...patch } },
    setShell: patch => { shell = { ...shell, ...patch } },
    render() { effects.length = 0; hooks.usePrepareSceneContext(); for (const effect of effects) effect() } }
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

test('unsaved keystrokes do not invalidate context and foreground work cancels preparation without immediate retry', async () => {
  const f = fixture()
  const scope = f.data.sceneContextScope(f.state())
  f.setState({ openSceneHtml: 'typing', openScenePlainText: 'typing', isDirty: true })
  assert.equal(f.data.sceneContextScope(f.state()).key, scope.key)
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

test('successful entity and scene metadata writes invalidate cached context while reads and keystrokes retain it', async () => {
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
