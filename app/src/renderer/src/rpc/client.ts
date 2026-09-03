/**
 * JSON-RPC 2.0 client over the backend MessagePort. Frames are LSP-style
 * (Content-Length header + UTF-8 JSON body), matching StreamJsonRpc's
 * HeaderDelimitedMessageHandler on the C# side.
 */

type Pending = { resolve: (v: unknown) => void; reject: (e: Error) => void }

export type NotificationHandler = (params: unknown) => void

const encoder = new TextEncoder()
const decoder = new TextDecoder()

export class RpcClient {
  private port: MessagePort | null = null
  private nextId = 1
  private readonly pending = new Map<number, Pending>()
  private readonly notificationHandlers = new Map<string, NotificationHandler>()
  private buffer = new Uint8Array(0)
  private readonly connectListeners = new Set<() => void>()
  private connecting: Promise<void> | null = null
  private resolveConnect: (() => void) | null = null

  constructor() {
    // Main may replace a renderer's channel after a detached pane closes. Keep
    // accepting ports after the initial connect instead of leaving the main
    // window permanently attached to a closed MessagePort.
    window.addEventListener('message', (event) => {
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
   * Idempotent: concurrent or repeat calls share one in-flight request so a
   * second port channel is never opened (React StrictMode invokes the boot
   * effect twice in dev, and main closes the prior backend port whenever a new
   * one is attached — a second request would orphan the first port). Genuine
   * reconnects reset the guard via reconnect().
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
    if (this.port) {
      for (const [, pending] of this.pending) {
        pending.reject(new Error('backend connection changed'))
      }
      this.pending.clear()
      this.port.close()
    }
    this.port = port
    this.buffer = new Uint8Array(0)
    port.onmessage = (event) => this.onPortMessage(event.data)
    for (const listener of this.connectListeners) listener()
  }

  onReconnected(listener: () => void): void {
    this.connectListeners.add(listener)
  }

  onNotification(method: string, handler: NotificationHandler): void {
    this.notificationHandlers.set(method, handler)
  }

  request<T>(method: string, params?: unknown): Promise<T> {
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

  private send(message: object): void {
    const body = encoder.encode(JSON.stringify(message))
    const header = encoder.encode(`Content-Length: ${body.length}\r\n\r\n`)
    const frame = new Uint8Array(header.length + body.length)
    frame.set(header)
    frame.set(body, header.length)
    this.port?.postMessage(frame)
  }

  private onPortMessage(data: unknown): void {
    const control = (data as { novalistControl?: string })?.novalistControl
    if (control === 'backend-restarted') {
      // Main restarted the backend; ask for a fresh port and let stores re-hydrate.
      for (const [, p] of this.pending) p.reject(new Error('backend restarted'))
      this.pending.clear()
      void this.reconnect()
      return
    }
    this.append(data as Uint8Array)
    this.drain()
  }

  private append(chunk: Uint8Array): void {
    const merged = new Uint8Array(this.buffer.length + chunk.length)
    merged.set(this.buffer)
    merged.set(chunk, this.buffer.length)
    this.buffer = merged
  }

  private drain(): void {
    for (;;) {
      const headerEnd = findSequence(this.buffer, [13, 10, 13, 10])
      if (headerEnd < 0) return
      const header = decoder.decode(this.buffer.subarray(0, headerEnd))
      const match = /Content-Length: *(\d+)/i.exec(header)
      if (!match) {
        this.buffer = this.buffer.subarray(headerEnd + 4)
        continue
      }
      const length = Number.parseInt(match[1], 10)
      const bodyStart = headerEnd + 4
      if (this.buffer.length < bodyStart + length) return
      const body = decoder.decode(this.buffer.subarray(bodyStart, bodyStart + length))
      this.buffer = this.buffer.subarray(bodyStart + length)
      this.dispatch(JSON.parse(body))
    }
  }

  private dispatch(message: {
    id?: number
    method?: string
    result?: unknown
    error?: { code: number; message: string }
    params?: unknown
  }): void {
    if (message.id !== undefined && message.method === undefined) {
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

function findSequence(haystack: Uint8Array, needle: number[]): number {
  outer: for (let i = 0; i <= haystack.length - needle.length; i++) {
    for (let j = 0; j < needle.length; j++) {
      if (haystack[i + j] !== needle[j]) continue outer
    }
    return i
  }
  return -1
}

export const rpc = new RpcClient()
