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
  /** Captures a region of the window to a PNG. Used by the map image export,
   * where the surface is a DOM tree rather than a single canvas. */
  captureRegion(
    rect: { x: number; y: number; width: number; height: number },
    outputPath: string,
    scale: number
  ): Promise<boolean> {
    return ipcRenderer.invoke('novalist:capture-region', rect, outputPath, scale)
  },
  saveFile(defaultName: string): Promise<string | null> {
    return ipcRenderer.invoke('novalist:save-file', defaultName)
  },
  /** Applies the writer's spell-check settings to the Chromium session and
   *  reports back which language tags this build can actually load. */
  applySpellCheck(
    enabled: boolean,
    languages: string[],
    words: string[]
  ): Promise<string[]> {
    return ipcRenderer.invoke('novalist:apply-spellcheck', enabled, languages, words)
  },
  spellCheckLanguages(): Promise<string[]> {
    return ipcRenderer.invoke('novalist:spellcheck-languages')
  },
  /** Localized labels for the spelling context menu, which the main process
   *  builds natively and so cannot translate itself. */
  setSpellCheckMenuLabels(labels: { addToDictionary: string; noSuggestions: string }): void {
    ipcRenderer.send('novalist:spellcheck-menu-labels', labels)
  },
  /** Fires when the writer adds a word from the native spelling menu, so the
   *  renderer can persist it alongside the rest of their settings. */
  onSpellCheckWordAdded(handler: (word: string) => void): void {
    ipcRenderer.on('novalist:spellcheck-word-added', (_event, word: string) => handler(word))
  },
  /** The misspelling under the pointer, reported as the context menu opens. */
  /**
   * A novalist:// link, if one is waiting.
   *
   * Pulled rather than pushed for the cold start: a link is usually what
   * launches the app, so it arrives long before the renderer is listening.
   */
  takeDeepLink(): Promise<{ project: string; chapter?: string; scene?: string } | null> {
    return ipcRenderer.invoke('novalist:take-deep-link')
  },

  /** Links that arrive while the app is already open. */
  onDeepLink(handler: (link: { project: string; chapter?: string; scene?: string }) => void): void {
    ipcRenderer.removeAllListeners('novalist:deep-link')
    ipcRenderer.on('novalist:deep-link', (_event, link) => handler(link))
  },

  onSpellingContext(handler: (word: string, suggestions: string[]) => void): void {
    // Replaces rather than adds. Every editor pane registers on mount, and a
    // second registration meant the menu was told about the same misspelling
    // twice - which is how the suggestions came out doubled.
    ipcRenderer.removeAllListeners('novalist:spelling-context')
    ipcRenderer.on(
      'novalist:spelling-context',
      (_event, payload: { word: string; suggestions: string[] }) =>
        handler(payload.word, payload.suggestions)
    )
  },
  /** Applies a correction through Chromium, which owns the misspelled range. */
  replaceMisspelling(replacement: string): void {
    ipcRenderer.send('novalist:replace-misspelling', replacement)
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
  registerExtensionRoots(roots: Record<string, string>): Promise<void> {
    // Awaitable rather than fired and forgotten: a plugin module URL resolves
    // against these, and an import that overtook the message got a 404.
    return ipcRenderer.invoke('novalist:register-ext-roots', roots)
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
  },
  /**
   * Repaints the system-drawn window controls to match the active theme. Only
   * does anything where the title bar is hidden behind an overlay (Windows and
   * Linux); a no-op on macOS, which draws its own traffic lights.
   */
  setTitleBarColors(color: string, symbolColor: string): void {
    ipcRenderer.send('novalist:set-titlebar-colors', color, symbolColor)
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
