/**
 * Mobile (.NET MAUI HybridWebView) shim for `window.novalist`.
 *
 * Loaded ONLY by the mobile build (index.mobile.html), before main.tsx. It
 * reproduces the Electron preload contract so the renderer boots and talks to
 * the in-process C# backend without any change to the renderer itself.
 *
 * Two channels ride the one HybridWebView raw-message pipe, disjoint by the
 * first byte of each JS->native message:
 *   - RPC transport (Phase 1): base64 of LSP-framed JSON-RPC bytes. Never starts
 *     with '{' (base64 alphabet excludes it). requestBackendPort() mirrors the
 *     Electron preload - it creates a MessageChannel, hands port1 to the page,
 *     and pumps port2's bytes across - so rpc/client.ts is untouched.
 *   - Host bridge (Phase 2): JSON `{id,method,args}` (starts with '{') for the
 *     native window.novalist surface (pickers, clipboard, open-external, ...).
 *
 * native->JS:  RPC   -> window.__novalistRecv(<base64 bytes>)
 *              host  -> window.__novalistHostResult(<base64 json>)
 */

type HybridWebViewApi = { SendRawMessage?: (message: string) => void }

function hwv(): HybridWebViewApi | undefined {
  return (window as unknown as { HybridWebView?: HybridWebViewApi }).HybridWebView
}

function sendRaw(message: string): void {
  hwv()?.SendRawMessage?.(message)
}

function bytesToBase64(bytes: Uint8Array): string {
  let binary = ''
  const chunk = 0x8000
  for (let i = 0; i < bytes.length; i += chunk) {
    binary += String.fromCharCode.apply(null, Array.from(bytes.subarray(i, i + chunk)))
  }
  return btoa(binary)
}

function base64ToBytes(base64: string): Uint8Array {
  const binary = atob(base64)
  const out = new Uint8Array(binary.length)
  for (let i = 0; i < binary.length; i++) out[i] = binary.charCodeAt(i)
  return out
}

// --- RPC transport (Phase 1) ---------------------------------------------

let backendPort: MessagePort | null = null

;(window as unknown as { __novalistRecv: (base64: string) => void }).__novalistRecv = (
  base64: string
) => {
  backendPort?.postMessage(base64ToBytes(base64))
}

function requestBackendPort(): void {
  const channel = new MessageChannel()
  backendPort = channel.port2
  backendPort.onmessage = (event) => sendRaw(bytesToBase64(event.data as Uint8Array))
  backendPort.start()
  window.postMessage({ novalist: 'backend-port' }, '*', [channel.port1])
}

// --- Host bridge (Phase 2) -----------------------------------------------

type Pending = { resolve: (v: unknown) => void; reject: (e: Error) => void }
const pendingHostCalls = new Map<number, Pending>()
let nextHostCallId = 1

;(window as unknown as { __novalistHostResult: (base64: string) => void }).__novalistHostResult = (
  base64: string
) => {
  const text = new TextDecoder().decode(base64ToBytes(base64))
  const msg = JSON.parse(text) as { id: number; ok: boolean; result?: unknown; error?: string }
  const pending = pendingHostCalls.get(msg.id)
  if (!pending) return
  pendingHostCalls.delete(msg.id)
  if (msg.ok) pending.resolve(msg.result)
  else pending.reject(new Error(msg.error ?? 'host call failed'))
}

function hostCall<T>(method: string, args: unknown[]): Promise<T> {
  const id = nextHostCallId++
  const promise = new Promise<T>((resolve, reject) => {
    pendingHostCalls.set(id, { resolve: resolve as (v: unknown) => void, reject })
  })
  sendRaw(JSON.stringify({ id, method, args }))
  return promise
}

// --- window.novalist -----------------------------------------------------

const novalist: Window['novalist'] = {
  material: 'opaque',
  // iOS is Darwin-based; gives the renderer Mac-like key/gesture behavior.
  platform: 'darwin',
  isMobile: true,
  isMas: false,
  autoUpdate: false,
  requestBackendPort,
  pickFolder: (title) => hostCall<string | null>('pickFolder', [title]),
  saveFile: (defaultName) => hostCall<string | null>('saveFile', [defaultName]),
  pickFile: (title, mode) => hostCall<string | null>('pickFile', [title, mode ?? 'all']),
  openExternal: (target) => hostCall<boolean>('openExternal', [target]),
  revealPath: (target) => hostCall<boolean>('revealPath', [target]),
  copyText: (text) => {
    void hostCall('copyText', [text])
  },
  // Mobile-only: show/hide the native Liquid Glass tab bar (hidden on welcome).
  setNavVisible: (visible: boolean) => {
    void hostCall('setNavVisible', [visible])
  },
  readClipboardImage: () => hostCall<string | null>('readClipboardImage', []),
  // App-container storage (Phase 2): every project path is inside the sandbox and
  // always accessible, so these are no-ops. Security-scoped external folders are
  // a later addition.
  setProjectRoot: () => {},
  beginProjectAccess: async () => true,
  endProjectAccess: () => {},
  registerExtensionRoots: () => {},
  // Store-delivered updates: no self-update on mobile.
  checkAppUpdate: async () => null,
  downloadAppUpdate: async () => '',
  updatesChecked: () => {}
}

window.novalist = novalist
