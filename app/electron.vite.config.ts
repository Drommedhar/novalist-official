import { existsSync, readdirSync, readFileSync } from 'node:fs'
import { extname, resolve } from 'node:path'
import { defineConfig } from 'electron-vite'
import type { Plugin } from 'vite'
import react from '@vitejs/plugin-react'

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

/**
 * Bundles the Markdown user manual (repo `docs/manual/*.md`, which lives
 * outside the renderer root) into the renderer as a virtual module. The map
 * is derived from whatever `.md` files exist at build time — no fixed page
 * list — so pages added by other work are picked up automatically. A second
 * virtual module inlines the manual's `images/` as data URIs (keyed by
 * filename) so the in-app viewer can render them without a real asset origin.
 */
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
    plugins: [react(), manualPlugin()]
  }
})
