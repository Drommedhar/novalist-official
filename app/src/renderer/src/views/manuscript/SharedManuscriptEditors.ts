import type { SharedSceneIdentity } from '../../../../shared/sharedScene'
import { SharedEditorSession, type SharedEditorStatus } from '../../collaboration/SharedEditorSession'
import { SharedEditorPresence } from '../../collaboration/SharedEditorPresence'
import { decodeState, sharedDocumentConnection, receiveRetainedDocument, flushRetainedDocument, retainSharedDocument, hasRetainedDocument, observeRetainedDocuments } from '../../collaboration/sharedDocuments'
import type { ManuscriptSectionDto } from '../../stores/manuscriptStore'
import { reportSharedManuscript } from '../../stores/manuscriptSharedStatus'

export interface ManuscriptEditingWindow extends Window {
  sanitizeSceneFragment(html: string): DocumentFragment
  toDisplayImages(root: ParentNode): void
  getSceneContent(root: HTMLElement): string
  flushSceneContent(root: HTMLElement): void
  setImageBase(base: string): void
}

interface Binding {
  root: HTMLElement
  identity: SharedSceneIdentity
  generation: string
  chapter: string
  active: boolean
  session: SharedEditorSession
  presence?: SharedEditorPresence
}

export class SharedManuscriptEditors {
  private readonly bindings = new Map<string, Binding>()
  private readonly stopObserving = observeRetainedDocuments(() => this.capture())

  prepare(sections: ManuscriptSectionDto[]): void {
    const scenes = new Map(sections.flatMap(section => section.scenes.map(scene => [scene.sceneId, scene.shared] as const)))
    for (const [id, binding] of this.bindings) {
      const identity = scenes.get(id)
      if (identity?.sessionId !== binding.identity.sessionId || identity.itemId !== binding.identity.itemId) this.detach(id, binding)
    }
  }

  sync(sections: ManuscriptSectionDto[], editor: ManuscriptEditingWindow): void {
    for (const section of sections) for (const scene of section.scenes) {
      if (!scene.shared) continue
      const root = editor.document.querySelector<HTMLElement>(`.scene-editor[data-scene-id="${CSS.escape(scene.sceneId)}"]`)
      if (!root) continue
      const previous = this.bindings.get(scene.sceneId)
      if (previous?.root === root) {
        previous.chapter = section.chapterGuid
        if (previous.identity.state !== scene.shared.state) {
          previous.identity = scene.shared
          previous.session.receive(decodeState(scene.shared.state))
        }
      } else {
        if (previous) this.detach(scene.sceneId, previous)
        this.attach(scene.sceneId, section.chapterGuid, scene.shared, root, editor)
      }
    }
  }

  private attach(id: string, chapter: string, identity: SharedSceneIdentity, root: HTMLElement, editor: ManuscriptEditingWindow): void {
    const generation = crypto.randomUUID()
    const publish = (status: SharedEditorStatus): void => reportSharedManuscript(generation, binding.chapter, id,
      { ...status, pending: status.pending || binding.active && hasRetainedDocument(identity) }, binding.active)
    const session = new SharedEditorSession({
      root, document: root.ownerDocument,
      sanitizeSceneFragment: html => editor.sanitizeSceneFragment(html),
      toDisplayImages: content => editor.toDisplayImages(content),
      getContent: () => editor.getSceneContent(root),
      getPlainText: () => root.innerText,
      flushPendingContentChange: () => editor.flushSceneContent(root)
    }, decodeState(identity.state), sharedDocumentConnection(identity), publish)
    const binding: Binding = { root, identity, generation, chapter, active: true, session }
    this.bindings.set(id, binding)
    if (identity.presence) binding.presence = new SharedEditorPresence(identity, session, () =>
      root.ownerDocument.activeElement === root && editor.frameElement?.ownerDocument.activeElement === editor.frameElement)
    receiveRetainedDocument(identity, session)
    void session.refresh().catch(() => {
      if (binding.active) publish({ html: editor.getSceneContent(root), plainText: root.innerText,
        pending: true, error: new Error('The shared scene could not be refreshed.') })
    })
  }

  capture(id?: string): boolean {
    if (id) {
      const binding = this.bindings.get(id)
      binding?.session.capture()
      return !!binding
    }
    for (const binding of this.bindings.values()) binding.session.capture()
    return true
  }

  async flush(): Promise<void> {
    for (const binding of this.bindings.values()) {
      await flushRetainedDocument(binding.identity)
      await binding.session.flush()
      await binding.session.refresh()
    }
  }

  private detach(id: string, binding: Binding): void {
    binding.presence?.close()
    binding.active = false
    retainSharedDocument(binding.identity, binding.generation, binding.session)
    this.bindings.delete(id)
  }

  close(): void {
    this.stopObserving()
    for (const [id, binding] of this.bindings) this.detach(id, binding)
  }
}
