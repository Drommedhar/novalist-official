import {
  app,
  BrowserWindow,
  MessageChannelMain,
  ipcMain,
  shell,
  nativeImage,
  screen
} from 'electron'
import { join } from 'node:path'
import { existsSync, readFileSync } from 'node:fs'
import { BackendProcess } from './backend-process'
import {
  attachLiquidGlass,
  detectMaterial,
  materialWindowOptions
} from './glass'
import { registerDialogHandlers } from './dialogs'
import { registerSpellCheckHandlers, attachSpellingMenu } from './spellcheck'
import { applyMenuTemplate, installAppMenu, type MenuLabels, type MenuNode } from './menu'
import { checkAppUpdate, downloadAndInstall } from './appUpdater'
import { createSplashWindow, setSplashStatus } from './splash'
import { registerProtocolSchemes, registerProtocolHandlers } from './protocols'
import { SCHEME, parseDeepLink, deepLinkFromArgv, type DeepLink } from './deeplink'

// Name the app before anything reads it (menu/About/dock/window title) so the
// UI never shows the default "Electron".
app.setName('Novalist')

const material = detectMaterial(process.platform, process.getSystemVersion())
const backend = new BackendProcess()

/** Resolves the app icon: packaged resources first, then the repo dev path. */
function resolveIconPath(): string | null {
  const candidates = [
    join(process.resourcesPath, 'icon.png'),
    join(__dirname, '..', '..', 'resources', 'icon.png'),
    join(__dirname, '..', '..', 'build', 'icon.png')
  ]
  return candidates.find((p) => existsSync(p)) ?? null
}

registerProtocolSchemes()

/**
 * The window that holds the project, as opposed to a torn-off pane.
 *
 * Tracked so there is something to bring back: closing it on macOS does not
 * quit the app, and a pane window left open is not a substitute for it.
 */
let mainWindow: BrowserWindow | null = null

/** A restore-down size expressed in Electron's display-independent pixels. */
function initialWindowGeometry(): {
  width: number
  height: number
  minWidth: number
  minHeight: number
} {
  const workArea = screen.getPrimaryDisplay().workAreaSize
  return {
    width: Math.min(1440, workArea.width),
    height: Math.min(900, workArea.height),
    minWidth: Math.min(760, workArea.width),
    minHeight: Math.min(520, workArea.height)
  }
}

/**
 * Keeps a restored window reachable after a monitor is removed or its display
 * scale changes. Bounds and work areas are both DIPs, so OS DPI remains native.
 */
function clampWindowToDisplay(win: BrowserWindow): void {
  if (win.isDestroyed() || win.isMaximized() || win.isFullScreen() || win.isMinimized()) return
  const bounds = win.getBounds()
  const area = screen.getDisplayMatching(bounds).workArea
  const width = Math.min(Math.max(bounds.width, Math.min(760, area.width)), area.width)
  const height = Math.min(Math.max(bounds.height, Math.min(520, area.height)), area.height)
  const x = Math.max(area.x, Math.min(bounds.x, area.x + area.width - width))
  const y = Math.max(area.y, Math.min(bounds.y, area.y + area.height - height))
  if (x !== bounds.x || y !== bounds.y || width !== bounds.width || height !== bounds.height) {
    win.setBounds({ x, y, width, height })
  }
}

function createWindow(): BrowserWindow {
  const iconPath = resolveIconPath()
  const geometry = initialWindowGeometry()
  const win = new BrowserWindow({
    ...geometry,
    show: false,
    title: 'Novalist',
    ...(iconPath ? { icon: iconPath } : {}),
    ...materialWindowOptions(material),
    webPreferences: {
      preload: join(__dirname, '../preload/index.js'),
      sandbox: false,
      contextIsolation: true,
      nodeIntegration: false,
      additionalArguments: [`--nl-material=${material}`]
    }
  })

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
  win.on('closed', () => {
    if (mainWindow === win) mainWindow = null
  })
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
 * the same renderer with the same preload, so it gets its own backend channel
 * from the handler below and needs nothing else: two windows talking to one
 * backend is what the port-per-sender design already allowed.
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
    const iconPath = resolveIconPath()
    const win = new BrowserWindow({
      width: 720,
      height: 900,
      minWidth: 360,
      minHeight: 400,
      show: false,
      title: 'Novalist',
      ...(iconPath ? { icon: iconPath } : {}),
      ...materialWindowOptions(material),
      webPreferences: {
        preload: join(__dirname, '../preload/index.js'),
        sandbox: false,
        contextIsolation: true,
        nodeIntegration: false,
        additionalArguments: [`--nl-material=${material}`]
      }
    })
    win.once('ready-to-show', () => win.show())
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
  const { port1, port2 } = new MessageChannelMain()
  backend.attachPort(port1)
  event.sender.postMessage('novalist:backend-port', null, [port2])
})

// App self-update (GitHub release → download installer → open). Extension
// updates are handled separately by the renderer via the extension store.
// Disabled in the Mac App Store build: Apple prohibits self-updating, so even a
// manual trigger must do nothing there (updates arrive via the App Store).
const isMasBuild = (process as NodeJS.Process & { mas?: boolean }).mas === true
ipcMain.handle('novalist:check-app-update', () => (isMasBuild ? null : checkAppUpdate()))
ipcMain.handle('novalist:download-app-update', (event, info) => {
  if (isMasBuild) throw new Error('Self-update is disabled in the App Store build.')
  const win = BrowserWindow.fromWebContents(event.sender)
  if (!win) throw new Error('No window for update download.')
  return downloadAndInstall(info, win)
})

// The menu bar's contents come from the renderer's command registry, because
// that registry is what decides an application-scoped command exists at all. A
// template written here beside it would be a second list, and second lists
// drift.
ipcMain.on('novalist:set-menu', (_event, nodes: MenuNode[], labels: MenuLabels) => {
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

app.on('before-quit', () => {
  backend.dispose()
})
