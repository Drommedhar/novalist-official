import type { SharedSceneIdentity } from '../../../shared/sharedScene'
import type { SharedSelection, ParticipantSelection } from './remoteSelections'
import { observeParticipants, useParticipantStore, type Participant } from './participants'
import { SharedItemPresence } from './SharedItemPresence'

interface PresenceEditor {
  selection(): SharedSelection | null
  showParticipant(participant: ParticipantSelection): void
  removeParticipant(id: string): void
}

export class SharedEditorPresence {
  private readonly presence: SharedItemPresence
  private readonly stopObserving: () => void
  private readonly unsubscribe: () => void
  private readonly visible = new Set<string>()
  private closed = false

  constructor(private readonly identity: SharedSceneIdentity, private readonly session: PresenceEditor, active?: () => boolean) {
    this.presence = new SharedItemPresence(identity, crypto.randomUUID(), () => {
      const selected = this.session.selection()
      return selected ? { anchor: btoa(String.fromCharCode(...selected.anchor)), focus: btoa(String.fromCharCode(...selected.focus)) } : null
    }, active)
    this.stopObserving = observeParticipants(identity.sessionId)
    this.unsubscribe = useParticipantStore.subscribe(state => this.render(state[identity.sessionId] ?? []))
    this.render(useParticipantStore.getState()[identity.sessionId] ?? [])
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
    this.unsubscribe()
    this.stopObserving()
    this.presence.close()
    this.clear()
  }
}
