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
  }
})

ipcRenderer.on('novalist:update-available', (_event, version: string) => {
  window.postMessage({ novalist: 'update-available', version }, '*')
})

ipcRenderer.on('novalist:backend-port', (event) => {
  window.postMessage({ novalist: 'backend-port' }, '*', event.ports)
})
