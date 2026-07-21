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
  }
  novalistRpc: import('./rpc/client').RpcClient
  novalist: {
    material: 'glass' | 'vibrancy' | 'opaque'
    platform: NodeJS.Platform
    // True on the mobile (MAUI) build. Undefined on desktop. Gates capabilities
    // unavailable in the sandbox (e.g. Git/versioning UI).
    isMobile?: boolean
    isMas: boolean
    autoUpdate: boolean
    requestBackendPort(): void
    pickFolder(title: string): Promise<string | null>
    saveFile(defaultName: string): Promise<string | null>
    pickFile(title: string, mode?: 'images' | 'all'): Promise<string | null>
    openExternal(target: string): Promise<boolean>
    revealPath(target: string): Promise<boolean>
    copyText(text: string): void
    readClipboardImage(): Promise<string | null>
    setProjectRoot(root: string | null): void
    beginProjectAccess(path: string): Promise<boolean>
    endProjectAccess(path: string): void
    registerExtensionRoots(roots: Record<string, string>): void
    checkAppUpdate(): Promise<AppUpdate | null>
    downloadAppUpdate(info: AppUpdate): Promise<string>
    updatesChecked(): void
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
