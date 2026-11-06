import { app, BrowserWindow, ipcMain, screen } from 'electron'
import { readFileSync, writeFileSync } from 'node:fs'
import { join } from 'node:path'
import { clampNotesBounds, selectNotesContext, type NotesBounds, type SceneNotesContext, type SceneNotesWindowState } from '../shared/sceneNotesProtocol'
import { createAppWindow, material } from './window-appearance'
import { attachLiquidGlass } from './glass'

/** A single editable notes surface, independent of whichever workspace pane
 * last selected the scene. It participates in the normal save/close protocol. */
export function registerSceneNotesWindow(attachLifecycle: (window: BrowserWindow) => void, mainWindow: () => BrowserWindow | null): void {
  let win: BrowserWindow | null = null
  let source: number | null = null
  let context: SceneNotesContext | null = null
  let pinned = true
  let incoming: SceneNotesContext | null = null
  let follow = true
  let docked = false
  let dockVisible = false
  let transferring = false
  let releaseSequence = 0
  let bounds: NotesBounds | null = null
  const state = (): SceneNotesWindowState => ({ context, pinned, docked, follow, visible: docked ? dockVisible : !!win && !win.isDestroyed() && win.isVisible() })
  const publish = (): void => {
    const snapshot = state()
    for (const target of BrowserWindow.getAllWindows()) {
      if (!target.isDestroyed() && !target.webContents.isDestroyed()) target.webContents.send('novalist:notes-state', snapshot)
    }
  }
  const save = (): void => {
    if (win && !win.isDestroyed()) bounds = win.getNormalBounds()
    try { writeFileSync(join(app.getPath('userData'), 'scene-notes-window.json'), JSON.stringify({ bounds, pinned, docked, dockVisible })) }
    catch { /* Window placement is optional; note persistence is not. */ }
  }
  const restore = (): void => {
    try {
      const stored = JSON.parse(readFileSync(join(app.getPath('userData'), 'scene-notes-window.json'), 'utf8'))
      if (typeof stored.pinned === 'boolean') pinned = stored.pinned
      if (typeof stored.docked === 'boolean') docked = stored.docked
      if (typeof stored.dockVisible === 'boolean') dockVisible = stored.dockVisible
      const value = stored.bounds
      if (value && ['x', 'y', 'width', 'height'].every((key) => Number.isFinite(value[key]))) bounds = value
    } catch { /* First use or unavailable preferences. */ }
  }
  // State is requested before the first toggle. Restore once so creating a
  // floating window later cannot overwrite a newly chosen docked preference.
  restore()
  const fit = (): void => {
    if (!win || win.isDestroyed()) return
    const current = bounds ?? win.getBounds()
    win.setBounds(clampNotesBounds(current, screen.getDisplayMatching(current).workArea))
  }
  ipcMain.on('novalist:notes-context', (event, value: SceneNotesContext | null) => {
    const sender = BrowserWindow.fromWebContents(event.sender)
    if (!sender || sender.isDestroyed() || sender === win || (!sender.isFocused() && source !== event.sender.id && source !== null)) return
    source = event.sender.id
    incoming = value
    context = selectNotesContext(context, incoming, follow)
    if (win && !win.isDestroyed() && sender.isFocused() && win.getParentWindow() !== sender) win.setParentWindow(sender)
    if (!transferring) publish()
  })
  ipcMain.handle('novalist:notes-state', () => state())
  const showFloating = (parent: BrowserWindow, toggle = false): void => {
    if (win && !win.isDestroyed()) {
      if (toggle && win.isVisible()) win.hide()
      else { win.setParentWindow(parent); fit(); win.show(); win.focus() }
      publish()
      return
    }
    // An owned window stays above its Novalist workspace without a global
    // always-on-top level or joining other apps' Spaces. fullscreenable:false
    // makes it an auxiliary window alongside its fullscreen parent on macOS.
    win = createAppWindow({ width: 420, height: 560, minWidth: 320, minHeight: 260 }, {
      parent, frame: false, titleBarStyle: 'default', minimizable: false,
      maximizable: false, fullscreenable: false, skipTaskbar: true
    })
    const created = win
    fit()
    if (material === 'glass') created.webContents.once('did-finish-load', () => { if (!created.isDestroyed()) attachLiquidGlass(created) })
    created.setMenuBarVisibility(false)
    attachLifecycle(created)
    created.once('ready-to-show', () => { if (!created.isDestroyed() && !docked) { created.show(); publish() } })
    created.webContents.setWindowOpenHandler(() => ({ action: 'deny' }))
    created.on('blur', () => { if (!pinned && !created.isDestroyed()) created.hide() })
    created.on('hide', publish)
    created.on('show', publish)
    created.on('move', save)
    created.on('resize', save)
    created.on('close', save)
    created.on('closed', () => { win = null; publish() })
    if (process.env.ELECTRON_RENDERER_URL) void created.loadURL(`${process.env.ELECTRON_RENDERER_URL}?sceneNotes=1`)
    else void created.loadFile(join(__dirname, '../renderer/index.html'), { query: { sceneNotes: '1' } })
  }
  ipcMain.handle('novalist:notes-toggle', (event) => {
    const parent = BrowserWindow.fromWebContents(event.sender)
    if (!parent || parent.isDestroyed() || parent === win || transferring) return
    if (docked) { dockVisible = !dockVisible; save(); publish(); return }
    showFloating(parent, true)
  })
  ipcMain.handle('novalist:notes-request-dock', () => {
    const main = mainWindow()
    if (!main || main.isDestroyed()) return
    main.webContents.send('novalist:notes-dock-request')
    main.show()
    main.focus()
  })
  ipcMain.handle('novalist:notes-dock', async (event) => {
    if (event.sender !== mainWindow()?.webContents || transferring) throw new Error('Notes transfer unavailable')
    transferring = true
    try {
      const editor = win
      if (!docked && editor && !editor.isDestroyed()) {
        const token = ++releaseSequence
        await new Promise<void>((resolve, reject) => {
          const finish = (ok: boolean): void => {
            clearTimeout(timer)
            ipcMain.removeListener('novalist:notes-released', released)
            ok ? resolve() : reject(new Error('Scene notes could not be saved'))
          }
          const released = (reply: Electron.IpcMainEvent, id: number, ok: boolean): void => {
            if (reply.sender === editor.webContents && id === token) finish(ok === true)
          }
          const timer = setTimeout(() => finish(false), 10_000)
          ipcMain.on('novalist:notes-released', released)
          editor.webContents.send('novalist:notes-release', token)
        })
      }
      docked = true
      dockVisible = true
      save()
      if (win && !win.isDestroyed()) win.hide()
      publish()
    } catch (error) {
      // Restore the original editor after a failed or timed-out handoff.
      publish()
      throw error
    } finally { transferring = false }
  })
  ipcMain.handle('novalist:notes-float', (event) => {
    const main = mainWindow()
    if (!main || event.sender !== main.webContents || transferring) throw new Error('Notes transfer unavailable')
    docked = false
    dockVisible = false
    save()
    showFloating(main)
    publish()
  })
  ipcMain.handle('novalist:notes-follow', (_event, value: boolean) => {
    if (typeof value !== 'boolean') return
    follow = value
    context = selectNotesContext(context, incoming, follow)
    publish()
  })
  ipcMain.handle('novalist:notes-hide', (event) => {
    if (docked && event.sender === mainWindow()?.webContents) { dockVisible = false; save(); publish(); return }
    if (win && !win.isDestroyed() && event.sender === win.webContents) win.hide()
  })
  ipcMain.handle('novalist:notes-pin', (event, value: boolean) => {
    if (!win || win.isDestroyed() || event.sender !== win.webContents || typeof value !== 'boolean') return
    pinned = value
    save()
    publish()
  })
  app.whenReady().then(() => {
    screen.on('display-removed', fit)
    screen.on('display-metrics-changed', fit)
  }).catch(() => {})
}
