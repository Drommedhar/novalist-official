import { DocumentHostProcess } from './DocumentHostProcess'
import type { SceneCommand } from '../../shared/collaborationDocuments'

export class DesktopDocumentBridge {
  private readonly hosts = new Map<string, DocumentHostProcess>()

  constructor(private readonly workerEntry?: string) {}

  async request(method: string, params: unknown): Promise<unknown> {
    if (method !== 'collaboration/documents') throw new Error('Unknown desktop operation.')
    const request = record(params)
    const session = identity(request.sessionId)
    if (request.action === 'open') {
      if (!this.hosts.has(session)) this.hosts.set(session, new DocumentHostProcess(this.workerEntry))
      return null
    }
    const host = this.hosts.get(session)
    if (!host) throw new Error('The collaboration session has no document host.')
    switch (request.action) {
      case 'close':
        this.hosts.delete(session)
        await host.dispose()
        return null
      case 'execute':
        return await host.execute(command(request.command)) ?? null
      case 'rename': {
        const update = await host.rename(identity(request.id), identity(request.entityId), name(request.oldName), name(request.newName))
        return update === null ? null : Buffer.from(update).toString('base64')
      }
      case 'capture': {
        const snapshot = await host.capture(identity(request.id))
        return { ...snapshot, state: Buffer.from(snapshot.state).toString('base64'),
          deletions: snapshot.deletions.map(state => Buffer.from(state).toString('base64')) }
      }
      default: throw new Error('Unknown document host operation.')
    }
  }

  async reset(): Promise<void> {
    const hosts = [...this.hosts.values()]
    this.hosts.clear()
    await Promise.all(hosts.map(host => host.dispose()))
  }
}

function record(value: unknown): Record<string, unknown> {
  if (!value || typeof value !== 'object' || Array.isArray(value)) throw new Error('Invalid document operation.')
  return value as Record<string, unknown>
}

function identity(value: unknown): string {
  if (typeof value !== 'string' || !value.length || value.length > 128) throw new Error('Invalid shared document identity.')
  return value
}

function name(value: unknown): string {
  if (typeof value !== 'string' || !value.trim() || Buffer.byteLength(value) > 1024 * 1024)
    throw new Error('Invalid Codex name.')
  return value
}

function state(value: unknown): Uint8Array {
  if (typeof value !== 'string' || !value.length || value.length > Math.ceil(64 * 1024 * 1024 / 3) * 4)
    throw new Error('Invalid shared document state size.')
  const bytes = Buffer.from(value, 'base64')
  if (bytes.length > 64 * 1024 * 1024 || bytes.toString('base64') !== value) throw new Error('Invalid shared document state encoding.')
  return bytes
}

function command(value: unknown): SceneCommand {
  const data = record(value)
  const id = identity(data.id)
  switch (data.kind) {
    case 'seed':
      if (typeof data.html !== 'string' || Buffer.byteLength(data.html) > 64 * 1024 * 1024) throw new Error('Invalid scene content size.')
      return { kind: 'seed', id, html: data.html }
    case 'seedText':
      if (typeof data.text !== 'string' || Buffer.byteLength(data.text) > 64 * 1024 * 1024) throw new Error('Invalid prose content size.')
      return { kind: 'seedText', id, text: data.text }
    case 'load':
      if (data.deletions !== undefined && !Array.isArray(data.deletions)) throw new Error('Invalid scene deletion state.')
      if (data.plainText !== undefined && typeof data.plainText !== 'boolean') throw new Error('Invalid prose format.')
      return { kind: 'load', id, state: state(data.state), deletions: data.deletions?.map(state), plainText: data.plainText }
    case 'receive':
    case 'remove':
    case 'validate':
      return { kind: data.kind, id, state: state(data.state) }
    default: throw new Error('Unknown shared scene operation.')
  }
}
