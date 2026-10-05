import { resolve } from 'node:path'
import { defineConfig } from 'electron-vite'
import react from '@vitejs/plugin-react'
import { manualPlugin } from './build/manual-plugin'

export default defineConfig({
  main: {
    build: {
      rollupOptions: {
        // Darwin-only optional native module (Liquid Glass). Never bundle it —
        // glass.ts imports it lazily at runtime and no-ops when it is absent
        // (Windows/Linux), so the build must not try to resolve it there.
        external: ['electron-liquid-glass']
      }
    }
  },
  preload: {},
  renderer: {
    plugins: [react(), manualPlugin(resolve(__dirname, '..'))]
  }
})
