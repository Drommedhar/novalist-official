const pending = new Set<AbortController>()

export function registerBackgroundRead(controller: AbortController): () => void {
  if (window.novalist.isMobile) pending.add(controller)
  return () => { pending.delete(controller) }
}

export function cancelBackgroundReads(): void {
  for (const controller of pending) controller.abort()
  pending.clear()
}
