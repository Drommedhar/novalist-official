import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { runInNewContext } from 'node:vm'
import test from 'node:test'
import { transformWithEsbuild } from 'vite'
import { loadRendererSource } from './renderer-source-loader.mjs'

const { code: shellCode } = await transformWithEsbuild(
  readFileSync(new URL('../src/renderer/src/shell/AppShell.tsx', import.meta.url), 'utf8'),
  'AppShell.tsx', { loader: 'tsx', format: 'cjs', jsxFactory: 'jsx', jsxFragment: 'fragment' }
)
function shell(state) {
  const module = { exports: {} }
  const defaults = new Proxy({}, { get: (_target, name) => String(name).startsWith('use') ? () => {} : name })
  const dependencies = {
    react: { useRef: current => ({ current }), useLayoutEffect: () => {} },
    'react-i18next': { useTranslation: () => ({ t: key => key }) },
    '../stores/projectStore': { useProjectStore: select => select(state) },
    '../stores/shellStore': {
      useShellStore: select => select({ mainView: 'write', shellCapacity: 'wide', panes: [], binderWidth: 200 }),
      panelWidthForShell: () => 200, anyPaneShows: () => true
    },
    './modes': { chromeForView: () => ({}), modeOf: () => 'write' },
    './appUpdateState': { useAppUpdateState: () => ({ extUpdates: [] }) }
  }
  runInNewContext(shellCode, { module, exports: module.exports,
    window: { novalist: { isMobile: true } },
    jsx: (type, props, ...children) => ({ type, props: props ?? {}, children }), fragment: 'fragment',
    require: name => dependencies[name] ?? defaults
  })
  return module.exports.AppShell()
}
function nodes(tree) {
  if (!tree || typeof tree !== 'object') return []
  return [tree, ...(tree.children ?? []).flat(Infinity).flatMap(nodes)]
}
const mobileMounted = state => nodes(shell(state)).some(node => node.type === 'MobileShell')

test('mobile close keeps the current editing surface mounted and inert until suspension or success', () => {
  const state = { isLoaded: true, closingProject: true, workspaceSuspended: false }
  const rendered = nodes(shell(state))
  assert.ok(rendered.some(node => node.type === 'MobileShell'), 'shared bindings must remain attached until their writes acknowledge')
  assert.equal(rendered.find(node => node.props.className?.startsWith('shell shell-capacity')).props.inert, true)
  assert.ok(rendered.some(node => node.props.role === 'status'), 'closing progress remains visible')
  assert.equal(mobileMounted({ ...state, workspaceSuspended: true }), false)
  assert.equal(mobileMounted({ ...state, isLoaded: false }), false)
  assert.equal(mobileMounted({ ...state, closingProject: false }), true, 'failed close retains the working editor')
})

const { code: mobileCode } = await transformWithEsbuild(
  readFileSync(new URL('../src/renderer/src/shell/MobileShell.tsx', import.meta.url), 'utf8'),
  'MobileShell.tsx', { loader: 'tsx', format: 'cjs', jsxFactory: 'jsx' }
)

function mobileView(project, initialLayout = 'phone') {
  const window = Object.assign(new EventTarget(), { novalist: { platform: 'android' } })
  const document = { documentElement: { dataset: {} } }
  const slots = [], effects = []
  let cursor = 0
  const react = {
    useRef(current) { const index = cursor++; return slots[index] ??= { current } },
    useState(initial) {
      const index = cursor++; slots[index] ??= { value: initial }
      return [slots[index].value, value => { slots[index].value = typeof value === 'function' ? value(slots[index].value) : value }]
    },
    useEffect(effect, deps) {
      const index = cursor++, old = slots[index]
      if (!old || deps.some((value, i) => value !== old.deps[i])) {
        effects.push(() => { old?.cleanup?.(); slots[index] = { deps, cleanup: effect() } })
      }
    }
  }
  const shellState = {
    mobileLayout: initialLayout, mobileTab: 'manuscript', mainView: 'write',
    setMobileLayout(layout) { shellState.mobileLayout = layout },
    setMobileTab(tab) { shellState.mobileTab = tab },
    setMainView(view) { shellState.mainView = view },
    setFindReplaceOpen() {}, guardLeave: action => action()
  }
  const shellStore = Object.assign(select => select(shellState), { getState: () => shellState })
  const projectStore = Object.assign(select => select(project.getState()), project)
  const dependencies = {
    react,
    'react-i18next': { useTranslation: () => ({ t: key => key, i18n: { on() {}, off() {} } }) },
    '../stores/projectStore': { useProjectStore: projectStore },
    '../stores/shellStore': { useShellStore: shellStore },
    '../stores/hostBridgeStore': { useHostBridgeStore: { getState: () => ({ pushToast() {} }) } },
    '../stores/codexStore': { useCodexStore: { setState() {} } }
  }
  dependencies['./backNavigation'] = loadRendererSource('src/mobile/backNavigation.ts', dependencies)
  dependencies['../mobile/useAndroidBack'] = loadRendererSource('src/mobile/useAndroidBack.ts', dependencies, { window, Event })
  const module = { exports: {} }
  const defaults = new Proxy({}, { get: (_target, name) => name })
  runInNewContext(mobileCode, { module, exports: module.exports, window, document,
    jsx: (type, props, ...children) => ({ type, props: props ?? {}, children }),
    require: name => dependencies[name] ?? defaults
  })
  loadRendererSource('src/mobile/nativeLayout.ts', dependencies, { window, document }).installNativeLayout(initialLayout)
  return { window, document, state: shellState,
    render() { cursor = 0; const tree = module.exports.MobileShell(); for (const effect of effects.splice(0)) effect(); return tree },
    dispose() { for (const slot of slots) slot?.cleanup?.() }
  }
}

function sharedFixture({ fail = false, layout = 'phone' } = {}) {
  const pending = loadRendererSource('src/stores/pendingWrites.ts')
  const events = []
  let release, stopPersistence, attached = true
  const blocked = new Promise(resolve => { release = resolve })
  const identity = { sessionId: 'session', itemId: 'scene', state: '', presence: false }
  const state = {
    isLoaded: true, closingProject: false, workspaceSuspended: false, workspaceBusy: false, changingSceneStructure: false,
    activeEditorPaneId: 'pane', openSceneId: 'scene', dirtyMap: {}, recentProjects: [],
    editors: { pane: { sceneId: 'scene', shared: identity, isDirty: false } }
  }
  const project = { getState: () => state, setState: patch => {
    Object.assign(state, typeof patch === 'function' ? patch(state) : patch)
    reconcile()
  } }
  const mobile = mobileView(project, layout)
  let initialSurface
  const reconcile = () => {
    const tree = mobile.render()
    const surface = nodes(tree).find(node => node.type === 'EditorFrame' || node.type === 'TabletShell')?.type
    initialSurface ??= surface
    if (attached && (!mobileMounted(state) || surface !== initialSurface)) {
      attached = false; events.push('detach'); shared.detachSharedEditor('pane'); stopPersistence?.()
    }
  }
  const documents = loadRendererSource('src/collaboration/sharedDocuments.ts', {
    '../rpc/client': { rpc: { onNotification() {}, request: async () => '' } },
    '../stores/pendingWrites': pending
  }, { atob, btoa })
  const control = { fail, live: 'Fresh shared words', saved: '' }
  class Session {
    constructor(_surface, _state, _connection, changed) { this.changed = changed }
    capture() { this.changed({ html: control.live, plainText: control.live, pending: control.live !== control.saved, error: null }) }
    refresh() { return Promise.resolve() }
    receive() {}
    state() { return new Uint8Array() }
    detach() {}
    async flush() {
      events.push('flush'); await blocked
      if (control.fail) throw new Error('shared write unavailable')
      control.saved = control.live; this.capture(); events.push('ack')
    }
  }
  const shared = loadRendererSource('src/collaboration/sharedEditors.ts', {
    '../stores/projectStore': { useProjectStore: project, mirror: () => ({}) },
    './SharedEditorSession': { SharedEditorSession: Session }, './SharedEditorPresence': {},
    './richEditingSurface': { sceneEditorSurface: editor => editor }, './sharedDocuments': documents
  }, { crypto }, readFileSync(new URL('../src/renderer/src/collaboration/sharedEditors.ts', import.meta.url), 'utf8')
    .replace("export { subscribeSharedDocument } from './sharedDocuments'", ''))
  const { flushEditor } = loadRendererSource('src/stores/projectPersistence.ts', {
    '../rpc/client': {}, './projectStore': { useProjectStore: project }, './sceneWriteQueue': {},
    '../collaboration/sharedEditors': shared
  })
  state.flushPane = pane => flushEditor(state.editors[pane], pane)
  state.flushPendingSave = () => state.flushPane('pane')
  state.applyState = patch => project.setState(patch)
  state.closeTab = async () => { state.openSceneId = null; reconcile() }
  const { useEditorPersistence } = loadRendererSource('src/views/editor/useEditorSynchronization.ts', {
    react: { useEffect: effect => { stopPersistence = effect() } },
    '../../stores/editorBridgeStore': {}, '../../stores/shellStore': {},
    '../../stores/projectStore': { useProjectStore: project, editorPane: (state, id) => state.editors[id] },
    '../../stores/pendingWrites': pending, '../../collaboration/sharedEditors': shared
  })
  shared.syncSharedEditor('pane', {})
  useEditorPersistence({ pane: 'pane', openSceneId: 'scene', editorRef: { current: {} }, lastReportedHtmlRef: { current: '' } })
  Object.assign(state, loadRendererSource('src/stores/projectLibraryActions.ts', {
    './pendingWrites': pending, './projectStore': { useProjectStore: project },
    './projectSceneActions': { runSceneContext: action => action() },
    './settingsStore': { useSettingsStore: { getState: () => ({ load: async () => {} }) } },
    '../rpc/client': { rpc: { request: async method => {
      if (method === 'project/recent') return []
      assert.equal(method, 'project/close')
      assert.equal(state.editors.pane.isDirty, false, 'the current binding must receive its acknowledgement')
      events.push('close'); return { isLoaded: false }
    } } }
  }).createProjectLibraryActions(project.setState, project.getState))
  reconcile()
  return { state, mobile, events, control, shared, release, reconcile,
    async dispose() { release(); control.fail = false; stopPersistence?.(); mobile.dispose(); shared.detachSharedEditor('pane'); await pending.flushPendingWrites() }
  }
}

for (const layout of ['phone', 'tablet']) {
  test('Library keeps the real shared ' + layout + ' binding through blocked saves, native navigation and rotation', async () => {
    const f = sharedFixture({ layout })
    const closing = f.state.closeProject()
    const settled = closing.catch(error => error)
    try {
      await new Promise(setImmediate)
      f.mobile.window.__novalistTab('settings')
      assert.equal(f.mobile.state.mobileTab, 'manuscript')
      assert.equal(f.mobile.state.mainView, 'write')
      f.mobile.window.__novalistPlanSelect(1)
      assert.equal(f.mobile.state.mobileTab, 'manuscript')
      f.mobile.window.dispatchEvent(new Event('novalist-android-back', { cancelable: true }))
      f.mobile.window.__novalistLayout(layout === 'phone' ? 'tablet' : 'phone')
      f.reconcile()
      assert.equal(f.state.isLoaded, true)
      assert.equal(f.state.closingProject, true)
      assert.equal(f.events.includes('detach'), false, 'busy navigation and rotation must not detach the saving binding')
      assert.equal(f.mobile.document.documentElement.dataset.mobileLayout, layout)
      assert.equal(f.mobile.state.mobileTab, 'manuscript')
      f.release(); await closing
      assert.equal(f.control.saved, 'Fresh shared words')
      assert.equal(f.state.isLoaded, false)
      assert.ok(f.events.indexOf('close') < f.events.indexOf('detach'))
    } finally { f.release(); await settled; await f.dispose() }
  })
}

test('failed shared close retains its binding and accepts new edits before retry', async () => {
  const f = sharedFixture({ fail: true })
  try {
    f.release()
    await assert.rejects(f.state.closeProject(), /shared write unavailable/)
    assert.equal(f.state.isLoaded, true)
    assert.equal(f.state.closingProject, false)
    assert.equal(f.events.includes('detach'), false)
    assert.equal(nodes(shell(f.state)).find(node => node.props.className?.startsWith('shell shell-capacity')).props.inert, false)
    f.control.fail = false; f.control.live = 'Edited after failed close'
    assert.equal(f.shared.captureSharedEditor('pane'), true)
    await f.state.closeProject()
    assert.equal(f.control.saved, 'Edited after failed close')
    assert.equal(f.state.isLoaded, false)
  } finally { await f.dispose() }
})

test('Android Back consumes the event without dispatching detail navigation while closing', () => {
  const state = { closingProject: true, isLoaded: true }
  const mobile = mobileView({ getState: () => state })
  let details = 0
  mobile.window.addEventListener('novalist-mobile-detail-back', event => { details++; event.preventDefault() })
  try {
    mobile.render()
    assert.equal(mobile.window.dispatchEvent(new Event('novalist-android-back', { cancelable: true })), false)
    assert.equal(details, 0)
    state.closingProject = false
    mobile.window.dispatchEvent(new Event('novalist-android-back', { cancelable: true }))
    assert.equal(details, 1)
  } finally { mobile.dispose() }
})

test('failed close applies the latest native layout and resumes navigation', () => {
  const state = { closingProject: false, openSceneId: 'scene', isLoaded: true }
  const mobile = mobileView({ getState: () => state })
  try {
    mobile.render(); state.closingProject = true; mobile.render()
    mobile.window.__novalistLayout('tablet')
    assert.ok(nodes(mobile.render()).some(node => node.type === 'EditorFrame'))
    state.closingProject = false
    assert.equal(mobile.render().type, 'TabletShell')
    assert.equal(mobile.document.documentElement.dataset.mobileLayout, 'tablet')
    mobile.window.__novalistTab('settings')
    assert.equal(mobile.state.mainView, 'settings')
  } finally { mobile.dispose() }
})
