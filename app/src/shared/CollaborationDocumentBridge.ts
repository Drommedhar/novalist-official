import type { SceneCommand, SceneApplyResult, SharedSceneSnapshot } from './collaborationDocuments'
import { encodeBase64, decodeBase64 } from './base64'

export interface DocumentSession {
  execute(command: SceneCommand): Promise<SceneApplyResult | null | undefined>
  rename(id: string, entityId: string, oldName: string, newName: string): Promise<Uint8Array | null>
  capture(id: string): Promise<SharedSceneSnapshot>
  dispose(): void | Promise<void>
}

export class DocumentBridge {
  private readonly hosts = new Map<string, DocumentSession>()

  constructor(private readonly createHost: () => DocumentSession) {}

  async handle(method: string, params: unknown): Promise<unknown> {
    if (method !== 'collaboration/documents') throw new Error('Unknown collaboration operation.')
    const request = record(params)
    const session = identity(request.sessionId)
    if (request.action === 'open') {
      if (!this.hosts.has(session)) this.hosts.set(session, this.createHost())
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
        return update === null ? null : encodeBase64(update)
      }
      case 'capture': {
        const snapshot = await host.capture(identity(request.id))
        return { ...snapshot, state: encodeBase64(snapshot.state),
          deletions: snapshot.deletions.map(encodeBase64) }
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
  if (typeof value !== 'string' || !value.trim() || new TextEncoder().encode(value).length > 1024 * 1024)
    throw new Error('Invalid Codex name.')
  return value
}

function state(value: unknown): Uint8Array {
  if (typeof value !== 'string' || !value.length || value.length > Math.ceil(64 * 1024 * 1024 / 3) * 4)
    throw new Error('Invalid shared document state size.')
  const bytes = decodeBase64(value)
  if (bytes.length > 64 * 1024 * 1024 || encodeBase64(bytes) !== value) throw new Error('Invalid shared document state encoding.')
  return bytes
}

function command(value: unknown): SceneCommand {
  const data = record(value)
  const id = identity(data.id)
  switch (data.kind) {
    case 'seed':
      if (typeof data.html !== 'string' || new TextEncoder().encode(data.html).length > 64 * 1024 * 1024) throw new Error('Invalid scene content size.')
      return { kind: 'seed', id, html: data.html }
    case 'seedText':
      if (typeof data.text !== 'string' || new TextEncoder().encode(data.text).length > 64 * 1024 * 1024) throw new Error('Invalid prose content size.')
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
