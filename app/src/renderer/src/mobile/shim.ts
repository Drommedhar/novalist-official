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

import i18next from 'i18next'

import { installProjectImageLoader, clearProjectImageCache } from './projectImages'

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

function manuscriptExtensions(options?: { extensions?: string[] }): string[] {
  if (!Array.isArray(options?.extensions)) return []
  return [
    ...new Set(
      options.extensions
        .filter((extension): extension is string => typeof extension === 'string')
        .map((extension) => extension.trim().replace(/^\./, '').toLowerCase())
        .filter((extension) => /^[a-z0-9]+$/.test(extension))
    )
  ]
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
  defaultProjectRoot: () => hostCall<string | null>('defaultProjectRoot', []),
  saveFile: (defaultName) => hostCall<string | null>('saveFile', [defaultName]),
  // Window capture is an Electron capability with no iOS equivalent, so map
  // image export reports failure rather than pretending to have written a file.
  captureRegion: () => Promise.resolve(false),
  // An image request opens a native "Photo Library / Browse Files" sheet, so the
  // host needs those three labels localized; the renderer owns the locale files,
  // the native side only renders what it is handed.
  pickFile: (title, mode, options) =>
    hostCall<string | null>('pickFile', [
      title,
      mode ?? 'all',
      mode === 'images'
        ? [
            i18next.t('mobile.imageSource.photos'),
            i18next.t('mobile.imageSource.files'),
            i18next.t('dialog.cancel')
          ]
        : mode === 'manuscript'
          ? manuscriptExtensions(options)
          : [],
      mode === 'manuscript' ? (options?.scrivenerAccessTitle ?? title) : ''
    ]),
  // The native host retains a security-scoped URL while preview/import reads
  // the selection (and the whole parent project for a direct .scrivx).
  releasePickedFile: (path) => hostCall<void>('releasePickedFile', [path]),
  // iOS spell-checks a contenteditable natively once the element carries
  // spellcheck="true", which the editor already sets from the same setting.
  // There is no session to configure and no menu for us to build: the system
  // supplies its own correction UI and its own "learn word" gesture.
  applySpellCheck: () => Promise.resolve([]),
  spellCheckLanguages: () => Promise.resolve([]),
  setSpellCheckMenuLabels: () => {},
  onSpellCheckWordAdded: () => {},
  // The platform keyboard owns spelling on mobile: there is no context menu
  // of ours to fold suggestions into, and no misspelled range to replace.
  onSpellingContext: () => {},
  replaceMisspelling: () => {},
  // Mobile has no desktop-style file drag-and-drop, so dropped-file paths never
  // arise; the picker is the only way in.
  filePath: () => '',
  openExternal: (target) => hostCall<boolean>('openExternal', [target]),
  revealPath: (target) => hostCall<boolean>('revealPath', [target]),
  copyText: (text) => {
    void hostCall('copyText', [text])
  },
  // Mobile-only: show/hide the native Liquid Glass tab bar (hidden on welcome).
  setNavVisible: (visible: boolean) => {
    void hostCall('setNavVisible', [visible])
  },
  // Mobile-only: push localized titles onto the native tab bar, in tab order
  // (dashboard, manuscript, codex, search, more). Re-pushed on language change.
  setTabTitles: (titles: string[]) => {
    void hostCall('setTabTitles', [titles])
  },
  // Mobile-only: move the native bar's highlight to this tab index, for a tab
  // change the web made rather than the writer tapping.
  setSelectedTab: (index: number) => {
    void hostCall('setSelectedTab', [index])
  },
  // Mobile-only: show/hide the native Liquid Glass Plan popover with the given
  // localized item labels; selection comes back via window.__novalistPlanSelect.
  setPlanningMenuOpen: (open: boolean, labels: string[]) => {
    void hostCall('setPlanningMenuOpen', [open, labels])
  },
  // Tablet-only: localized titles for the native iPad sidebar, in the order the
  // native SidebarItems table declares (see TABLET_DESTINATIONS).
  setSidebarTitles: (titles: string[]) => {
    void hostCall('setSidebarTitles', [titles])
  },
  // Tablet-only: move the sidebar highlight to a destination key.
  setSidebarSelection: (key: string) => {
    void hostCall('setSidebarSelection', [key])
  },
  // Tablet-only: collapse the sidebar to an icon-only rail, or expand it back.
  setSidebarCollapsed: (collapsed: boolean) => {
    void hostCall('setSidebarCollapsed', [collapsed])
  },
  // Ask the native side to re-push the current size class through
  // window.__novalistLayout; the first pass can run before the bundle loads.
  requestLayout: () => {
    void hostCall('requestLayout', [])
  },
  readClipboardImage: () => hostCall<string | null>('readClipboardImage', []),
  // Track the open project's folder natively so project images can be read, and
  // drop the resolved-image cache so a new project can't reuse the old one's.
  setProjectRoot: (root) => {
    clearProjectImageCache()
    void hostCall('setProjectRoot', [root])
  },
  // Read a project-relative image as a data: URI (novalist-project:// has no
  // scheme handler in the mobile WebView; projectImages rewrites those srcs).
  readProjectImage: (path: string) => hostCall<string | null>('readProjectImage', [path]),
  // Security-scoped external folders: resolve the native bookmark and start/stop
  // access around opening a project (mirrors the Mac App Store contract). A false
  // result makes the renderer re-prompt for the folder.
  beginProjectAccess: (path: string) => hostCall<boolean>('beginProjectAccess', [path]),
  endProjectAccess: (path: string) => {
    void hostCall('endProjectAccess', [path])
  },
  openPaneWindow: () => Promise.resolve(),
  registerExtensionRoots: () => Promise.resolve(),
  // Store-delivered updates: no self-update on mobile.
  checkAppUpdate: async () => null,
  hasDetachedPanes: async () => false,
  downloadAppUpdate: async () => ({ filePath: '', launchToken: null }),
  launchAppUpdate: async () => {},
  // No protocol handler on mobile: there is nothing to register a scheme with.
  takeDeepLink: () => Promise.resolve(null),
  onDeepLink: () => {},
  updatesChecked: () => {}
}

window.novalist = novalist

// Rewrite novalist-project:// <img> srcs to data URIs (no custom-scheme handler
// in the mobile WebView).
installProjectImageLoader()
