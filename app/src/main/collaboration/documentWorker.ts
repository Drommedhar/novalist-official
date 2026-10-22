import { ProseDocumentHost } from './ProseDocumentHost'
import type { DocumentReply, DocumentRequest } from '../../shared/collaborationDocuments'
import { InvalidSceneUpdate } from '../../renderer/src/collaboration/validateSceneUpdate'
import { createHash } from 'node:crypto'

const parent = process.parentPort
if (!parent) throw new Error('The shared document host needs its desktop connection.')
const documents = new ProseDocumentHost()

parent.on('message', ({ data }: { data: DocumentRequest }) => {
  let reply: DocumentReply
  try {
    const command = data.command
    let application: { accepted: boolean; rejected: string[] } | undefined
    switch (command.kind) {
      case 'seed': documents.seed(command.id, command.html); break
      case 'seedText': documents.seedText(command.id, command.text); break
      case 'load': documents.load(command.id, command.state, command.deletions, command.plainText); break
      case 'validate': documents.validate(command.id, command.state); break
      case 'receive': {
        const rejected = documents.receive(command.id, command.state)
        application = { accepted: true, rejected: rejected.map(error => hash(error.update)) }
        break
      }
      case 'remove': {
        const rejected = documents.remove(command.id, command.state)
        if (rejected.length) throw rejected[0]
        break
      }
      case 'capture': break
      default: throw new Error('Unknown shared document operation.')
    }
    reply = { sequence: data.sequence, failed: false, application }
    if (command.kind === 'capture') reply.snapshot = {
      state: documents.state(command.id), deletions: documents.deletions(command.id),
      html: documents.isPlainText(command.id) ? null : documents.project(command.id),
      recovered: documents.isPlainText(command.id) ? null : documents.recovered(command.id),
      ...(documents.isPlainText(command.id) ? { text: documents.project(command.id), recoveredText: documents.recovered(command.id) } : {})
    }
  } catch (error) {
    reply = error instanceof InvalidSceneUpdate && data.command.kind === 'receive'
      ? { sequence: data.sequence, failed: false, application: { accepted: false, rejected: [hash(data.command.state)] } }
      : { sequence: data.sequence, failed: true }
  }
  parent.postMessage(reply)
})

process.on('exit', () => documents.dispose())

function hash(update: Uint8Array): string { return createHash('sha256').update(update).digest('hex') }
