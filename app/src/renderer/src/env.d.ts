/// <reference types="vite/client" />

interface Window {
  novalistStores: {
    project: typeof import('./stores/projectStore').useProjectStore
    shell: typeof import('./stores/shellStore').useShellStore
  }
  novalistRpc: import('./rpc/client').RpcClient
  novalist: {
    material: 'glass' | 'vibrancy' | 'opaque'
    platform: NodeJS.Platform
    requestBackendPort(): void
    pickFolder(title: string): Promise<string | null>
    setProjectRoot(root: string | null): void
  }
}
