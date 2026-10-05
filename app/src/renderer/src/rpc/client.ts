import { encodeRpcFrame, RpcFrameDecoder, type RpcMessage } from '../../../shared/rpcFraming'

/**
 * JSON-RPC 2.0 client over the backend MessagePort. Frames are LSP-style
 * (Content-Length header + UTF-8 JSON body), matching StreamJsonRpc's
 * HeaderDelimitedMessageHandler on the C# side.
 */

type Pending = { resolve: (v: unknown) => void; reject: (e: Error) => void }

export type NotificationHandler = (params: unknown) => void


export class RpcClient {
  private port: MessagePort | null = null
  private nextId = 1
  private readonly pending = new Map<number, Pending>()
  private readonly notificationHandlers = new Map<string, NotificationHandler>()
  private frames = new RpcFrameDecoder()
  private readonly connectListeners = new Set<() => void>()
  private readonly recoveryListeners = new Set<(error?: string) => void>()
  private connecting: Promise<void> | null = null
  private resolveConnect: (() => void) | null = null

  constructor() {
    // Reconnects replace only this window's channel. Ports from another frame
    // cannot replace the renderer's authenticated desktop connection.
    window.addEventListener('message', (event) => {
      if (event.source !== window) return
      if ((event.data as { novalist?: string })?.novalist !== 'backend-port') return
      const port = event.ports[0]
      if (!port) return
      this.attach(port)
      this.resolveConnect?.()
      this.resolveConnect = null
    })
  }

  /**
   * Requests a backend port from main and resolves once frames can flow.
   * Idempotent: concurrent boot effects share one request. A backend restart
   * explicitly resets the guard before asking for a replacement channel.
   */
  connect(): Promise<void> {
    if (this.connecting) return this.connecting
    this.connecting = new Promise((resolve) => {
      this.resolveConnect = resolve
      window.novalist.requestBackendPort()
    })
    return this.connecting
  }

  /** Forces a fresh port request (used after the backend restarts). */
  private reconnect(): Promise<void> {
    this.connecting = null
    return this.connect()
  }

  attach(port: MessagePort): void {
    const reconnecting = this.port !== null
    if (this.port) {
      for (const [, pending] of this.pending) {
        pending.reject(new Error('backend connection changed'))
      }
      this.pending.clear()
      this.port.close()
    }
    this.port = port
    this.frames = new RpcFrameDecoder()
    port.onmessage = (event) => this.onPortMessage(event.data)
    // connect().then(hydrate) owns initial startup. Calling reconnect listeners
    // on the first port duplicated settings, assets and library initialization.
    if (reconnecting) for (const listener of this.connectListeners) listener()
  }

  onReconnected(listener: () => void): () => void {
    this.connectListeners.add(listener)
    return () => this.connectListeners.delete(listener)
  }

  onRecovery(listener: (error?: string) => void): () => void {
    this.recoveryListeners.add(listener)
    return () => this.recoveryListeners.delete(listener)
  }

  onNotification(method: string, handler: NotificationHandler): void {
    this.notificationHandlers.set(method, handler)
  }

  request<T>(method: string, params?: unknown): Promise<T> {
    if (!this.port) return this.connect().then(() => this.request<T>(method, params))
    const id = this.nextId++
    const promise = new Promise<T>((resolve, reject) => {
      this.pending.set(id, { resolve: resolve as (v: unknown) => void, reject })
    })
    this.send({ jsonrpc: '2.0', id, method, params })
    return promise
  }

  notify(method: string, params?: unknown): void {
    this.send({ jsonrpc: '2.0', method, params })
  }

  private send(message: RpcMessage): void {
    this.port?.postMessage(encodeRpcFrame(message))
  }

  private onPortMessage(data: unknown): void {
    const control = (data as { novalistControl?: string })?.novalistControl
    if (control === 'backend-recovering' || control === 'backend-recovery-failed') {
      for (const pending of this.pending.values()) pending.reject(new Error('Backend restarted.'))
      this.pending.clear()
      for (const listener of this.recoveryListeners) listener((data as { error?: string }).error)
      return
    }
    if (control === 'backend-restarted' || control === 'backend-protocol-error') {
      for (const pending of this.pending.values()) pending.reject(new Error('Backend connection changed.'))
      this.pending.clear()
      void this.reconnect()
      return
    }
    try {
      for (const message of this.frames.push(data as Uint8Array)) this.dispatch(message)
    } catch {
      for (const pending of this.pending.values()) pending.reject(new Error('Invalid backend response.'))
      this.pending.clear()
      void this.reconnect()
    }
  }

  private dispatch(message: RpcMessage): void {
    if (typeof message.id === 'number' && message.method === undefined) {
      const pending = this.pending.get(message.id)
      if (!pending) return
      this.pending.delete(message.id)
      if (message.error) {
        pending.reject(new Error(`${message.error.message} (${message.error.code})`))
      } else {
        pending.resolve(message.result)
      }
      return
    }
    if (message.method) {
      this.notificationHandlers.get(message.method)?.(message.params)
    }
  }

}

export const rpc = new RpcClient()
