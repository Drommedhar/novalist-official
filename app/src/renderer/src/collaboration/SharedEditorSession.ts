import { CollaborativeScene } from './CollaborativeScene'
import type { EditorWindow } from '../views/editor/editorBridge'
import type { SharedSelection, ParticipantSelection } from './remoteSelections'
import { mergeUpdates } from 'yjs'

export interface SharedEditorConnection {
  write(update: Uint8Array): Promise<void>
  read(): Promise<Uint8Array>
  subscribe(receive: (update: Uint8Array) => void): () => void
}

export interface SharedEditorStatus {
  html: string
  plainText: string
  pending: boolean
  error: Error | null
}

export class SharedEditorSession {
  private readonly scene: CollaborativeScene
  private readonly unsubscribe: () => void
  private readonly pending: Uint8Array[] = []
  private writing: Promise<void> | null = null
  private error: Error | null = null
  private detached = false
  private content: Pick<SharedEditorStatus, 'html' | 'plainText'>

  constructor(private readonly editor: EditorWindow, initialState: Uint8Array,
    private readonly connection: SharedEditorConnection,
    private readonly changed: (status: SharedEditorStatus) => void) {
    this.content = { html: editor.getContent(), plainText: editor.getPlainText() }
    this.scene = new CollaborativeScene(editor, {
      initialState,
      onUpdate: update => {
        this.pending.push(update.slice())
        this.publish()
        this.startWriting()
      },
      onError: error => { this.error = error; this.publish() }
    })
    this.unsubscribe = connection.subscribe(update => this.receive(update))
  }

  capture(): void {
    if (!this.detached) this.scene.capture()
    this.publish()
  }

  state(): Uint8Array { return this.scene.encodeState() }
  selection(): SharedSelection | null { return this.detached ? null : this.scene.getSelection() }
  showParticipant(participant: ParticipantSelection): void { if (!this.detached) this.scene.showParticipant(participant) }
  removeParticipant(id: string): void { if (!this.detached) this.scene.removeParticipant(id) }

  async refresh(): Promise<void> {
    this.receive(await this.connection.read())
  }

  receive(update: Uint8Array): void {
    if (this.detached) return
    try { this.scene.receive(update) }
    catch { this.error = new Error('A shared scene update could not be applied.') }
    this.publish()
  }

  private startWriting(): void {
    if (!this.writing && this.pending.length) this.writing = Promise.resolve().then(() => this.writePending())
  }

  private async writePending(): Promise<void> {
    try {
      while (this.pending.length) {
        const count = this.pending.length
        const update = count === 1 ? this.pending[0] : mergeUpdates(this.pending.slice(0, count))
        await this.connection.write(update)
        this.pending.splice(0, count)
        this.error = null
        this.publish()
      }
    } catch {
      this.error = new Error('Shared edits are still waiting to be saved. Retry before closing this project.')
      this.publish()
    } finally { this.writing = null }
  }

  async flush(): Promise<void> {
    if (!this.detached) this.scene.flushChanges()
    if (this.writing) await this.writing
    this.startWriting()
    if (this.writing) await this.writing
    if (this.pending.length) throw this.error ?? new Error('Shared edits have not been saved.')
  }

  detach(): void {
    if (this.detached) return
    this.scene.destroy()
    this.content = { html: this.editor.getContent(), plainText: this.editor.getPlainText() }
    this.detached = true
    this.unsubscribe()
  }

  private publish(): void {
    if (!this.detached) this.content = { html: this.editor.getContent(), plainText: this.editor.getPlainText() }
    this.changed({ ...this.content, pending: this.pending.length > 0, error: this.error })
  }
}
