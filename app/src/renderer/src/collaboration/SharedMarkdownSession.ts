import * as Y from 'yjs'
import { keymap, type EditorView } from '@codemirror/view'
import { yCollab, yUndoManagerKeymap } from 'y-codemirror.next'
import { EditorState, type Extension } from '@codemirror/state'
import { diffChars } from 'diff'
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
  private detached = false
  private view: EditorView | null = null
  private cursors: RemoteSelections | null = null
  readonly extension: Extension

  constructor(state: Uint8Array, private readonly connection: SharedEditorConnection, private readonly changed: (failed: boolean) => void) {
    validateSceneUpdate(this.doc, state, null, true)
    Y.applyUpdate(this.doc, state)
    const text = this.doc.getText('prose')
    this.undo = new Y.UndoManager(text, { trackedOrigins: new Set() })
    // Keep carriage returns as document characters. CodeMirror's default CRLF
    // folding would shift every following Y.Text offset and corrupt edits.
    this.extension = [EditorState.lineSeparator.of('\n'), yCollab(text, null, { undoManager: this.undo }), keymap.of(yUndoManagerKeymap)]
    this.doc.on('update', (update: Uint8Array, origin: unknown) => {
      if (origin !== null) {
        this.pending.push(update.slice())
        this.startWriting()
      }
      // Checklist views have no EditorView to observe remote changes through.
      this.changed(this.failed)
    })
    this.unsubscribe = connection.subscribe(update => this.receive(update))
  }

  get text(): string { return this.doc.getText('prose').toString() }
  get dirty(): boolean { return this.pending.length > 0 }
  get saving(): boolean { return this.writing !== null }
  get hasError(): boolean { return this.failed }

  /** Change only the edited characters so concurrent prose stays owned by its
   * author. Replacing the entire Y.Text would delete unrelated shared edits. */
  update(next: string): void {
    if (this.detached) return
    const text = this.doc.getText('prose')
    const changes = diffChars(text.toString(), next)
    this.doc.transact(() => {
      let offset = 0
      for (const change of changes) {
        if (change.removed) text.delete(offset, change.value.length)
        else {
          if (change.added) text.insert(offset, change.value)
          offset += change.value.length
        }
      }
    }, this)
  }

  attach(view: EditorView): void {
    this.detachView()
    this.view = view
    this.cursors = new RemoteSelections(view.dom.ownerDocument.defaultView!, selection => this.range(selection))
  }

  /** A checklist can temporarily unmount its editor while retaining the live
   * document, pending changes and remote subscription. */
  detachView(): void {
    this.cursors?.destroy()
    this.cursors = null
    this.view = null
  }

  receive(update: Uint8Array): void {
    if (this.detached) return
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
    if (this.detached) return
    this.detached = true
    this.unsubscribe()
    this.detachView()
    this.undo.destroy()
    this.doc.destroy()
  }
}
