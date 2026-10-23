import { BackendRouter, type BackendClient } from '../../../main/backend-router'
import { WorkspaceWindows } from '../../../main/workspace-windows'
import type { WorkspaceEvent, WorkspaceSnapshot } from '../../../shared/workspaceProtocol'

export class MobileBackend {
  private readonly router: BackendRouter
  private readonly workspace: WorkspaceWindows
  private readonly listeners = new Set<(event: WorkspaceEvent) => Promise<void> | void>()

  constructor(write: (frame: Uint8Array) => void, fail: (error: string) => void) {
    this.router = new BackendRouter(write, message => this.workspace.receive(message))
    this.workspace = new WorkspaceWindows(this.router, 15_000, fail)
    this.workspace.register(1, { send: event => { void this.deliver(event) } })
  }

  attach(port: BackendClient): void { this.router.attach(1, port) }
  send(port: BackendClient, frame: Uint8Array): void { this.router.receive(1, port, frame) }
  receive(frame: Uint8Array): void { this.router.receiveBackend(frame) }
  snapshot(): Promise<WorkspaceSnapshot> { return this.workspace.snapshot(1) }
  subscribe(handler: (event: WorkspaceEvent) => Promise<void> | void): () => void {
    this.listeners.add(handler)
    return () => { this.listeners.delete(handler) }
  }
  dispose(): void { this.workspace.remove(1); this.router.dispose(); this.listeners.clear() }

  private async deliver(event: WorkspaceEvent): Promise<void> {
    const token = event.phase === 'prepare' ? event.preparation.token : event.phase === 'changed' ? event.snapshot.token : event.token
    try {
      if (!this.listeners.size) throw new Error('The mobile workspace is not ready.')
      await Promise.all([...this.listeners].map(handler => handler(event)))
      this.workspace.acknowledge(1, token, event.phase, true)
    } catch (error) { this.workspace.acknowledge(1, token, event.phase, false, String(error)) }
  }
}
