import * as Y from 'yjs'
import { keymap, type EditorView } from '@codemirror/view'
import { yCollab, yUndoManagerKeymap } from 'y-codemirror.next'
import type { Extension } from '@codemirror/state'
import { SceneUpdateInbox } from './SceneUpdateInbox'
import { validateSceneUpdate } from './validateSceneUpdate'
import { RemoteSelections, type ParticipantSelection, type SharedSelection } from './remoteSelections'
import type { SharedEditorConnection } from './SharedEditorSession'

export class SharedMarkdownSession {
  private readonly doc = new Y.Doc()
  private readonly updates = new SceneUpdateInbox(this.doc, null)
  private readonly pending: Uint8Array[] = []
  private readonly unsubscribe: () => void
  private readonly undo: Y.UndoManager
  private writing: Promise<void> | null = null
  private failed = false
  private view: EditorView | null = null
  private cursors: RemoteSelections | null = null
  readonly extension: Extension

  constructor(state: Uint8Array, private readonly connection: SharedEditorConnection, private readonly changed: (failed: boolean) => void) {
    validateSceneUpdate(this.doc, state, null, true)
    Y.applyUpdate(this.doc, state)
    const text = this.doc.getText('prose')
    this.undo = new Y.UndoManager(text, { trackedOrigins: new Set() })
    this.extension = [yCollab(text, null, { undoManager: this.undo }), keymap.of(yUndoManagerKeymap)]
    this.doc.on('update', (update: Uint8Array, origin: unknown) => {
      if (origin === null) return
      this.pending.push(update.slice())
      this.startWriting()
    })
    this.unsubscribe = connection.subscribe(update => this.receive(update))
  }

  get text(): string { return this.doc.getText('prose').toString() }

  attach(view: EditorView): void {
    this.view = view
    this.cursors = new RemoteSelections(view.dom.ownerDocument.defaultView!, selection => this.range(selection))
  }

  receive(update: Uint8Array): void {
    try { this.updates.receive(update); this.cursors?.render() }
    catch { this.failed = true; this.changed(true) }
  }

  async refresh(): Promise<void> { this.receive(await this.connection.read()) }

  private startWriting(): void {
    if (!this.writing && this.pending.length) this.writing = Promise.resolve().then(() => this.writePending())
  }

  private async writePending(): Promise<void> {
    try {
      while (this.pending.length) {
        const count = this.pending.length
        await this.connection.write(Y.mergeUpdates(this.pending.slice(0, count)))
        this.pending.splice(0, count)
      }
      this.failed = false
    } catch { this.failed = true }
    finally { this.writing = null; this.changed(this.failed) }
  }

  async flush(): Promise<void> {
    if (this.writing) await this.writing
    this.startWriting()
    if (this.writing) await this.writing
    if (this.pending.length) throw new Error('Shared prose is waiting to be saved.')
  }

  selection(): SharedSelection | null {
    if (!this.view?.hasFocus) return null
    const { anchor, head } = this.view.state.selection.main
    const text = this.doc.getText('prose')
    return { anchor: Array.from(Y.encodeRelativePosition(Y.createRelativePositionFromTypeIndex(text, anchor))),
      focus: Array.from(Y.encodeRelativePosition(Y.createRelativePositionFromTypeIndex(text, head))) }
  }

  showParticipant(participant: ParticipantSelection): void { this.cursors?.show(participant) }
  removeParticipant(id: string): void { this.cursors?.remove(id) }

  private range(selection: SharedSelection): Range | null {
    if (!this.view) return null
    try {
      const positions = [selection.anchor, selection.focus].map(encoded =>
        Y.createAbsolutePositionFromRelativePosition(Y.decodeRelativePosition(new Uint8Array(encoded)), this.doc))
      if (positions.some(position => !position || position.type !== this.doc.getText('prose'))) return null
      const [from, to] = positions.map(position => position!.index).sort((a, b) => a - b)
      const start = this.view.domAtPos(from), end = this.view.domAtPos(to)
      const range = this.view.dom.ownerDocument.createRange()
      range.setStart(start.node, start.offset)
      range.setEnd(end.node, end.offset)
      return range
    } catch { return null }
  }

  detach(): void {
    this.unsubscribe()
    this.cursors?.destroy()
    this.cursors = null
    this.view = null
    this.undo.destroy()
    this.doc.destroy()
  }
}
