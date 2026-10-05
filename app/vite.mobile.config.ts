import { resolve } from 'node:path'
import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import { manualPlugin } from './build/manual-plugin'

// Plain-web build of the renderer for the .NET MAUI HybridWebView shell
// (Novalist.Mobile). Parallel to electron.vite.config.ts; produces a static
// bundle (no Electron assumptions) that the HybridWebView loads. Output goes
// straight into the MAUI project's Raw assets (git-ignored, regenerated).

export default defineConfig({
  root: resolve(__dirname, 'src/renderer'),
  // Relative asset URLs so the bundle resolves under the HybridWebView root
  // regardless of the platform's virtual origin.
  base: './',
  plugins: [react(), manualPlugin(resolve(__dirname, '..'))],
  build: {
    outDir: resolve(__dirname, '../Novalist.Mobile/Resources/Raw/app'),
    emptyOutDir: true,
    rollupOptions: {
      input: resolve(__dirname, 'src/renderer/index.mobile.html')
    }
  }
})
