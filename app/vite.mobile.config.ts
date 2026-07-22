import { existsSync, readdirSync, readFileSync } from 'node:fs'
import { extname, resolve } from 'node:path'
import { defineConfig } from 'vite'
import type { Plugin } from 'vite'
import react from '@vitejs/plugin-react'

// Plain-web build of the renderer for the .NET MAUI HybridWebView shell
// (Novalist.Mobile). Parallel to electron.vite.config.ts; produces a static
// bundle (no Electron assumptions) that the HybridWebView loads. Output goes
// straight into the MAUI project's Raw assets (git-ignored, regenerated).

const MANUAL_VIRTUAL_ID = 'virtual:novalist-manual'
const MANUAL_IMAGES_VIRTUAL_ID = 'virtual:novalist-manual-images'
const MANUAL_DIR = resolve(__dirname, '../docs/manual')
const MANUAL_IMAGES_DIR = resolve(MANUAL_DIR, 'images')

const IMAGE_MIME: Record<string, string> = {
  '.png': 'image/png',
  '.jpg': 'image/jpeg',
  '.jpeg': 'image/jpeg',
  '.gif': 'image/gif',
  '.webp': 'image/webp',
  '.svg': 'image/svg+xml'
}

// Duplicated from electron.vite.config.ts so the desktop build stays untouched.
// Bundles docs/manual/*.md and its images/ (as data URIs) into virtual modules.
function manualPlugin(): Plugin {
  const resolvedManual = '\0' + MANUAL_VIRTUAL_ID
  const resolvedImages = '\0' + MANUAL_IMAGES_VIRTUAL_ID
  return {
    name: 'novalist-manual',
    resolveId(id) {
      if (id === MANUAL_VIRTUAL_ID) return resolvedManual
      if (id === MANUAL_IMAGES_VIRTUAL_ID) return resolvedImages
    },
    load(id) {
      if (id === resolvedManual) {
        const files = readdirSync(MANUAL_DIR)
          .filter((f) => f.endsWith('.md'))
          .sort()
        const entries = files.map(
          (file) =>
            `${JSON.stringify(file)}: ${JSON.stringify(readFileSync(resolve(MANUAL_DIR, file), 'utf8'))}`
        )
        return `export default {\n${entries.join(',\n')}\n}`
      }
      if (id === resolvedImages) {
        if (!existsSync(MANUAL_IMAGES_DIR)) return 'export default {}'
        const files = readdirSync(MANUAL_IMAGES_DIR)
          .filter((f) => IMAGE_MIME[extname(f).toLowerCase()])
          .sort()
        const entries = files.map((file) => {
          const b64 = readFileSync(resolve(MANUAL_IMAGES_DIR, file)).toString('base64')
          const uri = `data:${IMAGE_MIME[extname(file).toLowerCase()]};base64,${b64}`
          return `${JSON.stringify(file)}: ${JSON.stringify(uri)}`
        })
        return `export default {\n${entries.join(',\n')}\n}`
      }
    }
  }
}

export default defineConfig({
  root: resolve(__dirname, 'src/renderer'),
  // Relative asset URLs so the bundle resolves under the HybridWebView root
  // regardless of the platform's virtual origin.
  base: './',
  plugins: [react(), manualPlugin()],
  build: {
    outDir: resolve(__dirname, '../Novalist.Mobile/Resources/Raw/app'),
    emptyOutDir: true,
    rollupOptions: {
      input: resolve(__dirname, 'src/renderer/index.mobile.html')
    }
  }
})
