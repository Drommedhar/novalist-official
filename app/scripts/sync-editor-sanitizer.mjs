import { copyFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { buildSync } from 'esbuild'

// The editor frames also run directly from file:// outside Vite. Keep their
// local sanitizer and license synchronized with the exact locked dependency.
copyFileSync(
  new URL('../node_modules/dompurify/dist/purify.min.js', import.meta.url),
  new URL('../src/renderer/public/editor/purify.min.js', import.meta.url)
)
copyFileSync(
  new URL('../node_modules/dompurify/LICENSE', import.meta.url),
  new URL('../src/renderer/public/licenses/dompurify-LICENSE.txt', import.meta.url)
)
buildSync({
  entryPoints: [fileURLToPath(new URL('../src/shared/sceneSanitizer.ts', import.meta.url))],
  outfile: fileURLToPath(new URL('../src/renderer/public/editor/scene-sanitizer.js', import.meta.url)),
  bundle: true, format: 'iife', globalName: 'NovalistSceneSanitizer'
})
