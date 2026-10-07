import i18n from '../i18n'

/** Resolve project assets in the mobile host and its same-origin editor/map frames. */
const PREFIX = 'novalist-project://'
const MAX_CACHE_BYTES = 32 * 1024 * 1024
const cache = new Map<string, { uri: string; bytes: number }>()
const pending = new Map<string, Promise<string | null>>()
let cacheBytes = 0
const documents = new Map<Document, MutationObserver>()
const frames = new WeakSet<HTMLIFrameElement>()
let generation = 0

export function clearProjectImageCache(): void {
  generation++
  cache.clear()
  pending.clear()
  cacheBytes = 0
  for (const [doc, observer] of documents) {
    if (doc !== document && !doc.defaultView?.frameElement?.isConnected) {
      observer.disconnect()
      documents.delete(doc)
    }
  }
}

function relativePathOf(src: string): string | null {
  try {
    return decodeURIComponent(new URL(src).pathname).replace(/^\/+/, '')
  } catch {
    return null
  }
}

export async function resolveProjectAssetUrl(src: string): Promise<string | null> {
  if (!window.novalist.isMobile || !src.startsWith(PREFIX)) return src
  const path = relativePathOf(src)
  const read = window.novalist.readProjectAsset ?? window.novalist.readProjectImage
  if (!path || !read) return null
  const requestedGeneration = generation
  const cached = cache.get(path)
  if (cached) {
    cache.delete(path)
    cache.set(path, cached)
    return cached.uri
  }
  let request = pending.get(path)
  if (!request) {
    request = read(path).then((uri) => {
      if (uri && requestedGeneration === generation) {
        const bytes = uri.length * 2
        if (bytes <= MAX_CACHE_BYTES) {
          while (cacheBytes + bytes > MAX_CACHE_BYTES) {
            const oldest = cache.keys().next().value
            if (oldest === undefined) break
            cacheBytes -= cache.get(oldest)!.bytes
            cache.delete(oldest)
          }
          cache.set(path, { uri, bytes })
          cacheBytes += bytes
        }
      }
      return uri
    })
    pending.set(path, request)
  }
  try {
    const uri = await request
    return requestedGeneration === generation ? uri : null
  } finally {
    if (pending.get(path) === request) pending.delete(path)
  }
}

async function resolveAsset(element: Element): Promise<void> {
  const attribute = element.tagName === 'OBJECT' ? 'data' : 'src'
  const src = element.getAttribute(attribute)
  if (!src?.startsWith(PREFIX)) return
  const requestedGeneration = generation
  const stillCurrent = (): boolean => requestedGeneration === generation && element.getAttribute(attribute) === src && element.isConnected
  try {
    const uri = await resolveProjectAssetUrl(src)
    if (!stillCurrent()) return
    if (!uri) throw new Error(i18n.t('research.previewFailed'))
    element.setAttribute(attribute, uri)
    if (element.tagName === 'SOURCE') (element.parentElement as HTMLMediaElement | null)?.load()
  } catch (error) {
    if (!stillCurrent()) return
    const message = error instanceof Error ? error.message : String(error)
    element.setAttribute('title', message)
    element.setAttribute('aria-description', message)
    element.dispatchEvent(new CustomEvent('novalist-asset-error', { bubbles: true, detail: { message } }))
    element.dispatchEvent(new Event('error'))
  }
}

function watchFrame(frame: HTMLIFrameElement): void {
  if (frames.has(frame)) return
  frames.add(frame)
  const loaded = (): void => {
    try {
      if (frame.contentDocument) watchDocument(frame.contentDocument)
    } catch {
      // Remote documents deliberately remain outside the host's asset access.
    }
  }
  frame.addEventListener('load', loaded)
  loaded()
}

function scan(root: ParentNode): void {
  const element = root.nodeType === Node.ELEMENT_NODE ? root as Element : null
  if (element?.matches('img, audio, video, source, iframe, object')) void resolveAsset(element)
  root.querySelectorAll('img, audio, video, source, iframe, object').forEach((element) => void resolveAsset(element))
  if (element?.tagName === 'IFRAME') watchFrame(element as HTMLIFrameElement)
  root.querySelectorAll<HTMLIFrameElement>('iframe').forEach(watchFrame)
}

function watchDocument(doc: Document): void {
  if (documents.has(doc) || !doc.documentElement) return
  const observer = new MutationObserver((mutations) => {
    for (const mutation of mutations) {
      if (mutation.type === 'attributes') void resolveAsset(mutation.target as Element)
      else mutation.addedNodes.forEach((node) => {
        if (node.nodeType === Node.ELEMENT_NODE) scan(node as Element)
      })
    }
  })
  documents.set(doc, observer)
  observer.observe(doc.documentElement, { childList: true, subtree: true, attributes: true, attributeFilter: ['src', 'data'] })
  scan(doc)
}

export function installProjectImageLoader(): void {
  ;(window as Window & { novalistResolveProjectAsset?: typeof resolveProjectAssetUrl }).novalistResolveProjectAsset = resolveProjectAssetUrl
  if (document.body) watchDocument(document)
  else document.addEventListener('DOMContentLoaded', () => watchDocument(document), { once: true })
}
