/** JSON-RPC messages shared by the desktop broker and browser client. */
export type RpcId = string | number | null

export interface RpcMessage {
  jsonrpc: '2.0'
  id?: RpcId
  method?: string
  params?: unknown
  result?: unknown
  error?: { code: number; message: string; data?: unknown }
}

const encoder = new TextEncoder()
const decoder = new TextDecoder('utf-8', { fatal: true })
const MAX_HEADER_BYTES = 65536

export function encodeRpcFrame(message: RpcMessage): Uint8Array {
  const body = encoder.encode(JSON.stringify(message))
  const header = encoder.encode(`Content-Length: ${body.length}\r\n\r\n`)
  const frame = new Uint8Array(header.length + body.length)
  frame.set(header)
  frame.set(body, header.length)
  return frame
}

/**
 * Parses complete LSP frames independently of stream chunk boundaries.
 * Body chunks are collected once rather than repeatedly copying a growing
 * manuscript every time stdout emits another fragment.
 */
export class RpcFrameDecoder {
  private header: number[] = []
  private length: number | null = null
  private received = 0
  private parts: Uint8Array[] = []

  push(chunk: Uint8Array): RpcMessage[] {
    const messages: RpcMessage[] = []
    let offset = 0
    while (offset < chunk.length) {
      if (this.length === null) {
        this.header.push(chunk[offset++])
        if (this.header.length > MAX_HEADER_BYTES) throw new Error('RPC header is too large.')
        const end = this.header.length
        if (end < 4 || this.header[end - 4] !== 13 || this.header[end - 3] !== 10 ||
          this.header[end - 2] !== 13 || this.header[end - 1] !== 10) continue
        const lines = decoder.decode(new Uint8Array(this.header)).split('\r\n')
        const lengths = lines.filter((line) => /^Content-Length:/i.test(line))
        const match = lengths.length === 1 && /^Content-Length:\s*(\d+)\s*$/i.exec(lengths[0])
        const length = match ? Number(match[1]) : Number.NaN
        if (!Number.isSafeInteger(length) || length <= 0) throw new Error('Invalid RPC content length.')
        this.length = length
        this.header = []
      }
      const count = Math.min(this.length - this.received, chunk.length - offset)
      if (count > 0) {
        this.parts.push(chunk.subarray(offset, offset + count))
        this.received += count
        offset += count
      }
      if (this.received !== this.length) continue
      const body = new Uint8Array(this.length)
      let bodyOffset = 0
      for (const part of this.parts) {
        body.set(part, bodyOffset)
        bodyOffset += part.length
      }
      this.length = null
      this.received = 0
      this.parts = []
      const message: unknown = JSON.parse(decoder.decode(body))
      if (!message || typeof message !== 'object' || Array.isArray(message) ||
        (message as RpcMessage).jsonrpc !== '2.0') throw new Error('Invalid RPC message.')
      messages.push(message as RpcMessage)
    }
    return messages
  }
}
