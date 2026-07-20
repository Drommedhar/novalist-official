import { contextBridge, ipcRenderer } from 'electron'

const material =
  process.argv.find((a) => a.startsWith('--nl-material='))?.split('=')[1] ?? 'opaque'

/**
 * Minimal privileged surface. The backend MessagePort cannot cross the context
 * bridge directly, so it is forwarded to the page via window.postMessage and
 * picked up by the RPC client from the message event's ports array.
 */
contextBridge.exposeInMainWorld('novalist', {
  material,
  platform: process.platform,
  // Off in headless/e2e runs (NOVALIST_NO_SPLASH) so a network update check
  // never pops a modal that blocks tests.
  autoUpdate: !process.env.NOVALIST_NO_SPLASH,
  requestBackendPort(): void {
    ipcRenderer.send('novalist:request-backend-port')
  },
  pickFolder(title: string): Promise<string | null> {
    return ipcRenderer.invoke('novalist:pick-folder', title)
  },
  saveFile(defaultName: string): Promise<string | null> {
    return ipcRenderer.invoke('novalist:save-file', defaultName)
  },
  pickFile(title: string, mode?: 'images' | 'all'): Promise<string | null> {
    return ipcRenderer.invoke('novalist:pick-file', title, mode)
  },
  openExternal(target: string): Promise<boolean> {
    return ipcRenderer.invoke('novalist:open-external', target)
  },
  revealPath(target: string): Promise<boolean> {
    return ipcRenderer.invoke('novalist:reveal-path', target)
  },
  copyText(text: string): void {
    ipcRenderer.send('novalist:copy-text', text)
  },
  readClipboardImage(): Promise<string | null> {
    return ipcRenderer.invoke('novalist:read-clipboard-image')
  },
  setProjectRoot(root: string | null): void {
    ipcRenderer.send('novalist:set-project-root', root)
  },
  registerExtensionRoots(roots: Record<string, string>): void {
    ipcRenderer.send('novalist:register-ext-roots', roots)
  },
  // App self-update (ported download-and-run-installer flow).
  checkAppUpdate(): Promise<unknown> {
    return ipcRenderer.invoke('novalist:check-app-update')
  },
  downloadAppUpdate(info: unknown): Promise<string> {
    return ipcRenderer.invoke('novalist:download-app-update', info)
  },
  // Tells main the startup update check finished, so it can close the splash.
  updatesChecked(): void {
    ipcRenderer.send('novalist:updates-checked')
  }
})

ipcRenderer.on('novalist:update-progress', (_event, percent: number) => {
  window.postMessage({ novalist: 'update-progress', percent }, '*')
})

ipcRenderer.on('novalist:backend-port', (event) => {
  window.postMessage({ novalist: 'backend-port' }, '*', event.ports)
})

ipcRenderer.on('novalist:menu-command', (_event, command: string) => {
  window.postMessage({ novalist: 'menu-command', command }, '*')
})
