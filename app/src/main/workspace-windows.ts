import type { RpcMessage } from '../shared/rpcFraming'
import type { WorkspaceChange, WorkspaceEvent, WorkspacePreparation, WorkspaceSnapshot } from '../shared/workspaceProtocol'
import type { BackendRouter } from './backend-router'

interface WorkspaceWindow {
  send(event: WorkspaceEvent): void
}

interface Acknowledgement {
  token: string
  phase: WorkspaceEvent['phase']
  resolve(): void
  reject(error: Error): void
}

/** Coordinates renderer state without importing Electron, so the protocol can
 * be exercised with multiple independent windows in ordinary node tests. */
export class WorkspaceWindows {
  private readonly windows = new Map<number, WorkspaceWindow>()
  private readonly joining = new Map<number, WorkspaceWindow>()
  private readonly settled = new Set<() => void>()
  private readonly acknowledgements = new Map<number, Acknowledgement>()
  private transition: string | null = null
  private lastSnapshot: WorkspaceSnapshot | null = null
  private recovering = false
  private revision = 0
  private recoveryRevision = 0

  constructor(
    private readonly router: BackendRouter,
    private readonly timeout = 15_000,
    private readonly onFailure?: (error: string) => void
  ) {}

  register(owner: number, window: WorkspaceWindow): void {
    if (this.transition) this.joining.set(owner, window)
    else this.windows.set(owner, window)
  }

  remove(owner: number): void {
    this.windows.delete(owner)
    this.joining.delete(owner)
    this.acknowledgements.get(owner)?.resolve()
    this.acknowledgements.delete(owner)
  }

  acknowledge(owner: number, token: string, phase: WorkspaceEvent['phase'], saved: boolean, error?: string): void {
    const pending = this.acknowledgements.get(owner)
    if (!pending || pending.token !== token || pending.phase !== phase) return
    if (saved) pending.resolve()
    else pending.reject(new Error(error || 'A window could not save its pending edits.'))
  }

  async snapshot(owner: number): Promise<WorkspaceSnapshot> {
    const recoveryRevision = this.recoveryRevision
    if (this.recovering) throw new Error('The backend is restoring the workspace. Retry after it reconnects.')
    // A joining window cannot expose old state between prepare and apply.
    if (this.transition) await new Promise<void>((resolve) => this.settled.add(resolve))
    if (this.recovering || recoveryRevision !== this.recoveryRevision) throw new Error('The workspace changed while connecting. Retry the connection.')
    const revision = this.revision
    const snapshot = await this.router.request<WorkspaceSnapshot>('workspace/snapshot')
    if (this.recovering || recoveryRevision !== this.recoveryRevision) throw new Error('The workspace changed while connecting. Retry the connection.')
    if (this.transition || revision !== this.revision) return this.snapshot(owner)
    this.router.setEpoch(snapshot.epoch)
    this.router.settle(owner, snapshot.epoch)
    this.lastSnapshot = snapshot
    return snapshot
  }

  async recover(): Promise<void> {
    const revision = ++this.revision
    this.recoveryRevision++
    this.recovering = true
    for (const pending of this.acknowledgements.values()) pending.reject(new Error('Backend restarted.'))
    this.acknowledgements.clear()
    this.finish()
    const previous = this.lastSnapshot
    // No renderer request is forwarded during recovery, so these owner-zero
    // calls restore the acknowledged scope before the new backend has clients.
    if (previous?.state.projectPath) {
      await this.router.request('project/open', [previous.state.projectPath, previous.state.activeBookId])
      if (revision !== this.revision) throw new Error('Backend recovery was superseded.')
      if (previous.draftId) await this.router.request('project/switchDraft', [previous.draftId])
    }
    const snapshot = await this.router.request<WorkspaceSnapshot>('workspace/snapshot')
    if (revision !== this.revision) throw new Error('Backend recovery was superseded.')
    if (previous && (snapshot.state.projectPath !== previous.state.projectPath ||
      snapshot.state.activeBookId !== previous.state.activeBookId || snapshot.draftId !== previous.draftId)) {
      throw new Error('The previous workspace could not be restored. Your pending edits remain in their windows.')
    }
    this.lastSnapshot = snapshot
    this.router.setEpoch(snapshot.epoch)
    this.recovering = false
  }

  async resync(): Promise<void> {
    const revision = ++this.revision
    this.recoveryRevision++
    this.recovering = true
    for (const pending of this.acknowledgements.values()) pending.reject(new Error('Workspace connection is being refreshed.'))
    this.acknowledgements.clear()
    this.finish()
    const snapshot = await this.router.request<WorkspaceSnapshot>('workspace/snapshot')
    if (revision !== this.revision) throw new Error('Workspace refresh was superseded.')
    this.lastSnapshot = snapshot
    this.router.setEpoch(snapshot.epoch)
    this.recovering = false
  }

  receive(message: RpcMessage): boolean {
    if (!message.method?.startsWith('workspace/')) return false
    const payload = Array.isArray(message.params) ? message.params[0] : message.params
    if (message.method === 'workspace/prepare') void this.prepare(payload as WorkspacePreparation)
    else if (message.method === 'workspace/changed') void this.changed(payload as WorkspaceChange)
    else if (message.method === 'workspace/aborted') void this.abort(payload as { token: string; epoch: number })
    return true
  }

  private wait(owner: number, event: WorkspaceEvent, token: string): Promise<void> {
    return new Promise<void>((resolve, reject) => {
      const timer = setTimeout(() => finish(new Error('A workspace window did not respond.')), this.timeout)
      const finish = (error?: Error): void => {
        clearTimeout(timer)
        if (this.acknowledgements.get(owner)?.token === token) this.acknowledgements.delete(owner)
        if (error) reject(error)
        else resolve()
      }
      this.acknowledgements.set(owner, { token, phase: event.phase, resolve: () => finish(), reject: finish })
      try {
        const window = this.windows.get(owner)
        if (window) window.send(event)
        else finish()
      } catch (error) { finish(error instanceof Error ? error : new Error(String(error))) }
    })
  }

  private async prepare(preparation: WorkspacePreparation): Promise<void> {
    if (this.transition && this.transition !== preparation.token) {
      await this.router.request('workspace/prepared', [preparation.token, false, 'Workspace windows are still applying the previous change.']).catch(() => {})
      return
    }
    this.revision++
    this.transition = preparation.token
    const owners = [...this.windows.keys()]
    for (const owner of owners) this.router.prepare(owner, preparation.token)
    const results = await Promise.allSettled(owners.map((owner) => this.wait(owner, { phase: 'prepare', preparation }, preparation.token)))
    if (this.transition !== preparation.token) return
    const failure = results.find((result) => result.status === 'rejected')
    await this.router.request('workspace/prepared', [preparation.token, !failure,
      failure?.status === 'rejected' ? String(failure.reason) : null]).catch(() => {})
  }

  private async changed(snapshot: WorkspaceChange): Promise<void> {
    const revision = ++this.revision
    this.transition ??= snapshot.token
    this.lastSnapshot = snapshot
    this.router.holdClientReplies()
    this.router.setEpoch(snapshot.epoch)
    const results = await Promise.allSettled([...this.windows.keys()].map(async (owner) => {
      await this.wait(owner, { phase: 'changed', snapshot }, snapshot.token)
      this.router.settle(owner, snapshot.epoch)
    }))
    if (this.transition !== snapshot.token || revision !== this.revision) return
    // Unacknowledged clients retain their old epoch and remain frozen. A later
    // explicit snapshot request is the only way for them to resume safely.
    if (results.some((result) => result.status === 'rejected')) {
      const error = 'A window could not apply the workspace change. Reconnect that window before continuing.'
      this.recovering = true
      this.recoveryRevision++
      this.router.rejectHeldReplies(error)
      this.finish()
      this.onFailure?.(error)
      return
    }
    try {
      await this.router.request('workspace/applied', [snapshot.token])
      if (this.transition !== snapshot.token || revision !== this.revision) return
      await Promise.all([...this.windows.keys()].map((owner) => this.wait(owner,
        { phase: 'resumed', token: snapshot.token, epoch: snapshot.epoch }, snapshot.token)))
      if (this.transition !== snapshot.token || revision !== this.revision) return
      this.router.releaseClientReplies()
    } catch {
      if (this.transition !== snapshot.token || revision !== this.revision) return
      const error = 'A window could not finish loading the workspace change. Reconnect that window before continuing.'
      this.recovering = true
      this.recoveryRevision++
      this.router.rejectHeldReplies(error)
      this.onFailure?.(error)
    }
    finally { if (this.transition === snapshot.token && revision === this.revision) this.finish() }
  }

  private async abort(payload: { token: string; epoch: number }): Promise<void> {
    if (this.transition !== payload.token) return
    const revision = this.revision
    this.router.holdClientReplies()
    for (const owner of this.windows.keys()) {
      this.router.settle(owner, payload.epoch)
    }
    await Promise.allSettled([...this.windows.keys()].map((owner) => this.wait(owner, { phase: 'aborted', ...payload }, payload.token)))
    if (this.transition !== payload.token || revision !== this.revision) return
    this.router.releaseClientReplies()
    this.finish()
  }

  private finish(): void {
    this.transition = null
    for (const [owner, window] of this.joining) this.windows.set(owner, window)
    this.joining.clear()
    for (const resolve of this.settled) resolve()
    this.settled.clear()
  }
}
