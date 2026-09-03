import { contextBridge, ipcRenderer, webUtils } from 'electron'

const material =
  process.argv.find((a) => a.startsWith('--nl-material='))?.split('=')[1] ?? 'opaque'

// True only in the Mac App Store (sandboxed) build. Apple forbids self-updating
// there (the store delivers updates), so the download-and-run-installer flow is
// disabled for MAS - and so is the extension feature, which the App Store does
// not allow because an extension is code that arrives after review and adds
// features.
//
// NOVALIST_FORCE_MAS stands in for the real thing so the e2e run can render the
// App Store build's UI without an App Store build, the same way
// NOVALIST_FORCE_MOBILE stands in for the phone shell. Never set in a shipped
// build.
const isMas =
  (process as NodeJS.Process & { mas?: boolean }).mas === true ||
  process.env.NOVALIST_FORCE_MAS === '1'

/**
 * Minimal privileged surface. The backend MessagePort cannot cross the context
 * bridge directly, so it is forwarded to the page via window.postMessage and
 * picked up by the RPC client from the message event's ports array.
 */
contextBridge.exposeInMainWorld('novalist', {
  material,
  platform: process.platform,
  // True only in the Mac App Store build. The renderer uses this to hide
  // self-update UI (the store delivers updates there) and to replace the
  // Extensions view and its Settings card with an explanation.
  isMas,
  // The phone shell (single pane, native tab bar) is what the iOS build gets
  // from its own bridge; here it is off, except when NOVALIST_FORCE_MOBILE asks
  // for it. That exists so the mobile layout can be rendered and measured
  // without a simulator - m51 checks the real MobileShell tree rather than an
  // approximation of it, and a developer can eyeball the phone layout by
  // setting the variable. It is never set in a shipped build.
  isMobile: process.env.NOVALIST_FORCE_MOBILE === '1',
  // On iOS the native side owns the horizontal size class and announces it; here
  // there is no size class, so NOVALIST_FORCE_TABLET stands in for one. Without
  // it the layout stays 'phone' and TabletShell can never render outside a
  // simulator - which is how the iPad two-pane layout went a release cycle with
  // its e2e "tablet" case silently exercising the phone shell at iPad width.
  // A flag rather than a callback: contextIsolation gives the preload its own
  // window, so it cannot reach the renderer's __novalistLayout. MobileShell feeds
  // this through that callback instead, keeping one application point for both
  // hosts. Never set in a shipped build.
  isTablet: process.env.NOVALIST_FORCE_TABLET === '1',
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
  pickFile(
    title: string,
    mode?: 'images' | 'all' | 'manuscript',
    options?: {
      extensions?: string[]
      filterName?: string
      scrivenerAccessTitle?: string
    }
  ): Promise<string | null> {
    return ipcRenderer.invoke('novalist:pick-file', title, mode, options)
  },
  /** Releases a temporary, sandbox-readable copy made for manuscript import.
   *  It is a no-op for ordinary desktop file selections. */
  releasePickedFile(path: string): Promise<void> {
    return ipcRenderer.invoke('novalist:release-picked-file', path)
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
  /** Opens a second window showing one view, on the project the asking window
   *  has open and - for the editor - the scene it is showing. */
  openPaneWindow(request: {
    view: string
    projectPath: string | null
    chapterGuid: string | null
    sceneId: string | null
  }): Promise<void> {
    return ipcRenderer.invoke('novalist:open-pane-window', request)
  },

  registerExtensionRoots(roots: Record<string, string>): Promise<void> {
    // Awaitable rather than fired and forgotten: a plugin module URL resolves
    // against these, and an import that overtook the message got a 404.
    return ipcRenderer.invoke('novalist:register-ext-roots', roots)
  },
  // App self-update. Download and launch are separate so the renderer can
  // finish its final saves and close backup before any installer/helper runs.
  checkAppUpdate(): Promise<unknown> {
    return ipcRenderer.invoke('novalist:check-app-update')
  },
  hasDetachedPanes(): Promise<boolean> {
    return ipcRenderer.invoke('novalist:has-detached-panes')
  },
  downloadAppUpdate(info: unknown): Promise<{ filePath: string; launchToken: string | null }> {
    return ipcRenderer.invoke('novalist:download-app-update', info)
  },
  launchAppUpdate(token: string): Promise<void> {
    return ipcRenderer.invoke('novalist:launch-app-update', token)
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
  },
  /** Replaces the application menu with the one the command registry describes. */
  setMenu(nodes: unknown[], labels: unknown): void {
    ipcRenderer.send('novalist:set-menu', nodes, labels)
  },
  /** Applies the persisted whole-interface scale to this renderer window. */
  setUiScale(factor: number): Promise<number> {
    return ipcRenderer.invoke('novalist:set-ui-scale', factor)
  },
  /** The installed application version, for About and support reports. */
  appVersion(): Promise<string> {
    return ipcRenderer.invoke('novalist:app-version')
  },
  /** Content-free display facts used by diagnostics and display troubleshooting. */
  displayDiagnostics(): Promise<{
    zoomFactor: number
    scaleFactor: number
    windowBounds: Electron.Rectangle
    contentBounds: Electron.Rectangle
    workArea: Electron.Rectangle
  } | null> {
    return ipcRenderer.invoke('novalist:display-diagnostics')
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
