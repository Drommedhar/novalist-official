/**
 * Mobile project-image loader.
 *
 * Project images (covers, banners, entity images, peek cards, context panel) are
 * referenced as `novalist-project://nl/<relative path>`. On desktop Electron
 * registers that scheme and serves the file; the mobile WebView has no such
 * handler, so those <img> elements would all fail to load.
 *
 * Instead of changing every component, we watch the DOM for such <img> srcs and
 * rewrite them to `data:` URIs fetched over the host bridge (readProjectImage,
 * which reads the file under the open project root). Resolved URIs are cached by
 * relative path; the cache is cleared when the project root changes (see the
 * shim's setProjectRoot) so a new project can't show the previous one's images.
 */

const PREFIX = 'novalist-project://'
const cache = new Map<string, string>()

export function clearProjectImageCache(): void {
  cache.clear()
}

function relativePathOf(src: string): string | null {
  try {
    // novalist-project://nl/<path> -> "<path>" (host "nl" is ignored, matching
    // the desktop handler which joins projectRoot + url.pathname).
    return decodeURIComponent(new URL(src).pathname).replace(/^\/+/, '')
  } catch {
    return null
  }
}

async function resolveImage(img: HTMLImageElement): Promise<void> {
  const src = img.getAttribute('src')
  if (!src || !src.startsWith(PREFIX)) return
  const path = relativePathOf(src)
  if (!path) return

  let uri = cache.get(path)
  if (!uri) {
    const read = window.novalist.readProjectImage
    if (!read) return
    const result = await read(path)
    if (!result) return
    uri = result
    cache.set(path, uri)
  }
  // Only apply if the element still points at the scheme URL (it may have been
  // re-rendered or replaced in the meantime).
  if (img.getAttribute('src')?.startsWith(PREFIX)) img.src = uri
}

function scan(root: ParentNode): void {
  root.querySelectorAll<HTMLImageElement>(`img[src^="${PREFIX}"]`).forEach((img) => {
    void resolveImage(img)
  })
}

export function installProjectImageLoader(): void {
  const observer = new MutationObserver((mutations) => {
    for (const m of mutations) {
      if (m.type === 'attributes' && m.target instanceof HTMLImageElement) {
        void resolveImage(m.target)
      } else {
        m.addedNodes.forEach((node) => {
          if (node instanceof HTMLImageElement) void resolveImage(node)
          else if (node instanceof Element) scan(node)
        })
      }
    }
  })
  const start = (): void => {
    scan(document)
    observer.observe(document.documentElement, {
      childList: true,
      subtree: true,
      attributes: true,
      attributeFilter: ['src']
    })
  }
  if (document.body) start()
  else document.addEventListener('DOMContentLoaded', start, { once: true })
}
