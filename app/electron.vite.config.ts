import { readdirSync, readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { defineConfig } from 'electron-vite'
import type { Plugin } from 'vite'
import react from '@vitejs/plugin-react'

const MANUAL_VIRTUAL_ID = 'virtual:novalist-manual'
const MANUAL_DIR = resolve(__dirname, '../docs/manual')

/**
 * Bundles the Markdown user manual (repo `docs/manual/*.md`, which lives
 * outside the renderer root) into the renderer as a virtual module. The map
 * is derived from whatever `.md` files exist at build time — no fixed page
 * list — so pages added by other work are picked up automatically.
 */
function manualPlugin(): Plugin {
  const resolvedId = '\0' + MANUAL_VIRTUAL_ID
  return {
    name: 'novalist-manual',
    resolveId(id) {
      if (id === MANUAL_VIRTUAL_ID) return resolvedId
    },
    load(id) {
      if (id !== resolvedId) return
      const files = readdirSync(MANUAL_DIR)
        .filter((f) => f.endsWith('.md'))
        .sort()
      const entries = files.map(
        (file) =>
          `${JSON.stringify(file)}: ${JSON.stringify(readFileSync(resolve(MANUAL_DIR, file), 'utf8'))}`
      )
      return `export default {\n${entries.join(',\n')}\n}`
    }
  }
}

export default defineConfig({
  main: {},
  preload: {},
  renderer: {
    plugins: [react(), manualPlugin()]
  }
})
