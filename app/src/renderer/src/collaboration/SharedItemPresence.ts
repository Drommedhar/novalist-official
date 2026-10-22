import { rpc } from '../rpc/client'
import { useHostedSessionStore } from './hostedSessionStore'

interface Identity { sessionId: string; itemId: string }
type Selection = { anchor: string; focus: string } | null

export class SharedItemPresence {
  private readonly timer: ReturnType<typeof setInterval>
  private pending: Promise<void> | null = null
  private closed = false
  private lastSelection = ''
  private lastSent = 0

  constructor(private readonly identity: Identity, private readonly view: string = crypto.randomUUID(),
    private readonly selection: () => Selection = () => null) {
    this.timer = setInterval(() => this.tick(), 500)
    this.tick()
  }

  private tick(): void {
    if (this.closed || this.pending) return
    this.pending = this.publish().finally(() => { this.pending = null })
  }

  private async publish(): Promise<void> {
    try {
      const selection = this.selection()
      const encoded = JSON.stringify(selection)
      if (encoded === this.lastSelection && Date.now() - this.lastSent < 5000) return
      await rpc.request('collaboration/presence', [this.identity.sessionId, this.identity.itemId, this.view, selection, true])
      this.lastSelection = encoded
      this.lastSent = Date.now()
      this.report(false)
    } catch { this.report(true) }
  }

  private report(failed: boolean): void {
    if (!this.closed) useHostedSessionStore.getState().reportPresence(this.identity.sessionId, this.view, failed)
  }

  close(): void {
    if (this.closed) return
    this.closed = true
    clearInterval(this.timer)
    useHostedSessionStore.getState().reportPresence(this.identity.sessionId, this.view, false)
    void (this.pending ?? Promise.resolve()).then(() =>
      rpc.notify('collaboration/presence', [this.identity.sessionId, this.identity.itemId, this.view, null, false]))
  }
}
