/// <reference types="vite/client" />

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
    requestBackendPort(): void
    pickFolder(title: string): Promise<string | null>
    saveFile(defaultName: string): Promise<string | null>
    pickFile(title: string, mode?: 'images' | 'all'): Promise<string | null>
    openExternal(target: string): Promise<boolean>
    revealPath(target: string): Promise<boolean>
    copyText(text: string): void
    setProjectRoot(root: string | null): void
    registerExtensionRoots(roots: Record<string, string>): void
  }
}
