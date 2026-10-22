import { utilityProcess, type UtilityProcess } from 'electron'
import { join } from 'node:path'
import type { DocumentReply, DocumentRequest, SceneCommand, SharedSceneSnapshot, SceneApplyResult } from '../../shared/collaborationDocuments'

interface PendingDocument {
  resolve(reply: Extract<DocumentReply, { failed: false }>): void
  reject(error: Error): void
  timer: ReturnType<typeof setTimeout>
}

export class DocumentHostProcess {
  private readonly child: UtilityProcess
  private readonly pending = new Map<number, PendingDocument>()
  private sequence = 0
  private closed = false
  private readonly exited: Promise<void>

  constructor(entry = join(__dirname, 'collaborationHost.js')) {
    this.child = utilityProcess.fork(entry, [], { stdio: 'ignore', serviceName: 'Novalist shared documents' })
    this.child.on('spawn', () => { if (this.closed) this.child.kill() })
    this.child.on('error', () => {
      this.fail(new Error('The shared document process failed. Reopen it from saved collaboration state.'))
      this.child.kill()
    })
    this.child.on('message', (reply: DocumentReply) => this.receive(reply))
    this.exited = new Promise(resolve => this.child.once('exit', () => {
      this.fail(new Error('The shared document process stopped. Reopen it from saved collaboration state.'))
      resolve()
    }))
  }

  async execute(command: SceneCommand): Promise<SceneApplyResult | undefined> {
    return (await this.request(command)).application
  }

  async capture(id: string): Promise<SharedSceneSnapshot> {
    const { snapshot } = await this.request({ kind: 'capture', id })
    if (!snapshot) throw new Error('The shared document process returned no saved state.')
    return snapshot
  }

  private request(command: DocumentRequest['command']): Promise<Extract<DocumentReply, { failed: false }>> {
    if (this.closed) return Promise.reject(new Error('The shared document process is closed.'))
    const sequence = ++this.sequence
    return new Promise((resolve, reject) => {
      const timer = setTimeout(() => {
        this.fail(new Error('The shared document process did not respond. Reopen it from saved collaboration state.'))
        this.child.kill()
      }, 30_000)
      this.pending.set(sequence, { resolve, reject, timer })
      try { this.child.postMessage({ sequence, command } satisfies DocumentRequest) }
      catch (error) {
        clearTimeout(timer)
        this.pending.delete(sequence)
        reject(error)
      }
    })
  }

  private receive(reply: DocumentReply): void {
    const pending = this.pending.get(reply.sequence)
    if (!pending) return
    clearTimeout(pending.timer)
    this.pending.delete(reply.sequence)
    if (reply.failed) pending.reject(new Error('The shared document operation was rejected.'))
    else pending.resolve(reply)
  }

  private fail(error: Error): void {
    this.closed = true
    for (const pending of this.pending.values()) {
      clearTimeout(pending.timer)
      pending.reject(error)
    }
    this.pending.clear()
  }

  async dispose(): Promise<void> {
    if (!this.closed) {
      this.fail(new Error('The shared document process is closed.'))
      this.child.kill()
    }
    let timer: ReturnType<typeof setTimeout> | undefined
    try {
      await Promise.race([this.exited, new Promise<never>((_, reject) => {
        timer = setTimeout(() => reject(new Error('The shared document process could not be stopped.')), 5_000)
      })])
    } finally { clearTimeout(timer) }
  }
}
