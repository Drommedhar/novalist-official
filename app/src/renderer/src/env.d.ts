/// <reference types="vite/client" />

/** Bundled Markdown user manual: filename ("05-editor.md") -> raw content. */
declare module 'virtual:novalist-manual' {
  const pages: Record<string, string>
  export default pages
}

/** The repo's CHANGELOG.md, bundled raw so About can render "What's new". */
declare module 'virtual:novalist-changelog' {
  const changelog: string
  export default changelog
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
    extensions: typeof import('./stores/extensionsStore').useExtensionsStore
    onboarding: typeof import('./stores/onboardingStore').useOnboardingStore
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
    // Test-only: stands in for the iPad horizontal size class, which only the
    // native shell can report. Set from NOVALIST_FORCE_TABLET so the e2e run can
    // render the real TabletShell; never true in a shipped build.
    isTablet?: boolean
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
    // Tablet-only (iPad, regular width): push localized titles onto the native
    // Liquid Glass sidebar, in TABLET_DESTINATIONS order.
    setSidebarTitles?(titles: string[]): void
    // Tablet-only: keep the sidebar highlight on the destination actually shown
    // (the web can switch views without a sidebar tap, e.g. opening a scene).
    setSidebarSelection?(key: string): void
    // Tablet-only: collapse the native sidebar to an icon-only rail, or expand it.
    setSidebarCollapsed?(collapsed: boolean): void
    // Mobile-only: ask the native side to re-announce the current layout through
    // window.__novalistLayout (the first size-class pass can precede page load).
    requestLayout?(): void
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
    /** Replaces the application menu with the one the command registry
     *  describes. Desktop only; the mobile build has no menu bar. */
    setMenu?(
      nodes: import('./shell/menuLayout').MenuNode[],
      labels: import('./shell/menuLayout').MenuLabels
    ): void
    /** Whole-interface scale, independent from the manuscript font size. */
    setUiScale?(factor: number): Promise<number>
    /** The installed application version. Desktop only; the mobile shell has
     *  no main process to ask. */
    appVersion?(): Promise<string>
    /** Content-free display facts for Settings -> Diagnostics. */
    displayDiagnostics?(): Promise<{
      zoomFactor: number
      scaleFactor: number
      windowBounds: { x: number; y: number; width: number; height: number }
      contentBounds: { x: number; y: number; width: number; height: number }
      workArea: { x: number; y: number; width: number; height: number }
    } | null>
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
