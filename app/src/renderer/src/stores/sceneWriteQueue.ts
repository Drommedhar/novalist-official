const tails = new Map<string, Promise<void>>()

export function enqueueSceneWrite<T>(sceneId: string, write: () => Promise<T>): Promise<T> {
  const next = (tails.get(sceneId) ?? Promise.resolve()).then(write)
  const tail = next.then(() => {}, () => {})
  tails.set(sceneId, tail)
  void tail.then(() => { if (tails.get(sceneId) === tail) tails.delete(sceneId) })
  return next
}
