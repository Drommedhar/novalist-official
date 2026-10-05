import { copyFileSync } from 'node:fs'

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
