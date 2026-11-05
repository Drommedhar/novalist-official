import { test } from 'node:test'
import assert from 'node:assert/strict'
import { loadRendererSource } from './renderer-source-loader.mjs'

const turn = () => new Promise(resolve => setImmediate(resolve))
function store(initial) {
  let state = initial
  const listeners = new Set()
  return { getState: () => state, setState: patch => {
    const previous = state
    state = { ...state, ...(typeof patch === 'function' ? patch(state) : patch) }
    for (const listener of listeners) listener(state, previous)
  }, subscribe: listener => { listeners.add(listener); return () => listeners.delete(listener) } }
}
function harness() {
  const panes = loadRendererSource('src/stores/shellPanes.ts', {}, { localStorage: { getItem: () => null } })
  const first = panes.newLeaf('write')
  const shell = store({ panes: first, activePaneId: first.id, notesDockVisible: false })
  let native = { docked: false, visible: true, pinned: true, follow: true, context: null }
  let publish = () => {}
  let resolve
  let requests = 0
  const transfer = new Promise(done => { resolve = done })
  const notes = loadRendererSource('src/shell/sceneNotesDocking.ts', {
    react: { useEffect: effect => effect() }, 'react-dom': { flushSync: fn => fn() }, zustand: { create: fn => store(fn()) },
    '../stores/shellStore': { useShellStore: shell, paneLeaves: panes.paneLeaves },
    '../stores/shellPanes': panes,
    './sceneNotesPlacement': { readNotesPlacement: () => null, saveNotesPlacement: () => {} },
    '../stores/pendingWrites': { flushPendingWrites: async () => {} },
    '../stores/hostBridgeStore': { useHostBridgeStore: { getState: () => ({ pushToast: () => {} }) } },
    '../i18n': { default: { t: key => key } }, './useWorkspaceWindow': { flushWindowWrites: async () => {} }
  }, { window: { novalist: {
    onSceneNotesState: handler => { publish = handler; return () => {} },
    sceneNotesWindowState: async () => native,
    dockSceneNotes: async () => { requests++; await transfer; native = { ...native, docked: true, visible: true }; publish(native) },
    hideSceneNotes: async () => { native = { ...native, visible: false }; publish(native) }
  } } })
  notes.useSceneNotesDocking()
  return { panes, shell, notes, resolve, get requests() { return requests }, get native() { return native } }
}

test('docking survives a layout replacing the target while the floating editor saves', async () => {
  const h = harness()
  await turn()
  const pending = h.notes.dockSceneNotesAt(h.shell.getState().activePaneId, 'top')
  await turn()
  const replacement = h.panes.newLeaf('write')
  h.shell.setState({ panes: replacement, activePaneId: replacement.id })
  h.resolve()
  await pending
  const leaves = h.panes.paneLeaves(h.shell.getState().panes)
  assert.equal(leaves.filter(p => p.view === 'sceneNotes').length, 1)
  assert.ok(leaves.some(p => p.id === replacement.id))
  assert.equal(h.native.docked, true)
})

test('restoring then resetting a layout during transfer leaves notes hidden and the editor reachable', async () => {
  const h = harness()
  await turn()
  const editor = h.shell.getState().panes
  h.shell.setState({ panes: h.panes.dockNotesPane(editor, editor.id, 'right') })
  await turn()
  assert.equal(h.notes.useNotesDocking.getState().transferring, true)
  h.shell.setState({ panes: h.panes.newLeaf('write') })
  h.resolve()
  await turn()
  assert.equal(h.native.visible, false)
  assert.equal(h.shell.getState().panes.view, 'write')
  assert.equal(h.notes.useNotesDocking.getState().transferring, false)
})

test('repeated saved layout changes share a single native handoff', async () => {
  const h = harness()
  await turn()
  const editor = h.shell.getState().panes
  const layout = h.panes.dockNotesPane(editor, editor.id, 'right')
  h.shell.setState({ panes: layout })
  await turn()
  h.shell.setState({ panes: h.panes.reidentify(layout) })
  await turn()
  assert.equal(h.requests, 1)
  h.resolve()
  await turn()
  assert.equal(h.native.visible, true)
})
