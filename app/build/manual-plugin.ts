import { existsSync, readdirSync, readFileSync } from 'node:fs'
import { extname, resolve } from 'node:path'
import type { Plugin } from 'vite'

const MANUAL_VIRTUAL_ID = 'virtual:novalist-manual'
const MANUAL_IMAGES_VIRTUAL_ID = 'virtual:novalist-manual-images'
const CHANGELOG_VIRTUAL_ID = 'virtual:novalist-changelog'

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
 * virtual module imports the manual images as emitted assets, so screenshots
 * load only when the reader opens a page that uses them.
 *
 * A third bundles the repo's `CHANGELOG.md` the same way, so About can show
 * what changed in the build the reader is running rather than sending them to
 * a web page to find out.
 */
export function manualPlugin(repositoryRoot: string): Plugin {
  const MANUAL_DIR = resolve(repositoryRoot, 'docs/manual')
  const MANUAL_IMAGES_DIR = resolve(MANUAL_DIR, 'images')
  const CHANGELOG_FILE = resolve(repositoryRoot, 'CHANGELOG.md')
  const resolvedManual = '\0' + MANUAL_VIRTUAL_ID
  const resolvedImages = '\0' + MANUAL_IMAGES_VIRTUAL_ID
  const resolvedChangelog = '\0' + CHANGELOG_VIRTUAL_ID
  return {
    name: 'novalist-manual',
    resolveId(id) {
      if (id === MANUAL_VIRTUAL_ID) return resolvedManual
      if (id === MANUAL_IMAGES_VIRTUAL_ID) return resolvedImages
      if (id === CHANGELOG_VIRTUAL_ID) return resolvedChangelog
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
        const imports = files.map((file, index) =>
          `import image${index} from ${JSON.stringify(resolve(MANUAL_IMAGES_DIR, file).replace(/\\/g, '/') + '?url&no-inline')}`)
        const entries = files.map((file, index) => `${JSON.stringify(file)}: image${index}`)
        return `${imports.join('\n')}\nexport default {\n${entries.join(',\n')}\n}`
      }
      if (id === resolvedChangelog) {
        const text = existsSync(CHANGELOG_FILE) ? readFileSync(CHANGELOG_FILE, 'utf8') : ''
        return `export default ${JSON.stringify(text)}`
      }
    }
  }
}
