import { rpc } from '../rpc/client'
import type { SharedSceneIdentity } from '../../../shared/sharedScene'
import type { SharedSelection, ParticipantSelection } from './remoteSelections'
import { useHostedSessionStore } from './hostedSessionStore'

interface Participant {
  deviceId: string
  viewId: string
  itemId: string
  name: string
  selection: { anchor: string; focus: string } | null
}

interface PresenceEditor {
  selection(): SharedSelection | null
  showParticipant(participant: ParticipantSelection): void
  removeParticipant(id: string): void
}

export class SharedEditorPresence {
  private readonly view = crypto.randomUUID()
  private readonly timer: ReturnType<typeof setInterval>
  private readonly visible = new Set<string>()
  private publishing = false
  private polling = false
  private publishFailed = false
  private pollFailed = false
  private closed = false
  private lastSelection = ''
  private lastSent = 0

  constructor(private readonly identity: SharedSceneIdentity, private readonly session: PresenceEditor) {
    this.timer = setInterval(() => this.tick(), 500)
    this.tick()
  }

  private tick(): void {
    if (this.closed) return
    if (!this.publishing) void this.publish()
    if (!this.polling) void this.refresh()
  }

  private async publish(): Promise<void> {
    this.publishing = true
    try {
      const selected = this.session.selection()
      const selection = selected ? { anchor: btoa(String.fromCharCode(...selected.anchor)), focus: btoa(String.fromCharCode(...selected.focus)) } : null
      const encoded = JSON.stringify(selection)
      if (encoded !== this.lastSelection || Date.now() - this.lastSent >= 5000) {
        await rpc.request('collaboration/presence', [this.identity.sessionId, this.identity.itemId, this.view, selection, true])
        this.lastSelection = encoded
        this.lastSent = Date.now()
        this.publishFailed = false
      }
    } catch { this.publishFailed = true }
    finally { this.publishing = false; this.report() }
  }

  private async refresh(): Promise<void> {
    this.polling = true
    try {
      const participants = await rpc.request<Participant[]>('collaboration/participants', [this.identity.sessionId])
      if (!this.closed) this.render(participants)
      this.pollFailed = false
    } catch {
      this.clear()
      this.pollFailed = true
    } finally { this.polling = false; this.report() }
  }

  private report(): void {
    if (!this.closed) useHostedSessionStore.getState().reportPresence(this.identity.sessionId, this.view, this.publishFailed || this.pollFailed)
  }

  private render(participants: Participant[]): void {
    const current = new Set<string>()
    for (const participant of participants) {
      if (participant.itemId !== this.identity.itemId || !participant.selection) continue
      const id = participant.deviceId + ':' + participant.viewId
      current.add(id)
      this.session.showParticipant({ id, name: participant.name, selection: {
        anchor: Array.from(atob(participant.selection.anchor), character => character.charCodeAt(0)),
        focus: Array.from(atob(participant.selection.focus), character => character.charCodeAt(0))
      } })
      this.visible.add(id)
    }
    for (const id of this.visible) if (!current.has(id)) { this.session.removeParticipant(id); this.visible.delete(id) }
  }

  private clear(): void {
    for (const id of this.visible) this.session.removeParticipant(id)
    this.visible.clear()
  }

  close(): void {
    if (this.closed) return
    this.closed = true
    clearInterval(this.timer)
    this.clear()
    useHostedSessionStore.getState().reportPresence(this.identity.sessionId, this.view, false)
    rpc.notify('collaboration/presence', [this.identity.sessionId, this.identity.itemId, this.view, null, false])
  }
}
