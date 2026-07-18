import { app, BrowserWindow, MessageChannelMain, ipcMain, shell } from 'electron'
import { join } from 'node:path'
import { BackendProcess } from './backend-process'
import { attachLiquidGlass, detectMaterial, materialWindowOptions } from './glass'
import { registerDialogHandlers } from './dialogs'
import { registerProtocolSchemes, registerProtocolHandlers } from './protocols'

const material = detectMaterial(process.platform, process.getSystemVersion())
const backend = new BackendProcess()

registerProtocolSchemes()

function createWindow(): BrowserWindow {
  const win = new BrowserWindow({
    width: 1440,
    height: 900,
    minWidth: 960,
    minHeight: 600,
    show: false,
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
