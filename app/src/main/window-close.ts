import { randomUUID } from 'node:crypto'

import type { CloseStage } from '../shared/workspaceProtocol'
interface ClosingWindow {
  send(token: string, stage: CloseStage): void
  focus(): void
}

/** A close is a transaction: every participant stays locked until all saves
 * and the main window's close backup succeed, or all participants resume. */
export class WindowCloseCoordinator {
  private readonly windows = new Map<number, ClosingWindow>()
  private readonly pending = new Map<number, { token: string; resolve(): void; reject(error: Error): void }>()
  private active: Promise<void> | null = null

  constructor(private readonly timeout = 15_000) {}

  register(owner: number, window: ClosingWindow): void { this.windows.set(owner, window) }
  has(owner: number): boolean { return this.windows.has(owner) }
  remove(owner: number): void {
    this.windows.delete(owner)
    this.pending.get(owner)?.resolve()
    this.pending.delete(owner)
  }
  acknowledge(owner: number, token: string, saved: boolean): void {
    const pending = this.pending.get(owner)
    if (!pending || pending.token !== token) return
    if (saved) pending.resolve()
    else {
      this.windows.get(owner)?.focus()
      pending.reject(new Error('A window could not save its pending edits.'))
    }
  }

  prepare(owners: number[], backupOwner?: number): Promise<void> {
    // A detached close can be followed by a global quit/install. The latter
    // must earn acknowledgements from its own complete participant set.
    if (this.active) return this.active.catch(() => {}).then(() => this.prepare(owners, backupOwner))
    const token = randomUUID()
    const run = async (): Promise<void> => {
      try {
        const results = await Promise.allSettled(owners.map((owner) => this.ask(owner, token, 'flush')))
        const failure = results.find((result) => result.status === 'rejected')
        if (failure?.status === 'rejected') throw failure.reason
        if (backupOwner !== undefined) await this.ask(backupOwner, token, 'backup')
      } catch (error) {
        for (const owner of owners) this.windows.get(owner)?.send(token, 'abort')
        throw error
      }
    }
    const operation = run()
    this.active = operation
    void operation.finally(() => { if (this.active === operation) this.active = null }).catch(() => {})
    return operation
  }

  abort(owners: number[]): void {
    for (const owner of owners) this.windows.get(owner)?.send('', 'abort')
  }

  private ask(owner: number, token: string, stage: CloseStage): Promise<void> {
    const window = this.windows.get(owner)
    if (!window) return Promise.resolve()
    return new Promise<void>((resolve, reject) => {
      const timer = setTimeout(() => finish(new Error('A window did not respond to the close request.')), this.timeout)
      const finish = (error?: Error): void => {
        clearTimeout(timer)
        this.pending.delete(owner)
        if (error) reject(error)
        else resolve()
      }
      this.pending.set(owner, { token, resolve: () => finish(), reject: finish })
      try { window.send(token, stage) }
      catch (error) { finish(error instanceof Error ? error : new Error(String(error))) }
    })
  }
}
