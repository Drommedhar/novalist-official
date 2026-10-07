type PendingCall = {
  resolve: (value: unknown) => void
  reject: (error: Error) => void
  timer: ReturnType<typeof setTimeout>
}

type HostReply = { id: number; ok: boolean; result?: unknown; error?: string; errorCode?: string }

export class HostCallChannel {
  private readonly pending = new Map<number, PendingCall>()
  private nextId = 1
  private failure: Error | null = null
  private readonly send: (message: string) => void

  constructor(send: (message: string) => void) { this.send = send }

  request<T>(method: string, args: unknown[], timeoutMs = 30_000): Promise<T> {
    if (this.failure) return Promise.reject(this.failure)
    const id = this.nextId++
    return new Promise<T>((resolve, reject) => {
      const timer = setTimeout(() => {
        this.pending.delete(id)
        reject(new Error(`The mobile host did not acknowledge ${method}. Its result is unknown; reopen the app before retrying a write.`))
      }, timeoutMs)
      this.pending.set(id, { resolve: resolve as (value: unknown) => void, reject, timer })
      try { this.send(JSON.stringify({ id, method, args })) }
      catch (error) {
        clearTimeout(timer)
        this.pending.delete(id)
        reject(error instanceof Error ? error : new Error(String(error)))
      }
    })
  }

  receive(reply: HostReply): void {
    const pending = this.pending.get(reply.id)
    if (!pending) return
    this.pending.delete(reply.id)
    clearTimeout(pending.timer)
    if (reply.ok) pending.resolve(reply.result)
    else if (reply.errorCode === 'permission-denied')
      pending.reject(new DOMException(reply.error ?? 'Permission denied.', 'NotAllowedError'))
    else pending.reject(new Error(reply.error ?? 'Mobile host call failed.'))
  }

  disconnect(error: Error): void {
    this.failure = error
    for (const pending of this.pending.values()) {
      clearTimeout(pending.timer)
      pending.reject(error)
    }
    this.pending.clear()
  }
}

// Audit M05 renderer-only Apple CI path-trigger probe.
