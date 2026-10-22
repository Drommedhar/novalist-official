import * as Y from 'yjs'
import type { SceneMarkup } from './sceneMarkup'
import { validateSceneUpdate } from './validateSceneUpdate'

export class RejectedPendingUpdate extends Error {
  constructor(readonly update: Uint8Array) { super('An invalid pending shared edit was rejected.') }
}

export class SceneUpdateInbox {
  private pending: Uint8Array[] = []
  private pendingBytes = 0

  constructor(private readonly doc: Y.Doc, private readonly markup: SceneMarkup) {}

  assertComplete(): void {
    if (this.pending.length) throw new Error('Shared edits are still waiting for missing history. Synchronize before capturing the scene.')
  }

  receive(update: Uint8Array): RejectedPendingUpdate[] {
    if (!validateSceneUpdate(this.doc, update, this.markup)) {
      if (this.pending.some(value => value.length === update.length && value.every((byte, index) => byte === update[index]))) return []
      if (this.pendingBytes + update.length > 8 * 1024 * 1024 || this.pending.length >= 1024)
        throw new Error('Too many shared edits are waiting for missing history. Reconnect to synchronize the scene.')
      this.pending.push(update.slice())
      this.pendingBytes += update.length
      return []
    }
    Y.applyUpdate(this.doc, update)
    const rejected: RejectedPendingUpdate[] = []
    let progressed: boolean
    do {
      progressed = false
      const waiting: Uint8Array[] = []
      for (const pending of this.pending) {
        try {
          if (!validateSceneUpdate(this.doc, pending, this.markup)) { waiting.push(pending); continue }
          Y.applyUpdate(this.doc, pending)
          progressed = true
        } catch {
          rejected.push(new RejectedPendingUpdate(pending))
        }
        this.pendingBytes -= pending.length
      }
      this.pending = waiting
    } while (progressed && this.pending.length)
    return rejected
  }
}
