import { contextBridge, ipcRenderer, webUtils } from 'electron'

const material =
  process.argv.find((a) => a.startsWith('--nl-material='))?.split('=')[1] ?? 'opaque'

// True only in the Mac App Store (sandboxed) build. Apple forbids self-updating
// there (the store delivers updates), so the download-and-run-installer flow is
// disabled for MAS.
const isMas = (process as NodeJS.Process & { mas?: boolean }).mas === true

/**
 * Minimal privileged surface. The backend MessagePort cannot cross the context
 * bridge directly, so it is forwarded to the page via window.postMessage and
 * picked up by the RPC client from the message event's ports array.
 */
contextBridge.exposeInMainWorld('novalist', {
  material,
  platform: process.platform,
  // True only in the Mac App Store build. The renderer uses this to hide
  // self-update UI (the store delivers updates there).
  isMas,
  // Off in headless/e2e runs (NOVALIST_NO_SPLASH) so a network update check
  // never pops a modal that blocks tests. Also off in the Mac App Store build,
  // where self-updating is prohibited.
  autoUpdate: !process.env.NOVALIST_NO_SPLASH && !isMas,
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
  /** Absolute path of a dropped File. Electron removed File.path, so resolving
   *  it has to happen here in the preload. Empty string when unavailable. */
  filePath(file: File): string {
    try {
      return webUtils.getPathForFile(file)
    } catch {
      return ''
    }
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
  // Sandbox (MAS) access to a project folder reopened from a stored path. Returns
  // true when access is available (always so off MAS); false means re-prompt.
  beginProjectAccess(path: string): Promise<boolean> {
    return ipcRenderer.invoke('novalist:begin-project-access', path)
  },
  endProjectAccess(path: string): void {
    ipcRenderer.send('novalist:end-project-access', path)
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
