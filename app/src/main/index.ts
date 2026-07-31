import { app, BrowserWindow, MessageChannelMain, ipcMain, shell, nativeImage } from 'electron'
import { join } from 'node:path'
import { existsSync } from 'node:fs'
import { BackendProcess } from './backend-process'
import {
  attachLiquidGlass,
  detectMaterial,
  materialWindowOptions,
  DEFAULT_TITLE_BAR_OVERLAY
} from './glass'
import { registerDialogHandlers } from './dialogs'
import { registerSpellCheckHandlers, attachSpellingMenu } from './spellcheck'
import { installAppMenu } from './menu'
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

function createWindow(): BrowserWindow {
  const iconPath = resolveIconPath()
  const win = new BrowserWindow({
    width: 1440,
    height: 900,
    minWidth: 960,
    minHeight: 600,
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
  return win
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
 */
ipcMain.handle('novalist:open-pane-window', (_event, view: string) => {
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

  if (process.env.ELECTRON_RENDERER_URL) {
    void win.loadURL(`${process.env.ELECTRON_RENDERER_URL}?pane=${encodeURIComponent(view)}`)
  } else {
    void win.loadFile(join(__dirname, '../renderer/index.html'), { query: { pane: view } })
  }
})

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

// Repaints the system-drawn window controls when the renderer's theme changes.
// Only meaningful where the title bar is hidden behind an overlay; setting it
// on a window without one throws, so the material gates the call.
ipcMain.on('novalist:set-titlebar-colors', (event, color: string, symbolColor: string) => {
  if (material !== 'opaque') return
  const win = BrowserWindow.fromWebContents(event.sender)
  if (!win || win.isDestroyed()) return
  try {
    win.setTitleBarOverlay({ color, symbolColor, height: DEFAULT_TITLE_BAR_OVERLAY.height })
  } catch {
    // Linux builds without overlay support: the toolbar still themes itself.
  }
})

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
  installAppMenu()
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

  app.on('activate', () => {
    if (BrowserWindow.getAllWindows().length === 0) createWindow()
  })
})

app.on('window-all-closed', () => {
  if (process.platform !== 'darwin') app.quit()
})

app.on('before-quit', () => {
  backend.dispose()
})
