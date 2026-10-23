import createDOMPurify from 'dompurify'
import { DocumentBridge, type DocumentSession } from '../../../shared/CollaborationDocumentBridge'
import { ProseDocumentHost } from '../../../shared/ProseDocumentHost'
import { createSceneSanitizer } from '../../../shared/sceneSanitizer'
import type { SceneCommand, SceneApplyResult, SharedSceneSnapshot } from '../../../shared/collaborationDocuments'
import { InvalidSceneUpdate } from '../collaboration/validateSceneUpdate'

export class CollaborationDocumentBridge extends DocumentBridge {
  constructor(window: Window & typeof globalThis) {
    super(() => new BrowserDocuments(window))
  }
}

class BrowserDocuments implements DocumentSession {
  private readonly documents: ProseDocumentHost

  constructor(window: Window & typeof globalThis) {
    this.documents = new ProseDocumentHost(window.document,
      createSceneSanitizer(window, createDOMPurify(window)))
  }

  async execute(command: SceneCommand): Promise<SceneApplyResult | undefined> {
    const documents = this.documents
    try {
      switch (command.kind) {
        case 'seed': documents.seed(command.id, command.html); break
        case 'seedText': documents.seedText(command.id, command.text); break
        case 'load': documents.load(command.id, command.state, command.deletions, command.plainText); break
        case 'validate': documents.validate(command.id, command.state); break
        case 'receive': return { accepted: true,
          rejected: await Promise.all(documents.receive(command.id, command.state).map(error => hash(error.update))) }
        case 'remove': {
          const rejected = documents.remove(command.id, command.state)
          if (rejected.length) throw rejected[0]
          break
        }
      }
    } catch (error) {
      if (error instanceof InvalidSceneUpdate && command.kind === 'receive') {
        return { accepted: false, rejected: [await hash(command.state)] }
      }
      throw error
    }
  }

  async rename(id: string, entityId: string, oldName: string, newName: string): Promise<Uint8Array | null> {
    return this.documents.rename(id, entityId, oldName, newName)
  }

  async capture(id: string): Promise<SharedSceneSnapshot> {
    const documents = this.documents
    const plainText = documents.isPlainText(id)
    return {
      state: documents.state(id), deletions: documents.deletions(id),
      html: plainText ? null : documents.project(id), recovered: plainText ? null : documents.recovered(id),
      ...(plainText ? { text: documents.project(id), recoveredText: documents.recovered(id) } : {})
    }
  }

  dispose(): void { this.documents.dispose() }
}

async function hash(update: Uint8Array): Promise<string> {
  const digest = await crypto.subtle.digest('SHA-256', new Uint8Array(update))
  return Array.from(new Uint8Array(digest), byte => byte.toString(16).padStart(2, '0')).join('')
}
