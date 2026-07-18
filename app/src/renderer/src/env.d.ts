/// <reference types="vite/client" />

interface Window {
  novalist: {
    material: 'glass' | 'vibrancy' | 'opaque'
    platform: NodeJS.Platform
    requestBackendPort(): void
  }
}
