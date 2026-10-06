import {
  app,
  BrowserWindow,
  MessageChannelMain,
  ipcMain,
  shell,
  nativeImage,
  screen,
  type WebContents
} from 'electron'
import { join } from 'node:path'
import { readFileSync } from 'node:fs'
import { BackendProcess } from './backend-process'
import { WorkspaceWindows } from './workspace-windows'
import { WindowCloseCoordinator } from './window-close'
import type { WorkspaceEvent } from '../shared/workspaceProtocol'
import { attachLiquidGlass } from './glass'
import { clampWindowToDisplay, createAppWindow, initialWindowGeometry, material, resolveIconPath } from './window-appearance'
import { registerDialogHandlers } from './dialogs'
import { registerSpellCheckHandlers, attachSpellingMenu } from './spellcheck'
import { applyMenuTemplate, installAppMenu, type MenuLabels, type MenuNode } from './menu'
import { registerUpdateHandlers } from './update-handlers'
import { createSplashWindow, setSplashStatus } from './splash'
import { registerProtocolSchemes, registerProtocolHandlers } from './protocols'
import { SCHEME, parseDeepLink, deepLinkFromArgv, type DeepLink } from './deeplink'
import { installFocusWindow } from './focus-window'

// Name the app before anything reads it (menu/About/dock/window title) so the
// UI never shows the default "Electron".
app.setName('Novalist')

const backend = new BackendProcess()
const workspaceWindows = new WorkspaceWindows(backend.router, 15_000, (error) => backend.requireResync(error))
const windowCloses = new WindowCloseCoordinator()
backend.onNotification((message) => workspaceWindows.receive(message))
backend.onRestart(() => workspaceWindows.recover())
backend.onResync(() => workspaceWindows.resync())

function attachBackendPort(sender: WebContents): void {
  const { port1, port2 } = new MessageChannelMain()
  backend.attachPort(sender.id, port1)
  if (sender === mainWindow?.webContents) backend.router.setDialogOwner(sender.id)
  sender.postMessage('novalist:backend-port', null, [port2])
}

registerProtocolSchemes()

/**
 * The main shell owns application dialogs. Its close drains and closes every
 * workspace window; macOS can reopen the shell against the retained backend.
 */
let mainWindow: BrowserWindow | null = null
let setFocusWindow: ((enabled: boolean) => Promise<void>) | null = null
const approvedCloses = new Set<number>()
let closeRequest: Promise<void> | null = null
let quitRequested = false

function prepareWindowClose(win: BrowserWindow, quit: boolean): void {
  quitRequested ||= quit
  if (closeRequest) return
  const all = quit || win === mainWindow
  const targets = all ? BrowserWindow.getAllWindows() : [win]
  const owners = targets.map((target) => target.webContents.id)
  const backupOwner = all ? mainWindow?.webContents.id : undefined
  const operation = windowCloses.prepare(owners, backupOwner).then(() => {
    if (quitRequested && !all && mainWindow && !mainWindow.isDestroyed()) {
      // The current operation only prepared one detached pane. Escalating to
      // quit needs a fresh global transaction, including the main window.
      closeRequest = null
      prepareWindowClose(mainWindow, true)
      return
    }
    for (const target of targets) if (!target.isDestroyed()) approvedCloses.add(target.id)
    if (quitRequested) app.quit()
    else for (const target of targets) if (!target.isDestroyed()) target.close()
  }).catch(() => {}).finally(() => { if (closeRequest === operation) { closeRequest = null; quitRequested = false } })
  closeRequest = operation
}

ipcMain.on('novalist:close-handler-ready', (event, ready: boolean) => {
  const win = BrowserWindow.fromWebContents(event.sender)
  if (!win) return
  if (!ready) { windowCloses.remove(event.sender.id); return }
  windowCloses.register(event.sender.id, {
    send: (token, stage) => event.sender.send('novalist:prepare-close', token, stage),
    focus: () => { if (win.isMinimized()) win.restore(); win.show(); win.focus() }
  })
})
ipcMain.on('novalist:close-prepared', (event, token: string, saved: boolean) => {
  windowCloses.acknowledge(event.sender.id, token, saved)
})
ipcMain.on('novalist:workspace-ready', (event, ready: boolean) => {
  if (ready) workspaceWindows.register(event.sender.id, { send: (change) => event.sender.send('novalist:workspace-event', change) })
  else workspaceWindows.remove(event.sender.id)
})
ipcMain.on('novalist:workspace-ack', (event, token: string, phase: WorkspaceEvent['phase'], saved: boolean, error?: string) => {
  workspaceWindows.acknowledge(event.sender.id, token, phase, saved, error)
})
ipcMain.handle('novalist:workspace-snapshot', (event) => workspaceWindows.snapshot(event.sender.id))
ipcMain.handle('novalist:workspace-retry', () => backend.retryRecovery())

function attachWindowLifecycle(win: BrowserWindow): void {
  const owner = win.webContents.id
  win.on('close', (event) => {
    if (approvedCloses.has(win.id)) return
    if (!windowCloses.has(owner) && win !== mainWindow) return
    event.preventDefault()
    prepareWindowClose(win, false)
  })
  const release = (): void => {
    windowCloses.remove(owner)
    workspaceWindows.remove(owner)
    backend.detachClient(owner)
  }
  win.webContents.on('render-process-gone', release)
  win.on('closed', () => {
    release()
    approvedCloses.delete(win.id)
    if (mainWindow === win) mainWindow = null
  })
}

function createWindow(): BrowserWindow {
  const win = createAppWindow(initialWindowGeometry())

  setFocusWindow = installFocusWindow(win)
  if (material === 'glass') {
    win.webContents.once('did-finish-load', () => attachLiquidGlass(win))
  }
  win.webContents.setWindowOpenHandler(({ url }) => {
    void shell.openExternal(url)
    return { action: 'deny' }
  })
  // Right-clicking a misspelling offers corrections and "add to dictionary".
  // The labels come from the renderer so they follow the UI language; English
  // stands in until the renderer has pushed its own.
  attachSpellingMenu(win, () => spellingMenuLabels)

  if (process.env.ELECTRON_RENDERER_URL) {
    void win.loadURL(process.env.ELECTRON_RENDERER_URL)
  } else {
    void win.loadFile(join(__dirname, '../renderer/index.html'))
  }
  mainWindow = win
  win.on('unmaximize', () => clampWindowToDisplay(win))
  attachWindowLifecycle(win)
  return win
}

/**
 * The main window, opened if it is gone and brought forward if it is not.
 *
 * Closing the last window on macOS does not quit Novalist - the dock icon and
 * the menu bar stay - so there has to be a way back to it. There was not: the
 * dock-icon path made a window and left it hidden, because the reveal belongs
 * to the startup sequence that waits for the splash, and nothing was doing that
 * afterwards. The app then believed it had a window, so it never made another
 * one, and the only way back was to quit and relaunch. A torn-off pane window
 * counts for even less: it kept the window list non-empty while the project
 * itself had nowhere to be.
 */
function showMainWindow(): void {
  if (mainWindow && !mainWindow.isDestroyed()) {
    if (mainWindow.isMinimized()) mainWindow.restore()
    if (!mainWindow.isVisible()) mainWindow.show()
    mainWindow.focus()
    return
  }
  const win = createWindow()
  win.once('ready-to-show', () => {
    if (win.isDestroyed()) return
    win.maximize()
    win.show()
    win.focus()
  })
}

/**
 * A second window showing one view.
 *
 * The Codex on another monitor while the manuscript stays where it is. It runs
 * the same renderer with the same preload. Its independent RPC channel joins
 * the shared project/book/draft; the workspace coordinator owns transitions.
 *
 * Smaller and without a minimum width, because a torn-off pane is usually
 * narrow on purpose - a column of notes beside a full-screen editor.
 *
 * The project and scene travel with the request. The window used to open
 * whatever project was most recent and no scene at all, so tearing off the
 * editor produced a window saying "open a project" that had no binder to open
 * one with.
 */
ipcMain.handle(
  'novalist:open-pane-window',
  (
    _event,
    request: {
      view: string
      projectPath: string | null
      chapterGuid: string | null
      sceneId: string | null
    }
  ) => {
    const win = createAppWindow({ width: 720, height: 900, minWidth: 360, minHeight: 400 })
    win.once('ready-to-show', () => win.show())
    attachWindowLifecycle(win)
    attachSpellingMenu(win, () => spellingMenuLabels)

    const query: Record<string, string> = { pane: request.view }
    if (request.projectPath) query.project = request.projectPath
    if (request.chapterGuid) query.chapter = request.chapterGuid
    if (request.sceneId) query.scene = request.sceneId

    if (process.env.ELECTRON_RENDERER_URL) {
      const search = new URLSearchParams(query).toString()
      void win.loadURL(`${process.env.ELECTRON_RENDERER_URL}?${search}`)
    } else {
      void win.loadFile(join(__dirname, '../renderer/index.html'), { query })
    }
  }
)

let spellingMenuLabels = {
  addToDictionary: 'Add to dictionary',
  noSuggestions: 'No suggestions'
}
ipcMain.on('novalist:spellcheck-menu-labels', (_event, labels: typeof spellingMenuLabels) => {
  spellingMenuLabels = labels
})

// The renderer asks for a fresh backend channel on boot (and after backend restarts).
ipcMain.on('novalist:request-backend-port', (event) => {
  attachBackendPort(event.sender)
})

ipcMain.handle('novalist:set-focus-window', (event, enabled: boolean) => {
  if (event.sender === mainWindow?.webContents && typeof enabled === 'boolean') {
    return setFocusWindow?.(enabled)
  }
})

registerUpdateHandlers(() => mainWindow, windowCloses, approvedCloses)

// The menu bar's contents come from the renderer's command registry, because
// that registry is what decides an application-scoped command exists at all. A
// template written here beside it would be a second list, and second lists
// drift.
ipcMain.on('novalist:main-command', (event, command: string) => {
  if (!BrowserWindow.fromWebContents(event.sender) || typeof command !== 'string') return
  showMainWindow()
  mainWindow?.webContents.send('novalist:menu-command', command)
})
ipcMain.on('novalist:set-menu', (event, nodes: MenuNode[], labels: MenuLabels) => {
  if (event.sender !== mainWindow?.webContents) return
  try {
    applyMenuTemplate(nodes, labels)
  } catch (error) {
    // Electron refuses a template it cannot parse - an accelerator in a form it
    // does not know is the likely cause. Losing the new menu is bad; taking the
    // main process down with it, while the writer has a scene open, is worse.
    console.error('[menu] the renderer described a menu Electron refused:', error)
  }
})

// The window controls are the system's own again now that Windows and Linux
// keep their native title bar, so there is nothing here to repaint. Kept as a
// no-op rather than removed from the preload, so an older renderer talking to a
// newer main process does not throw on a channel that has gone.
ipcMain.on('novalist:set-titlebar-colors', () => {})

/**
 * One explicit whole-interface scale. Native menu shortcuts, Settings and
 * detached panes all call this bridge instead of accumulating hidden Chromium
 * page zoom independently.
 */
ipcMain.handle('novalist:set-ui-scale', (event, requested: number) => {
  const factor = Math.max(0.75, Math.min(1.5, Number.isFinite(requested) ? requested : 1))
  event.sender.setZoomFactor(factor)
  return factor
})

/** Content-free display facts for Settings -> Diagnostics and regression tests. */
ipcMain.handle('novalist:display-diagnostics', (event) => {
  const win = BrowserWindow.fromWebContents(event.sender)
  if (!win || win.isDestroyed()) return null
  const display = screen.getDisplayMatching(win.getBounds())
  return {
    zoomFactor: event.sender.getZoomFactor(),
    scaleFactor: display.scaleFactor,
    windowBounds: win.getBounds(),
    contentBounds: win.getContentBounds(),
    workArea: display.workArea
  }
})

/**
 * The installed app version.
 *
 * The renderer only ever knew the core process's version - the one the status
 * bar shows - so About had no way to name the application itself.
 *
 * Read from the manifest rather than through app.getVersion(), which is only
 * right in a packaged build: started as `electron out/main/index.js` the app
 * path is the folder of that script, there is no manifest in it, and Electron
 * answers with its own version - which is why the app has to name itself above
 * as well. `../../package.json` is the manifest in both layouts: beside `out/`
 * in a checkout, and at the root of the asar in a package.
 */
function installedVersion(): string {
  try {
    const manifest = JSON.parse(
      readFileSync(join(__dirname, '../../package.json'), 'utf8')
    ) as { version?: string }
    if (manifest.version) return manifest.version
  } catch {
    /* No readable manifest - fall back to whatever Electron believes. */
  }
  return app.getVersion()
}

ipcMain.handle('novalist:app-version', () => installedVersion())

/**
 * A novalist:// link that arrived before the renderer could take it.
 *
 * A link is usually what starts the app, so it lands well before anything is
 * listening. Holding it and handing it over on request is the difference
 * between a link that works cold and one that only works when Novalist is
 * already open.
 */
let pendingDeepLink: DeepLink | null = deepLinkFromArgv(process.argv)

function deliverDeepLink(link: DeepLink | null): void {
  if (!link) return
  const win = BrowserWindow.getAllWindows()[0]
  if (!win || win.isDestroyed()) {
    pendingDeepLink = link
    return
  }
  if (win.isMinimized()) win.restore()
  win.focus()
  win.webContents.send('novalist:deep-link', link)
}

// The renderer asks once it is ready, which is how a cold start gets its link.
ipcMain.handle('novalist:take-deep-link', () => {
  const link = pendingDeepLink
  pendingDeepLink = null
  return link
})

// One window owns the project, so a second launch hands its link over rather
// than starting a rival instance on the same folder.
if (!app.requestSingleInstanceLock()) {
  app.quit()
} else {
  app.on('second-instance', (_event, argv) => deliverDeepLink(deepLinkFromArgv(argv)))
  // macOS delivers links as an event rather than as arguments.
  app.on('open-url', (event, url) => {
    event.preventDefault()
    deliverDeepLink(parseDeepLink(url))
  })
}

void app.whenReady().then(() => {
  installAppMenu(showMainWindow)
  // Registered on every start: an install, a move or a reinstall all leave the
  // registration pointing somewhere else or nowhere.
  if (app.isPackaged) app.setAsDefaultProtocolClient(SCHEME)
  // Dock icon for the dev run (packaged builds get it from the app bundle).
  if (process.platform === 'darwin' && !app.isPackaged) {
    const iconPath = resolveIconPath()
    if (iconPath) app.dock?.setIcon(nativeImage.createFromPath(iconPath))
  }
  registerDialogHandlers()
  registerSpellCheckHandlers()
  registerProtocolHandlers()
  backend.start()

  // Create the main window first (kept hidden), then show a splash over it while
  // the renderer runs the startup update check - mirroring the Avalonia splash.
  // Skipped under NOVALIST_NO_SPLASH so e2e's app.firstWindow() deterministically
  // hits the renderer and startup is not gated on a network check.
  const win = createWindow()
  const reclampOpenWindows = (): void => {
    for (const open of BrowserWindow.getAllWindows()) clampWindowToDisplay(open)
  }
  screen.on('display-metrics-changed', reclampOpenWindows)
  screen.on('display-added', reclampOpenWindows)
  screen.on('display-removed', reclampOpenWindows)
  const splash = process.env.NOVALIST_NO_SPLASH ? null : createSplashWindow(resolveIconPath())
  setSplashStatus(splash, 'Checking for updates…')

  // Open maximised. maximize() also shows the window, so it has to wait until
  // the splash is out of the way - calling it at construction would reveal the
  // main window behind the splash. The constructor's width/height stay as the
  // restore-down size, so un-maximising gives a sensible window rather than one
  // still filling the screen.
  const reveal = (): void => {
    if (win.isDestroyed()) return
    if (splash && !splash.isDestroyed()) splash.close()
    if (!win.isVisible()) {
      win.maximize()
      win.show()
    }
  }
  if (splash) {
    // The renderer signals when the app+extension update check has finished;
    // a safety timeout reveals anyway so startup never hangs on the network.
    ipcMain.once('novalist:updates-checked', reveal)
    setTimeout(reveal, 15000)
  } else {
    win.once('ready-to-show', reveal)
  }

  app.on('activate', () => showMainWindow())
})

app.on('window-all-closed', () => {
  if (process.platform !== 'darwin') app.quit()
})

app.on('before-quit', (event) => {
  const win = mainWindow ?? BrowserWindow.getAllWindows().find((candidate) => windowCloses.has(candidate.webContents.id))
  if (!win || win.isDestroyed() || approvedCloses.has(win.id)) return
  event.preventDefault()
  prepareWindowClose(win, true)
})

app.on('will-quit', () => {
  backend.dispose()
})
