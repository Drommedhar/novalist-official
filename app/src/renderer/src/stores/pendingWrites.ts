/**
 * Mounted editing surfaces register the work they still hold outside their
 * persistent stores. The updater drains this list before handing control to
 * an installer, so a fast cached update cannot outrun an editor debounce.
 */
export type PendingWriteFlusher = () => void | Promise<void>
export type PendingWriteKey = string

const flushers = new Set<PendingWriteFlusher>()
type RetainedWrite = {
  key: PendingWriteKey
  version: number
  flush: PendingWriteFlusher
  inFlight: Promise<void> | null
}
const retainedWrites = new Map<PendingWriteKey, RetainedWrite>()
const writeVersions = new Map<PendingWriteKey, number>()
/**
 * Same-resource writes must finish in the order they were authored. Replacing a
 * retryable payload prevents an old failure from running again, while this tail
 * prevents an already-started old request from completing after its successor.
 */
const writeTails = new Map<PendingWriteKey, Promise<void>>()

export function registerPendingWrite(flusher: PendingWriteFlusher): () => void {
  flushers.add(flusher)
  return () => flushers.delete(flusher)
}

async function runRetainedWrite(write: RetainedWrite): Promise<void> {
  if (write.inFlight) return write.inFlight
  const predecessor = writeTails.get(write.key) ?? Promise.resolve()
  const inFlight = predecessor.then(async () => {
    try {
      await write.flush()
      if (retainedWrites.get(write.key)?.version === write.version) {
        retainedWrites.delete(write.key)
      }
    } catch (error) {
      // A newer generation owns this key now. Its write is queued behind this
      // one, so the stale failure must neither return to the retry set nor stop
      // the newer payload from becoming authoritative.
      if (retainedWrites.get(write.key)?.version !== write.version) return
      throw error
    }
  })
  write.inFlight = inFlight
  const tail = inFlight.catch(() => {})
  writeTails.set(write.key, tail)
  void tail.then(() => {
    if (writeTails.get(write.key) !== tail) return
    writeTails.delete(write.key)
    if (!retainedWrites.has(write.key)) writeVersions.delete(write.key)
  })
  try {
    await inFlight
  } finally {
    if (write.inFlight === inFlight) write.inFlight = null
  }
}

function replaceRetainedWrite(
  key: PendingWriteKey,
  flusher: PendingWriteFlusher
): RetainedWrite {
  if (!key) throw new Error('A pending write needs a stable resource key.')
  const version = (writeVersions.get(key) ?? 0) + 1
  writeVersions.set(key, version)
  const write: RetainedWrite = { key, version, flush: flusher, inFlight: null }
  retainedWrites.set(key, write)
  return write
}

/**
 * Starts a write owned by a surface that is unmounting and keeps a retryable
 * acknowledgement in the global registry until it succeeds.
 */
export function retainPendingWrite(key: PendingWriteKey, flusher: PendingWriteFlusher): void {
  const write = replaceRetainedWrite(key, flusher)
  void runRetainedWrite(write).catch(() => {
    // The current generation stays retryable. A successor with the same key
    // replaces it before issuing its own write.
  })
}

/**
 * Persists current mounted state through the same keyed queue as retained
 * unmount writes. This is what makes a successful newer payload supersede an
 * older failed one instead of letting shutdown retry stale state afterward.
 */
export function persistPendingWrite(
  key: PendingWriteKey,
  flusher: PendingWriteFlusher
): Promise<void> {
  return runRetainedWrite(replaceRetainedWrite(key, flusher))
}

export async function flushPendingWrites(): Promise<void> {
  // A snapshot lets a flusher trigger a render/unmount without changing which
  // callbacks this pass owes an acknowledgement from.
  const results = await Promise.allSettled(
    [...flushers].map((flush) => Promise.resolve().then(() => flush()))
  )
  // A registered flusher can unmount a child and create a retained write. Keep
  // draining snapshots until every such write has acknowledged persistence.
  while (retainedWrites.size > 0) {
    const retainedResults = await Promise.allSettled(
      [...retainedWrites.values()].map((write) => runRetainedWrite(write))
    )
    results.push(...retainedResults)
    if (retainedResults.some((result) => result.status === 'rejected')) break
  }
  const failure = results.find((result) => result.status === 'rejected')
  if (failure?.status === 'rejected') throw failure.reason
}
