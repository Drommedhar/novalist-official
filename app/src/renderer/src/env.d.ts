/// <reference types="vite/client" />

/** Bundled Markdown user manual: filename ("05-editor.md") -> raw content. */
declare module 'virtual:novalist-manual' {
  const pages: Record<string, string>
  export default pages
}

/** Manual images inlined as data URIs, keyed by filename ("editor.png"). */
declare module 'virtual:novalist-manual-images' {
  const images: Record<string, string>
  export default images
}

interface Window {
  novalistStores: {
    project: typeof import('./stores/projectStore').useProjectStore
    shell: typeof import('./stores/shellStore').useShellStore
    codex: typeof import('./stores/codexStore').useCodexStore
    wiki: typeof import('./stores/wikiStore').useWikiStore
    settings: typeof import('./stores/settingsStore').useSettingsStore
  }
  novalistRpc: import('./rpc/client').RpcClient
  novalistExtensionTheme: {
    themeTokens: () => Record<string, string>
    postThemeToFrame: (frame: Window | null | undefined) => void
    watchTheme: (send: () => void) => () => void
  }
  novalistPlugins?: {
    commands: () => readonly { extensionId: string; id: string; title: string }[]
    statusItems: () => readonly { extensionId: string; id: string; text: string }[]
    reload: () => Promise<void>
  }
  novalist: {
    material: 'glass' | 'vibrancy' | 'opaque'
    platform: NodeJS.Platform
    // True on the mobile (MAUI) build. Undefined on desktop. Gates capabilities
    // unavailable in the sandbox (e.g. Git/versioning UI).
    isMobile?: boolean
    // Mobile-only: show/hide the native Liquid Glass tab bar (project vs welcome).
    setNavVisible?(visible: boolean): void
    // Mobile-only: push localized titles onto the native tab bar (tab order).
    setTabTitles?(titles: string[]): void
    // Mobile-only: highlight the tab at this index. The native bar highlights
    // what was tapped, so anything that switches tab from the web side (the
    // first-run tour) has to say so, or the bar names one place while the
    // screen shows another.
    setSelectedTab?(index: number): void
    // Mobile-only: read a project-relative image as a data: URI (the mobile
    // WebView has no novalist-project:// scheme handler).
    readProjectImage?(path: string): Promise<string | null>
    // Mobile-only: show/hide the native Liquid Glass Plan popover with the given
    // localized labels (selection returns via window.__novalistPlanSelect).
    setPlanningMenuOpen?(open: boolean, labels: string[]): void
    isMas: boolean
    autoUpdate: boolean
    requestBackendPort(): void
    pickFolder(title: string): Promise<string | null>
    captureRegion(
      rect: { x: number; y: number; width: number; height: number },
      outputPath: string,
      scale: number
    ): Promise<boolean>
    saveFile(defaultName: string): Promise<string | null>
    applySpellCheck(enabled: boolean, languages: string[], words: string[]): Promise<string[]>
    spellCheckLanguages(): Promise<string[]>
    setSpellCheckMenuLabels(labels: { addToDictionary: string; noSuggestions: string }): void
    onSpellCheckWordAdded(handler: (word: string) => void): void
    /** The misspelling under the pointer, as the context menu opens. */
    /** A novalist:// link waiting from launch, or null. */
    takeDeepLink(): Promise<{ project: string; chapter?: string; scene?: string } | null>
    /** Links arriving while the app is already open. */
    onDeepLink(handler: (link: { project: string; chapter?: string; scene?: string }) => void): void
    onSpellingContext(handler: (word: string, suggestions: string[]) => void): void
    /** Applies a correction through Chromium, which owns the misspelled range. */
    replaceMisspelling(replacement: string): void
    pickFile(title: string, mode?: 'images' | 'all'): Promise<string | null>
    /** Absolute path of a dropped File (Electron removed File.path). */
    filePath(file: File): string
    openExternal(target: string): Promise<boolean>
    revealPath(target: string): Promise<boolean>
    copyText(text: string): void
    readClipboardImage(): Promise<string | null>
    setProjectRoot(root: string | null): void
    beginProjectAccess(path: string): Promise<boolean>
    endProjectAccess(path: string): void
    openPaneWindow(request: {
      view: string
      projectPath: string | null
      chapterGuid: string | null
      sceneId: string | null
    }): Promise<void>
    registerExtensionRoots(roots: Record<string, string>): Promise<void>
    checkAppUpdate(): Promise<AppUpdate | null>
    downloadAppUpdate(info: AppUpdate): Promise<string>
    updatesChecked(): void
    /** Repaints the system window controls to match the theme. Desktop only;
     *  a no-op on macOS and on the mobile build. */
    setTitleBarColors?(color: string, symbolColor: string): void
  }
}

/** App self-update info returned by the main-process GitHub check. */
interface AppUpdate {
  version: string
  tagName: string
  htmlUrl: string
  notes: string
  downloadUrl: string
  assetName: string
  assetSize: number
}
