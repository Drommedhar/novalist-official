import * as Y from 'yjs'
import type { RichEditingSurface } from './richEditingSurface'
import { updateRichText } from './richText'
import { SceneMarkup } from './sceneMarkup'
import { validateSceneUpdate } from './validateSceneUpdate'
import { SceneUpdateInbox } from './SceneUpdateInbox'
import { RemoteSelections, type SharedSelection, type ParticipantSelection } from './remoteSelections'

interface SceneOptions {
  initialState?: Uint8Array
  onUpdate(update: Uint8Array): void
  onError(error: Error): void
}

interface SceneSelection {
  anchor: Y.RelativePosition
  focus: Y.RelativePosition
}

export class CollaborativeScene {
  private readonly doc = new Y.Doc()
  private readonly prose = this.doc.getText('prose')
  private readonly root: HTMLElement
  private readonly observer: MutationObserver
  private readonly history: Y.UndoManager
  private readonly markup: SceneMarkup
  private readonly updates: SceneUpdateInbox
  private readonly presence: RemoteSelections
  private applying = false
  private composing = false
  private compositionTimer: ReturnType<typeof setTimeout> | undefined
  private pending: Uint8Array[] = []
  private pendingBytes = 0

  constructor(private readonly editor: RichEditingSurface, private readonly options: SceneOptions) {
    const root = editor.root
    const view = root.ownerDocument.defaultView
    if (!view) throw new Error('The prose editor is not ready.')
    this.root = root
    this.markup = new SceneMarkup(editor, root)
    this.updates = new SceneUpdateInbox(this.doc, this.markup)
    if (options.initialState) {
      validateSceneUpdate(this.doc, options.initialState, this.markup, true)
      Y.applyUpdate(this.doc, options.initialState)
      this.render()
    } else {
      this.capture()
    }
    this.history = new Y.UndoManager(this.prose, { trackedOrigins: new Set([this]) })
    this.presence = new RemoteSelections(view, selection => this.selectionRange(selection))
    this.doc.on('update', this.updated)
    this.observer = new MutationObserver(() => this.capture())
    this.observer.observe(root, { subtree: true, childList: true, characterData: true, attributes: true })
    root.addEventListener('keydown', this.onKeyDown, true)
    root.addEventListener('beforeinput', this.onBeforeInput, true)
    root.addEventListener('compositionstart', this.onCompositionStart)
    root.addEventListener('compositionend', this.onCompositionEnd)
  }

  private readonly updated = (update: Uint8Array, origin: unknown): void => {
    if (origin === this || origin === this.history) this.options.onUpdate(update)
  }

  private readonly onCompositionStart = (): void => {
    clearTimeout(this.compositionTimer)
    this.composing = true
  }

  private readonly onCompositionEnd = (): void => {
    this.compositionTimer = setTimeout(() => {
      this.composing = false
      this.capture()
      const pending = this.pending
      this.pending = []
      this.pendingBytes = 0
      for (const update of pending) {
        try { this.receive(update) }
        catch { this.options.onError(new Error('An invalid shared edit was rejected after composition.')) }
      }
    }, 0)
  }

  private readonly onKeyDown = (event: KeyboardEvent): void => {
    if (this.composing || event.isComposing || !(event.ctrlKey || event.metaKey) || event.altKey) return
    const key = event.key.toLowerCase()
    if (key !== 'z' && key !== 'y') return
    event.preventDefault()
    event.stopImmediatePropagation()
    this.changeHistory(key === 'y' || event.shiftKey)
  }

  private readonly onBeforeInput = (event: InputEvent): void => {
    if (event.inputType !== 'historyUndo' && event.inputType !== 'historyRedo') return
    event.preventDefault()
    event.stopImmediatePropagation()
    this.changeHistory(event.inputType === 'historyRedo')
  }

  private changeHistory(redo: boolean): void {
    this.capture()
    const selection = this.selection()
    if (redo) this.history.redo()
    else this.history.undo()
    this.render()
    if (selection) this.restoreSelection(selection)
    this.editor.flushPendingContentChange()
  }

  capture(): void {
    if (this.applying || this.composing) return
    this.doc.transact(() => {
      updateRichText(this.prose, this.markup.read())
    }, this)
    this.presence?.render()
  }

  getSelection(): SharedSelection | null {
    if (this.composing) return null
    this.capture()
    const selection = this.selection()
    return selection ? { anchor: Array.from(Y.encodeRelativePosition(selection.anchor)), focus: Array.from(Y.encodeRelativePosition(selection.focus)) } : null
  }

  showParticipant(participant: ParticipantSelection): void {
    this.presence.show(participant)
  }

  removeParticipant(id: string): void {
    this.presence.remove(id)
  }

  private selectionRange(selection: SharedSelection): Range | null {
    try { return this.resolveSelectionRange(selection) }
    catch { return null }
  }

  private resolveSelectionRange(selection: SharedSelection): Range | null {
    const anchor = this.domPosition(Y.decodeRelativePosition(new Uint8Array(selection.anchor)))
    const focus = this.domPosition(Y.decodeRelativePosition(new Uint8Array(selection.focus)))
    if (!anchor || !focus) return null
    const range = this.editor.document.createRange()
    range.setStart(anchor.node, anchor.offset)
    range.setEnd(focus.node, focus.offset)
    if (range.collapsed && (anchor.node !== focus.node || anchor.offset !== focus.offset)) {
      range.setStart(focus.node, focus.offset)
      range.setEnd(anchor.node, anchor.offset)
    }
    return range
  }

  encodeState(): Uint8Array {
    this.flushChanges()
    return Y.encodeStateAsUpdate(this.doc)
  }

  flushChanges(): void {
    if (this.composing) throw new Error('Confirm the composition before saving shared state.')
    this.capture()
    this.updates.assertComplete()
  }

  receive(update: Uint8Array): void {
    if (update.byteLength > 64 * 1024 * 1024) throw new Error('The shared scene update is too large.')
    if (this.composing) {
      if (this.pendingBytes + update.byteLength > 8 * 1024 * 1024) throw new Error('Confirm the composition before receiving more shared edits.')
      this.pending.push(update.slice())
      this.pendingBytes += update.byteLength
      return
    }
    this.capture()
    const selection = this.selection()
    let changed = false
    const onChange = (): void => { changed = true }
    this.prose.observe(onChange)
    let rejected
    try { rejected = this.updates.receive(update) }
    finally { this.prose.unobserve(onChange) }
    if (changed) {
      this.render()
      if (selection) this.restoreSelection(selection)
      this.editor.flushPendingContentChange()
    }
    for (const error of rejected) this.options.onError(error)
  }

  private render(): void {
    this.applying = true
    try {
      this.markup.render(this.prose.toDelta())
      this.presence?.render()
      this.observer?.takeRecords()
    } finally {
      this.applying = false
    }
  }

  private selection(): SceneSelection | null {
    const selection = this.editor.document.getSelection()
    if (!selection?.anchorNode || !selection.focusNode) return null
    const anchor = this.relativePosition(selection.anchorNode, selection.anchorOffset)
    const focus = this.relativePosition(selection.focusNode, selection.focusOffset)
    return anchor && focus ? { anchor, focus } : null
  }

  private relativePosition(node: Node, offset: number): Y.RelativePosition | null {
    const index = this.markup.position(node, offset)
    return index === null ? null : Y.createRelativePositionFromTypeIndex(this.prose, index)
  }

  private restoreSelection(selection: SceneSelection): void {
    const anchor = this.domPosition(selection.anchor)
    const focus = this.domPosition(selection.focus)
    if (anchor && focus) this.editor.document.getSelection()?.setBaseAndExtent(anchor.node, anchor.offset, focus.node, focus.offset)
  }

  private domPosition(position: Y.RelativePosition): { node: Node; offset: number } | null {
    const absolute = Y.createAbsolutePositionFromRelativePosition(position, this.doc)
    return absolute?.type === this.prose ? this.markup.domPosition(absolute.index) : null
  }

  destroy(): void {
    if (this.composing) throw new Error('Confirm the composition before closing the shared scene.')
    this.capture()
    this.observer.disconnect()
    this.root.removeEventListener('keydown', this.onKeyDown, true)
    this.root.removeEventListener('beforeinput', this.onBeforeInput, true)
    this.root.removeEventListener('compositionstart', this.onCompositionStart)
    this.root.removeEventListener('compositionend', this.onCompositionEnd)
    clearTimeout(this.compositionTimer)
    this.history.destroy()
    this.presence.destroy()
    this.doc.off('update', this.updated)
    this.doc.destroy()
  }
}
