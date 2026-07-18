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
  }
})

ipcRenderer.on('novalist:backend-port', (event) => {
  window.postMessage({ novalist: 'backend-port' }, '*', event.ports)
})
