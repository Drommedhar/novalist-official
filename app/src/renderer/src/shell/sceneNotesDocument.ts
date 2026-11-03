export interface SceneNotesDocumentState { text: string; loaded: boolean; saving: boolean; error: string | null }

export class SceneNotesDocument {
  state: SceneNotesDocumentState = { text: '', loaded: false, saving: false, error: null }
  private saved = ''
  private generation = 0
  private pending: Promise<void> | null = null
  private unacknowledged = false
  private listeners = new Set<() => void>()
  private read: () => Promise<string>
  private write: (text: string) => Promise<void>
  get dirty(): boolean { return this.state.loaded && (this.unacknowledged || this.state.text !== this.saved) }

  constructor(read: () => Promise<string>, write: (text: string) => Promise<void>) {
    this.read = read
    this.write = write
  }

  subscribe = (listener: () => void): (() => void) => {
    this.listeners.add(listener)
    return () => { this.listeners.delete(listener) }
  }
  snapshot = (): SceneNotesDocumentState => this.state
  private change(patch: Partial<SceneNotesDocumentState>): void {
    this.state = { ...this.state, ...patch }
    for (const listener of this.listeners) listener()
  }

  async load(): Promise<void> {
    const generation = ++this.generation
    try {
      const text = await this.read()
      if (generation !== this.generation) return
      this.saved = text
      this.change({ text, loaded: true, error: null })
    } catch (error) {
      if (generation === this.generation) this.change({ error: String(error) })
    }
  }
  cancelLoad(): void { this.generation++ }
  dispose(): void { this.cancelLoad() }
  update(text: string): void {
    if (this.state.loaded) this.change({ text })
  }
  async flush(): Promise<void> {
    if (this.pending) return this.pending
    const run = async (): Promise<void> => {
      this.change({ saving: true })
      try {
        while (this.dirty) {
          const text = this.state.text
          this.unacknowledged = true
          await this.write(text)
          this.saved = text
          this.unacknowledged = false
        }
        this.change({ error: null })
      } catch (error) {
        this.change({ error: String(error) })
        throw error
      } finally { this.change({ saving: false }) }
    }
    this.pending = run()
    try { await this.pending } finally { this.pending = null }
  }
}
