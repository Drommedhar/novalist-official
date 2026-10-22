import { JSDOM } from 'jsdom'
import createDOMPurify from 'dompurify'
import { createHash } from 'node:crypto'
import * as Y from 'yjs'
import { createSceneSanitizer } from '../../shared/sceneSanitizer'
import { SceneMarkup } from '../../renderer/src/collaboration/sceneMarkup'
import { updateRichText } from '../../renderer/src/collaboration/richText'
import { validateSceneUpdate } from '../../renderer/src/collaboration/validateSceneUpdate'
import { SceneUpdateInbox, type RejectedPendingUpdate } from '../../renderer/src/collaboration/SceneUpdateInbox'

interface Scene {
  doc: Y.Doc
  root: HTMLElement
  markup: SceneMarkup | null
  updates: SceneUpdateInbox
  deletions: Map<string, { state: Uint8Array; html: string }>
}

export class ProseDocumentHost {
  private readonly dom = new JSDOM('<!doctype html>')
  private readonly sanitizer = createSceneSanitizer(this.dom.window, createDOMPurify(this.dom.window))
  private readonly scenes = new Map<string, Scene>()
  private disposed = false

  seed(id: string, html: string): Uint8Array {
    const scene = this.create(id)
    try {
      scene.root.replaceChildren(this.sanitizer.sanitizeSceneFragment(html))
      const original = scene.root.innerHTML
      updateRichText(scene.doc.getText('prose'), scene.markup!.read())
      if (this.html(scene) !== original) throw new Error('This scene cannot yet be shared without changing its content.')
      this.scenes.set(id, scene)
      return Y.encodeStateAsUpdate(scene.doc)
    } catch (error) { scene.doc.destroy(); throw error }
  }

  seedText(id: string, text: string): void {
    const scene = this.create(id, true)
    scene.doc.getText('prose').insert(0, text)
    this.scenes.set(id, scene)
  }

  isPlainText(id: string): boolean { return this.get(id).markup === null }

  load(id: string, state: Uint8Array, deletions: readonly Uint8Array[] = [], plainText = false): void {
    const scene = this.create(id, plainText)
    try {
      validateSceneUpdate(scene.doc, state, scene.markup, true)
      Y.applyUpdate(scene.doc, state)
      for (const deletion of deletions) this.markDeleted(scene, deletion)
      this.scenes.set(id, scene)
    } catch (error) { scene.doc.destroy(); throw error }
  }

  receive(id: string, update: Uint8Array): RejectedPendingUpdate[] {
    return this.get(id).updates.receive(update)
  }

  validate(id: string, update: Uint8Array): void {
    const scene = this.get(id)
    validateSceneUpdate(scene.doc, update, scene.markup, true)
  }

  state(id: string): Uint8Array {
    const scene = this.get(id)
    scene.updates.assertComplete()
    return Y.encodeStateAsUpdate(scene.doc)
  }

  project(id: string): string | null {
    const scene = this.get(id)
    return scene.deletions.size ? null : this.html(scene)
  }

  remove(id: string, deletedAt: Uint8Array): Error[] {
    return this.markDeleted(this.get(id), deletedAt)
  }

  deletions(id: string): Uint8Array[] {
    return [...this.get(id).deletions.values()].map(deletion => deletion.state.slice())
  }

  recovered(id: string): string | null {
    const scene = this.get(id)
    if (!scene.deletions.size) return null
    const html = this.html(scene)
    return [...scene.deletions.values()].some(deletion => deletion.html !== html) ? html : null
  }

  private markDeleted(scene: Scene, deletedAt: Uint8Array): Error[] {
    const state = deletedAt.slice()
    const hash = createHash('sha256').update(state).digest('hex')
    if (scene.deletions.has(hash)) return []
    const baseline = this.createScene(scene.markup === null)
    try {
      validateSceneUpdate(baseline.doc, state, baseline.markup, true)
      Y.applyUpdate(baseline.doc, state)
      const html = this.html(baseline)
      const rejected = scene.updates.receive(state)
      scene.deletions.set(hash, { state, html })
      return rejected
    } finally { baseline.doc.destroy() }
  }

  private html(scene: Scene): string {
    if (!scene.markup) return scene.doc.getText('prose').toString()
    scene.markup.render(scene.doc.getText('prose').toDelta())
    return scene.root.innerHTML
  }

  private create(id: string, plainText = false): Scene {
    if (this.disposed) throw new Error('The shared document host is closed.')
    if (!id || this.scenes.has(id)) throw new Error('The scene already has a shared identity or has no identity.')
    return this.createScene(plainText)
  }

  private createScene(plainText = false): Scene {
    const root = this.dom.window.document.createElement('div')
    const doc = new Y.Doc()
    const markup = plainText ? null : new SceneMarkup({ document: this.dom.window.document,
      sanitizeSceneFragment: this.sanitizer.sanitizeSceneFragment, toDisplayImages: () => {} }, root)
    return { doc, root, markup, deletions: new Map(), updates: new SceneUpdateInbox(doc, markup) }
  }

  private get(id: string): Scene {
    const scene = this.scenes.get(id)
    if (!scene) throw new Error('The shared scene is not loaded.')
    return scene
  }

  dispose(): void {
    for (const scene of this.scenes.values()) scene.doc.destroy()
    this.scenes.clear()
    this.dom.window.close()
    this.disposed = true
  }
}
