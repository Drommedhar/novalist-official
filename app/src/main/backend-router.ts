import { encodeRpcFrame, RpcFrameDecoder, type RpcId, type RpcMessage } from '../shared/rpcFraming'
import { desktopRequestId } from '../shared/workspaceProtocol'

export interface BackendClient {
  postMessage(message: unknown): void
  close(): void
}

interface ClientConnection {
  port: BackendClient
  decoder: RpcFrameDecoder
  epoch: number
  flushToken?: string
}

interface PendingRequest {
  owner: number
  port: BackendClient | null
  originalId?: RpcId
  resolve?: (result: unknown) => void
  reject?: (error: Error) => void
}

/**
 * The backend has one framed stream; each renderer has its own request IDs.
 * Only complete messages cross this boundary, and only the originating port
 * receives a reply. A replaced port never receives its predecessor's replies.
 */
export class BackendRouter {
  private readonly clients = new Map<number, ClientConnection>()
  private readonly pending = new Map<string, PendingRequest>()
  private stdout = new RpcFrameDecoder()
  private sequence = 0
  private epoch = 0
  private dialogOwner: number | null = null
  private recovering = false
  private readonly heldReplyIds = new Set<string>()
  private readonly heldReplies: RpcMessage[] = []

  constructor(
    private readonly write: (frame: Uint8Array) => void,
    private readonly notification: (message: RpcMessage) => boolean = () => false
  ) {}

  attach(owner: number, port: BackendClient): void {
    this.detach(owner)
    this.clients.set(owner, { port, decoder: new RpcFrameDecoder(), epoch: this.epoch })
    if (this.recovering) port.postMessage({ novalistControl: 'backend-recovering' })
    if (this.dialogOwner === null) this.dialogOwner = owner
  }

  detach(owner: number, port?: BackendClient): void {
    if (port && this.clients.get(owner)?.port !== port) return
    this.clients.get(owner)?.port.close()
    this.clients.delete(owner)
    for (const [id, pending] of this.pending) {
      if (pending.owner === owner) this.pending.delete(id)
    }
    if (this.dialogOwner === owner) this.dialogOwner = this.clients.keys().next().value ?? null
  }

  hasClient(owner: number): boolean {
    return this.clients.has(owner)
  }

  setDialogOwner(owner: number): void {
    if (this.clients.has(owner)) this.dialogOwner = owner
  }

  prepare(owner: number, token: string): void {
    const client = this.clients.get(owner)
    if (client) client.flushToken = token
  }

  settle(owner: number, epoch: number): void {
    const client = this.clients.get(owner)
    if (!client) return
    client.epoch = epoch
    client.flushToken = undefined
  }

  setEpoch(epoch: number): void {
    this.epoch = epoch
  }

  holdClientReplies(): void {
    for (const [id, pending] of this.pending) if (pending.owner !== 0) this.heldReplyIds.add(id)
  }

  releaseClientReplies(): void {
    this.heldReplyIds.clear()
    for (const reply of this.heldReplies.splice(0)) this.deliverReply(reply)
  }

  rejectHeldReplies(error: string): void {
    const ids = [...this.heldReplyIds]
    this.heldReplyIds.clear()
    this.heldReplies.length = 0
    for (const id of ids) this.deliverReply({ jsonrpc: '2.0', id, error: { code: -32000, message: error } })
  }

  receive(owner: number, port: BackendClient, chunk: Uint8Array): void {
    const client = this.clients.get(owner)
    if (!client || client.port !== port) return
    try {
      for (const message of client.decoder.push(chunk)) {
        if (typeof message.method !== 'string') continue
        if (this.recovering) {
          if (message.id !== undefined) port.postMessage(encodeRpcFrame({ jsonrpc: '2.0', id: message.id,
            error: { code: -32000, message: 'The backend is restoring the workspace. Retry after it reconnects.' } }))
          continue
        }
        const id = desktopRequestId(owner, client.epoch, ++this.sequence, client.flushToken)
        this.pending.set(id, { owner, port, originalId: message.id })
        this.write(encodeRpcFrame({ ...message, id }))
      }
    } catch {
      // A malformed client frame must not corrupt another window's transport.
      port.postMessage({ novalistControl: 'backend-protocol-error' })
      this.detach(owner)
    }
  }

  request<T>(method: string, params?: unknown, token?: string): Promise<T> {
    const id = desktopRequestId(0, this.epoch, ++this.sequence, token)
    return new Promise<T>((resolve, reject) => {
      this.pending.set(id, { owner: 0, port: null, resolve: resolve as (result: unknown) => void, reject })
      try {
        this.write(encodeRpcFrame({ jsonrpc: '2.0', id, method, params }))
      } catch (error) {
        this.pending.delete(id)
        reject(error)
      }
    })
  }

  receiveBackend(chunk: Uint8Array): void {
    for (const message of this.stdout.push(chunk)) {
      if (message.method) {
        if (this.recovering) continue
        if (this.notification(message)) continue
        const frame = encodeRpcFrame(message)
        if (message.method.startsWith('ui/')) {
          if (this.dialogOwner !== null) this.clients.get(this.dialogOwner)?.port.postMessage(frame)
        } else {
          for (const client of this.clients.values()) client.port.postMessage(frame)
        }
        continue
      }
      if (typeof message.id === 'string' && this.heldReplyIds.has(message.id)) this.heldReplies.push(message)
      else this.deliverReply(message)
    }
  }

  private deliverReply(message: RpcMessage): void {
      if (typeof message.id !== 'string') return
      const pending = this.pending.get(message.id)
      if (!pending) return
      this.pending.delete(message.id)
      if (pending.port) {
        if (pending.originalId !== undefined && this.clients.get(pending.owner)?.port === pending.port) {
          pending.port.postMessage(encodeRpcFrame({ ...message, id: pending.originalId }))
        }
      } else if (message.error) {
        pending.reject?.(new Error(`${message.error.message} (${message.error.code})`))
      } else {
        pending.resolve?.(message.result)
      }
  }

  restart(notifyClients = true): void {
    this.stdout = new RpcFrameDecoder()
    this.epoch = 0
    for (const pending of this.pending.values()) pending.reject?.(new Error('Backend restarted.'))
    this.pending.clear()
    this.heldReplyIds.clear()
    this.heldReplies.length = 0
    for (const client of this.clients.values()) {
      client.epoch = 0
      client.flushToken = undefined
      client.decoder = new RpcFrameDecoder()
      if (notifyClients) client.port.postMessage({ novalistControl: 'backend-restarted' })
    }
  }

  beginRecovery(): void {
    this.recovering = true
    this.restart(false)
    this.control('backend-recovering')
  }

  pauseForResync(error: string): void {
    this.recovering = true
    this.holdClientReplies()
    this.rejectHeldReplies(error)
    this.control('backend-recovery-failed', error)
  }

  finishRecovery(): void {
    this.recovering = false
    this.control('backend-restarted')
  }

  failRecovery(error: string): void { this.control('backend-recovery-failed', error) }

  private control(novalistControl: string, error?: string): void {
    for (const client of this.clients.values()) client.port.postMessage({ novalistControl, error })
  }

  dispose(): void {
    for (const pending of this.pending.values()) pending.reject?.(new Error('Backend closed.'))
    this.pending.clear()
    for (const client of this.clients.values()) client.port.close()
    this.clients.clear()
  }
}
