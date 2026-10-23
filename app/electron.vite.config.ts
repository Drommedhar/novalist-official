import { resolve } from 'node:path'
import { defineConfig } from 'electron-vite'
import react from '@vitejs/plugin-react'
import { manualPlugin } from './build/manual-plugin'

export default defineConfig(({ mode }) => {
  const define = { __NOVALIST_STEAM_BUILD__: mode === 'steam' }
  return {
    main: {
      define,
      build: {
        rollupOptions: {
          input: {
            index: resolve(__dirname, 'src/main/index.ts'),
            collaborationHost: resolve(__dirname, 'src/main/collaboration/documentWorker.ts')
          },
          // Darwin-only optional native module (Liquid Glass). Never bundle it —
          // glass.ts imports it lazily at runtime and no-ops when it is absent
          // (Windows/Linux), so the build must not try to resolve it there.
          external: ['electron-liquid-glass']
        }
      }
    },
    preload: { define },
    renderer: {
      plugins: [react(), manualPlugin(resolve(__dirname, '..'))]
    }
  }
})
