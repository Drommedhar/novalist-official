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
import { HostCallChannel } from './hostCalls'

function sendRaw(message: string): void {
  if (!window.HybridWebView?.SendRawMessage) throw new Error('The mobile host bridge is unavailable.')
  window.HybridWebView.SendRawMessage(message)
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


let backendPort: MessagePort | null = null
let bridgeFailed = false
let ready: Promise<void> | null = null

window.__novalistRecv = (base64: string) => {
  if (!backendPort || bridgeFailed) return 'unavailable'
  backendPort.postMessage(base64ToBytes(base64))
  return 'accepted'
}

window.__novalistBridgeFailed = () => {
  if (bridgeFailed) return
  bridgeFailed = true
  hostCalls.disconnect(new Error('The mobile connection stopped. Reopen Novalist before retrying changes.'))
  backendPort?.postMessage({ novalistControl: 'backend-recovery-failed', error: 'The mobile connection stopped. Reopen Novalist to recover your work.' })
}

function requestBackendPort(): void {
  const channel = new MessageChannel()
  backendPort?.close()
  const port = channel.port2
  backendPort = port
  const connection = ready ??= hostCall<void>('bridgeReady', [])
  backendPort.onmessage = (event) => {
    void connection.then(() => {
      if (backendPort !== port) return
      if (bridgeFailed) throw new Error('The mobile connection stopped.')
      sendRaw(bytesToBase64(event.data as Uint8Array))
    }).catch(() => window.__novalistBridgeFailed?.())
  }
  void ready.catch(() => window.__novalistBridgeFailed?.())
  backendPort.start()
  window.postMessage({ novalist: 'backend-port' }, '*', [channel.port1])
}


const hostCalls = new HostCallChannel(sendRaw)
const backgroundSaves = new Set<() => Promise<void>>()

window.__novalistLifecycleSave = (requestId: number) => {
  void (async () => {
    let success = false
    try {
      await Promise.all([...backgroundSaves].map((save) => save()))
      success = backgroundSaves.size > 0
    } finally {
      await hostCall('backgroundSaveCompleted', [requestId, success])
    }
  })().catch((error: unknown) => console.error('Mobile background save did not complete:', error instanceof Error ? error.name : 'Error'))
}

window.__novalistHostResult = (base64: string) => {
  const text = new TextDecoder().decode(base64ToBytes(base64))
  hostCalls.receive(JSON.parse(text))
  return 'accepted'
}

function hostCall<T>(method: string, args: unknown[]): Promise<T> {
  const timeout = method.startsWith('pick') || method === 'shareExport'
    ? 600_000 : method === 'readProjectAsset' ? 120_000 : 30_000
  return hostCalls.request<T>(method, args, timeout)
}

function notifyHost(method: string, args: unknown[]): void {
  void hostCall(method, args).catch(() => window.__novalistBridgeFailed?.())
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


const novalist: Window['novalist'] = {
  onBackgroundSave: (handler) => {
    backgroundSaves.add(handler)
    return () => { backgroundSaves.delete(handler) }
  },
  systemMicrophone: {
    start: () => hostCall<void>('microphoneStart', []),
    read: () => hostCall<{ clips: string[]; ended: boolean }>('microphoneRead', []),
    stop: () => hostCall<{ clips: string[]; ended: boolean }>('microphoneStop', [])
  },
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
  shareExport: (path) => hostCall<boolean>('shareExport', [path]),
  releaseExport: (path) => hostCall<void>('releaseExport', [path]),
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
    notifyHost('copyText', [text])
  },
  // Mobile-only: show/hide the native Liquid Glass tab bar (hidden on welcome).
  setNavVisible: (visible: boolean) => {
    notifyHost('setNavVisible', [visible])
  },
  // Mobile-only: push localized titles onto the native tab bar, in tab order
  // (dashboard, manuscript, codex, search, more). Re-pushed on language change.
  setTabTitles: (titles: string[]) => {
    notifyHost('setTabTitles', [titles])
  },
  // Mobile-only: move the native bar's highlight to this tab index, for a tab
  // change the web made rather than the writer tapping.
  setSelectedTab: (index: number) => {
    notifyHost('setSelectedTab', [index])
  },
  // Mobile-only: show/hide the native Liquid Glass Plan popover with the given
  // localized item labels; selection comes back via window.__novalistPlanSelect.
  setPlanningMenuOpen: (open: boolean, labels: string[]) => {
    notifyHost('setPlanningMenuOpen', [open, labels])
  },
  // Tablet-only: localized titles for the native iPad sidebar, in the order the
  // native SidebarItems table declares (see TABLET_DESTINATIONS).
  setSidebarTitles: (titles: string[]) => {
    notifyHost('setSidebarTitles', [titles])
  },
  // Tablet-only: move the sidebar highlight to a destination key.
  setSidebarSelection: (key: string) => {
    notifyHost('setSidebarSelection', [key])
  },
  // Tablet-only: collapse the sidebar to an icon-only rail, or expand it back.
  setSidebarCollapsed: (collapsed: boolean) => {
    notifyHost('setSidebarCollapsed', [collapsed])
  },
  // Ask the native side to re-push the current size class through
  // window.__novalistLayout; the first pass can run before the bundle loads.
  requestLayout: () => {
    notifyHost('requestLayout', [])
  },
  readClipboardImage: () => hostCall<string | null>('readClipboardImage', []),
  // Track the open project's folder natively so project images can be read, and
  // drop the resolved-image cache so a new project can't reuse the old one's.
  setProjectRoot: (root) => {
    clearProjectImageCache()
    notifyHost('setProjectRoot', [root])
  },
  // Read a project-relative image as a data: URI (novalist-project:// has no
  // scheme handler in the mobile WebView; projectImages rewrites those srcs).
  readProjectImage: (path: string) => hostCall<string | null>('readProjectImage', [path]),
  readProjectAsset: (path: string) => hostCall<string | null>('readProjectAsset', [path]),
  // Security-scoped external folders: resolve the native bookmark and start/stop
  // access around opening a project (mirrors the Mac App Store contract). A false
  // result makes the renderer re-prompt for the folder.
  beginProjectAccess: (path: string) => hostCall<boolean>('beginProjectAccess', [path]),
  resolveStoredProjectPath: (path: string) => hostCall<string | null>('resolveStoredProjectPath', [path]),
  endProjectAccess: (path: string) => {
    notifyHost('endProjectAccess', [path])
  },
  openPaneWindow: () => Promise.resolve(),
  registerExtensionRoots: () => Promise.resolve(),
  // Store-delivered updates: no self-update on mobile.
  checkAppUpdate: async () => null,
  hasDetachedPanes: async () => false,
  downloadAppUpdate: async () => ({ filePath: '', launchToken: null }),
  launchAppUpdate: () => Promise.resolve(),
  // No protocol handler on mobile: there is nothing to register a scheme with.
  takeDeepLink: () => Promise.resolve(null),
  onDeepLink: () => {
    // Mobile has no protocol handler to deliver desktop deep-link events.
  },
  updatesChecked: () => {
    // Store updates have no desktop update-check event to acknowledge.
  }
}

window.novalist = novalist

const healthCheck = setInterval(() => {
  if (!ready || bridgeFailed || document.visibilityState !== 'visible') return
  void hostCall('bridgeHealth', []).catch(() => window.__novalistBridgeFailed?.())
}, 15_000)
window.addEventListener('pagehide', () => {
  clearInterval(healthCheck)
  bridgeFailed = true
  backendPort?.close()
  hostCalls.disconnect(new Error('The mobile page closed.'))
})

// Rewrite novalist-project:// <img> srcs to data URIs (no custom-scheme handler
// in the mobile WebView).
installProjectImageLoader()
