import { test } from 'node:test'
import assert from 'node:assert/strict'
import { EventEmitter } from 'node:events'
import { loadRendererSource } from './renderer-source-loader.mjs'
import { clampNotesBounds, selectNotesContext } from '../src/shared/sceneNotesProtocol.ts'

function harness(material = 'opaque', preferences = { pinned: true, bounds: { x: 9000, y: 0, width: 420, height: 560 } }) {
  const ipcMain = new EventEmitter()
  const handlers = new Map()
  ipcMain.handle = (name, handler) => handlers.set(name, handler)
  const windows = []
  const writes = []
  const glassWindows = []
  const messages = []
  const screen = Object.assign(new EventEmitter(), {
    getDisplayMatching: () => ({ workArea: { x: 0, y: 0, width: 1000, height: 700 } })
  })
  function makeWindow(_geometry, options = {}) {
    const live = () => { if (win.destroyed) throw new Error('Object has been destroyed') }
    const win = Object.assign(new EventEmitter(), {
      destroyed: false,
      visible: false, focused: false, alwaysOnTop: false, allWorkspaces: false, parent: options.parent, options,
      bounds: { x: 100, y: 100, width: 420, height: 560 },
      isDestroyed: () => win.destroyed,
      isFocused: () => win.focused,
      isVisible: () => { live(); return win.visible },
      getBounds: () => win.bounds,
      getNormalBounds: () => win.bounds,
      setBounds: (bounds) => { win.bounds = bounds },
      show: () => { live(); win.visible = true; win.emit('show') },
      hide: () => { live(); win.visible = false; win.emit('hide') },
      focus: () => { for (const other of windows) other.focused = false; win.focused = true },
      getParentWindow: () => win.parent,
      setParentWindow: (value) => { win.parent = value },
      setAlwaysOnTop: (value) => { win.alwaysOnTop = value },
      setVisibleOnAllWorkspaces: (value) => { win.allWorkspaces = value },
      setMenuBarVisibility: () => {},
      loadFile: async () => {},
      loadURL: async () => {}
    })
    win.webContents = Object.assign(new EventEmitter(), { id: windows.length + 1, destroyed: false,
      isDestroyed: () => win.webContents.destroyed,
      send: (channel, ...args) => { messages.push({ win, channel, args }); if (win.webContents.destroyed) throw new Error('WebContents has been destroyed') }, setWindowOpenHandler: () => {} })
    windows.push(win)
    return win
  }
  const main = makeWindow()
  main.focus()
  const lifecycle = []
  const { registerSceneNotesWindow } = loadRendererSource('../main/scene-notes-window.ts', {
    electron: { app: { getPath: () => '/settings', whenReady: () => Promise.resolve() }, ipcMain, screen,
      BrowserWindow: { getAllWindows: () => windows, fromWebContents: (sender) => windows.find((win) => win.webContents === sender) } },
    'node:fs': { readFileSync: () => JSON.stringify(preferences), writeFileSync: (_path, data) => writes.push(JSON.parse(data)) },
    'node:path': { join: (...parts) => parts.join('/') },
    '../shared/sceneNotesProtocol': { clampNotesBounds, selectNotesContext },
    './window-appearance': { createAppWindow: makeWindow, material },
    './glass': { attachLiquidGlass: (win) => glassWindows.push(win) }
  }, { __dirname: '/app', process: { env: {} } })
  registerSceneNotesWindow((win) => lifecycle.push(win), () => main)
  const invoke = (name, sender = main, ...args) => handlers.get(`novalist:notes-${name}`)({ sender: sender.webContents }, ...args)
  return { main, makeWindow, ipcMain, invoke, windows, lifecycle, writes, glassWindows, messages }
}

test('pinned notes belong to the active workspace without floating over other apps or Spaces', () => {
  const h = harness()
  h.invoke('toggle')
  const notes = h.windows[1]
  notes.emit('ready-to-show')
  assert.equal(notes.getParentWindow(), h.main)
  assert.equal(notes.alwaysOnTop, false)
  assert.equal(notes.allWorkspaces, false)
  h.invoke('pin', notes, false)
  h.invoke('pin', notes, true)
  assert.equal(notes.alwaysOnTop, false)
  assert.equal(notes.getParentWindow(), h.main)

  const detached = h.makeWindow()
  detached.focus()
  h.ipcMain.emit('novalist:notes-context', { sender: detached.webContents }, null)
  assert.equal(notes.getParentWindow(), detached)
  h.ipcMain.emit('novalist:notes-context', { sender: h.main.webContents }, null)
  assert.equal(notes.getParentWindow(), detached)
})

test('notes attach native Liquid Glass after their renderer loads', () => {
  const h = harness('glass')
  h.invoke('toggle')
  const notes = h.windows[1]
  assert.equal(h.glassWindows.length, 0)
  notes.webContents.emit('did-finish-load')
  assert.deepEqual(h.glassWindows, [notes])
})

test('toggle reuses one reachable window and auto-hide obeys pin state', () => {
  const h = harness()
  h.invoke('toggle')
  const notes = h.windows[1]
  notes.emit('ready-to-show')
  assert.equal(h.lifecycle[0], notes)
  assert.equal(notes.bounds.x, 580)
  assert.equal(notes.visible, true)
  notes.emit('blur')
  assert.equal(notes.visible, true)
  h.invoke('pin', notes, false)
  notes.emit('blur')
  assert.equal(notes.visible, false)
  h.invoke('toggle')
  assert.equal(notes.visible, true)
  assert.equal(h.windows.length, 2)
  h.invoke('toggle')
  assert.equal(notes.visible, false)
  assert.equal(h.writes.at(-1).pinned, false)
})

test('notes state and late native events tolerate shutdown before the closed callback', () => {
  const h = harness()
  h.invoke('toggle')
  const notes = h.windows[1]
  h.invoke('pin', notes, false)
  notes.destroyed = true
  notes.webContents.destroyed = true
  assert.equal(h.invoke('state').visible, false)
  assert.doesNotThrow(() => notes.emit('blur'))
  assert.doesNotThrow(() => notes.emit('hide'))
  assert.doesNotThrow(() => notes.emit('ready-to-show'))
  assert.doesNotThrow(() => h.invoke('hide', notes))
  assert.doesNotThrow(() => h.invoke('pin', notes, true))
})

test('closing notes does not publish to a workspace whose renderer is already destroyed', () => {
  const h = harness()
  h.invoke('toggle')
  const notes = h.windows[1]
  h.main.webContents.destroyed = true
  notes.destroyed = true
  notes.webContents.destroyed = true
  assert.doesNotThrow(() => notes.emit('closed'))
})

test('closing notes before renderer load does not attach glass to a destroyed native window', () => {
  const h = harness('glass')
  h.invoke('toggle')
  const notes = h.windows[1]
  notes.destroyed = true
  notes.webContents.emit('did-finish-load')
  assert.equal(h.glassWindows.length, 0)
})

test('only the active workspace or current scene source can publish notes context', () => {
  const h = harness()
  const send = (win, value) => h.ipcMain.emit('novalist:notes-context', { sender: win.webContents }, value)
  const first = { projectPath: '/book', bookId: 'book', draftId: 'draft', epoch: 1, chapterGuid: 'chapter', sceneId: 'A', title: 'A' }
  send(h.main, first)
  h.invoke('toggle')
  const notes = h.windows[1]
  notes.focus()
  const background = h.makeWindow()
  send(background, { ...first, sceneId: 'B' })
  assert.equal(h.invoke('state').context.sceneId, 'A')
  send(notes, { ...first, sceneId: 'C' })
  assert.equal(h.invoke('state').context.sceneId, 'A')
  background.focus()
  send(background, { ...first, sceneId: 'B' })
  assert.equal(h.invoke('state').context.sceneId, 'B')
  send(h.main, null)
  assert.equal(h.invoke('state').context.sceneId, 'B')
  send(background, null)
  assert.equal(h.invoke('state').context, null)
})

test('the shared display-change handler respects the utility window minimum size', () => {
  const { clampWindowToDisplay } = loadRendererSource('../main/window-appearance.ts', {
    electron: { BrowserWindow: class {}, screen: { getDisplayMatching: () => ({ workArea: { x: 0, y: 0, width: 1200, height: 900 } }) } },
    'node:path': { join: (...parts) => parts.join('/') }, 'node:fs': { existsSync: () => false },
    './glass': { detectMaterial: () => 'opaque', materialWindowOptions: () => ({}) }
  }, { process: { platform: 'linux', getSystemVersion: () => '1', resourcesPath: '/app' }, __dirname: '/app' })
  let bounds = { x: 50, y: 50, width: 420, height: 400 }
  clampWindowToDisplay({ isDestroyed: () => false, isMaximized: () => false, isFullScreen: () => false, isMinimized: () => false,
    getBounds: () => bounds, getMinimumSize: () => [320, 260], setBounds: (value) => { bounds = value } })
  assert.deepEqual(bounds, { x: 50, y: 50, width: 420, height: 400 })
})


test('docking waits for the floating editor to save and release ownership', async () => {
  const h = harness()
  h.invoke('toggle')
  const notes = h.windows[1]
  notes.emit('ready-to-show')
  const pending = h.invoke('dock')
  const release = h.messages.find(m => m.channel === 'novalist:notes-release')
  assert.ok(release)
  assert.equal(h.invoke('state').docked, false)
  h.ipcMain.emit('novalist:notes-released', { sender: h.main.webContents }, release.args[0], true)
  assert.equal(h.invoke('state').docked, false, 'another renderer cannot acknowledge the editor')
  h.ipcMain.emit('novalist:notes-released', { sender: notes.webContents }, release.args[0], true)
  await pending
  assert.equal(notes.visible, false)
  assert.equal(h.invoke('state').docked, true)
  assert.equal(h.invoke('state').visible, true)
  h.invoke('toggle')
  assert.equal(h.invoke('state').visible, false)
  h.invoke('toggle')
  assert.equal(h.invoke('state').visible, true)
  await h.invoke('float')
  assert.equal(h.invoke('state').docked, false)
  assert.equal(notes.visible, true)
})

test('a failed save cancels docking and keeps the floating notes editable', async () => {
  const h = harness()
  h.invoke('toggle')
  const notes = h.windows[1]
  notes.emit('ready-to-show')
  const pending = h.invoke('dock')
  const release = h.messages.find(m => m.channel === 'novalist:notes-release')
  assert.ok(release)
  h.ipcMain.emit('novalist:notes-released', { sender: notes.webContents }, release.args[0], false)
  await assert.rejects(pending)
  assert.equal(h.invoke('state').docked, false)
  assert.equal(notes.visible, true)
})

test('follow/hold state survives docking and dock requests go to the main window', async () => {
  const h = harness()
  const first = { projectPath: '/book', bookId: 'book', draftId: 'draft', epoch: 1, chapterGuid: 'chapter', sceneId: 'A', title: 'A' }
  h.ipcMain.emit('novalist:notes-context', { sender: h.main.webContents }, first)
  h.invoke('toggle')
  const notes = h.windows[1]
  h.invoke('follow', notes, false)
  h.ipcMain.emit('novalist:notes-context', { sender: h.main.webContents }, { ...first, sceneId: 'B' })
  assert.equal(h.invoke('state').context.sceneId, 'A')
  h.invoke('request-dock', notes)
  assert.ok(h.messages.some(m => m.win === h.main && m.channel === 'novalist:notes-dock-request'))
  h.invoke('follow', notes, true)
  assert.equal(h.invoke('state').context.sceneId, 'B')
})


test('docked mode and visibility survive a fresh native manager without creating a floating window', async () => {
  const first = harness()
  await first.invoke('dock')
  assert.equal(first.writes.at(-1)?.docked, true)
  const restarted = harness('opaque', first.writes.at(-1))
  assert.equal(restarted.invoke('state').docked, true)
  assert.equal(restarted.invoke('state').visible, true)
  restarted.invoke('toggle')
  assert.equal(restarted.invoke('state').visible, false)
  const hidden = harness('opaque', restarted.writes.at(-1))
  assert.equal(hidden.invoke('state').docked, true)
  assert.equal(hidden.invoke('state').visible, false)
  hidden.invoke('toggle')
  assert.equal(hidden.invoke('state').visible, true)
  assert.equal(hidden.windows.length, 1)
})

test('explicitly floating notes replaces a persisted docked preference', async () => {
  const first = harness('opaque', { docked: true, dockVisible: true, pinned: false })
  await first.invoke('float')
  assert.equal(first.writes.at(-1)?.docked, false)
  assert.equal(first.invoke('state').docked, false)
  const restarted = harness('opaque', first.writes.at(-1))
  assert.equal(restarted.invoke('state').docked, false)
  restarted.invoke('toggle')
  assert.equal(restarted.windows.length, 2)
  assert.equal(restarted.invoke('state').pinned, false)
})
