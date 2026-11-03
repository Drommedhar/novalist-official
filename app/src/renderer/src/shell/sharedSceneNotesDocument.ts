import type { SharedMarkdownSession } from '../collaboration/SharedMarkdownSession'
import type { SceneNotesDocumentState } from './sceneNotesDocument'

/** A checklist and its Markdown editor use the same shared prose document.
 * This adapter observes it even while the editor itself is not mounted. */
export class SharedSceneNotesDocument {
  state: SceneNotesDocumentState = { text: '', loaded: false, saving: false, error: null }
  private session: SharedMarkdownSession | null = null
  private generation = 0
  private listeners = new Set<() => void>()
  private open: (changed: (failed: boolean) => void) => Promise<SharedMarkdownSession>

  constructor(open: (changed: (failed: boolean) => void) => Promise<SharedMarkdownSession>) { this.open = open }

  get dirty(): boolean { return this.session?.dirty ?? false }
  get markdownSession(): SharedMarkdownSession | undefined { return this.session ?? undefined }
  subscribe = (listener: () => void): (() => void) => {
    this.listeners.add(listener)
    return () => { this.listeners.delete(listener) }
  }
  snapshot = (): SceneNotesDocumentState => this.state

  private change(patch: Partial<SceneNotesDocumentState>): void {
    this.state = { ...this.state, ...patch }
    for (const listener of this.listeners) listener()
  }

  private publish(failed = this.session?.hasError ?? false): void {
    if (!this.session) return
    this.change({ text: this.session.text, saving: this.session.saving,
      error: failed ? 'Shared scene notes are waiting to be saved.' : null })
  }

  async load(): Promise<void> {
    const generation = ++this.generation
    try {
      if (!this.session) {
        let opened: SharedMarkdownSession | undefined
        opened = await this.open(failed => { if (opened && this.session === opened) this.publish(failed) })
        if (generation !== this.generation) { opened.detach(); return }
        this.session = opened
      }
      // Subscribe before this second read so startup/reconnect cannot miss an
      // update between the initial checkpoint and notification registration.
      await this.session.refresh()
      if (generation !== this.generation) return
      this.change({ loaded: true })
      this.publish()
    } catch (error) {
      if (generation === this.generation) this.change({ error: String(error) })
    }
  }

  cancelLoad(): void { this.generation++ }
  update(text: string): void { if (this.state.loaded) this.session?.update(text) }

  async flush(): Promise<void> {
    try {
      await this.session?.flush()
      this.publish()
    } catch (error) {
      this.change({ error: String(error), saving: false })
      throw error
    }
  }

  dispose(): void {
    this.cancelLoad()
    this.session?.detach()
    this.session = null
    this.change({ loaded: false, saving: false })
  }
}
