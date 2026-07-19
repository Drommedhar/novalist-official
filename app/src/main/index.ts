import { app, BrowserWindow, MessageChannelMain, ipcMain, shell, nativeImage } from 'electron'
import { join } from 'node:path'
import { existsSync } from 'node:fs'
import { BackendProcess } from './backend-process'
import { attachLiquidGlass, detectMaterial, materialWindowOptions } from './glass'
import { registerDialogHandlers } from './dialogs'
import { installAppMenu } from './menu'
import { checkForUpdates } from './updater'
import { registerProtocolSchemes, registerProtocolHandlers } from './protocols'

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

  win.once('ready-to-show', () => win.show())
  if (app.isPackaged) {
    win.webContents.once('did-finish-load', () => checkForUpdates(win))
  }
  if (material === 'glass') {
    win.webContents.once('did-finish-load', () => attachLiquidGlass(win))
  }
  win.webContents.setWindowOpenHandler(({ url }) => {
    void shell.openExternal(url)
    return { action: 'deny' }
  })

  if (process.env.ELECTRON_RENDERER_URL) {
    void win.loadURL(process.env.ELECTRON_RENDERER_URL)
  } else {
    void win.loadFile(join(__dirname, '../renderer/index.html'))
  }
  return win
}

// The renderer asks for a fresh backend channel on boot (and after backend restarts).
ipcMain.on('novalist:request-backend-port', (event) => {
  const { port1, port2 } = new MessageChannelMain()
  backend.attachPort(port1)
  event.sender.postMessage('novalist:backend-port', null, [port2])
})

void app.whenReady().then(() => {
  installAppMenu()
  // Dock icon for the dev run (packaged builds get it from the app bundle).
  if (process.platform === 'darwin' && !app.isPackaged) {
    const iconPath = resolveIconPath()
    if (iconPath) app.dock?.setIcon(nativeImage.createFromPath(iconPath))
  }
  registerDialogHandlers()
  registerProtocolHandlers()
  backend.start()
  createWindow()

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
