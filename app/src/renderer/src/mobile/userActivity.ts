const EVENTS = ['pointerdown', 'keydown', 'input', 'wheel', 'touchmove']

export function listenForUserActivity(activity: () => void): () => void {
  const documents = new Set<Document>()
  const listen = (target: EventTarget): void => {
    for (const event of EVENTS) target.addEventListener(event, activity, { capture: true })
  }
  const unlisten = (target: EventTarget): void => {
    for (const event of EVENTS) target.removeEventListener(event, activity, { capture: true })
  }
  const syncFrames = (): void => {
    const current = new Set<Document>()
    for (const frame of document.querySelectorAll('iframe')) {
      if (frame.contentDocument) current.add(frame.contentDocument)
    }
    for (const doc of documents) {
      if (!current.has(doc)) { unlisten(doc); documents.delete(doc) }
    }
    for (const doc of current) {
      if (!documents.has(doc)) { listen(doc); documents.add(doc) }
    }
  }
  listen(window)
  syncFrames()
  const observer = new MutationObserver(syncFrames)
  observer.observe(document.documentElement, { childList: true, subtree: true })
  document.addEventListener('load', syncFrames, { capture: true })
  return () => {
    observer.disconnect()
    document.removeEventListener('load', syncFrames, { capture: true })
    unlisten(window)
    for (const doc of documents) unlisten(doc)
    documents.clear()
  }
}
