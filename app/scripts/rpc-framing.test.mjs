import assert from 'node:assert/strict'
import test from 'node:test'
import { encodeRpcFrame, RpcFrameDecoder } from '../src/shared/rpcFraming.ts'

test('frames preserve Unicode when every byte arrives separately', () => {
  const message = { jsonrpc: '2.0', id: 1, result: 'Änderung 中文 — prose' }
  const decoder = new RpcFrameDecoder()
  const received = []
  for (const byte of encodeRpcFrame(message)) received.push(...decoder.push(Uint8Array.of(byte)))
  assert.deepEqual(received, [message])
})

test('concatenated frames and a partial successor keep their own boundaries', () => {
  const messages = [1, 2, 3].map(id => ({ jsonrpc: '2.0', id, result: { id } }))
  const bytes = Buffer.concat(messages.map(message => encodeRpcFrame(message)))
  for (let split = 1; split < bytes.length; split++) {
    const decoder = new RpcFrameDecoder()
    assert.deepEqual([...decoder.push(bytes.subarray(0, split)), ...decoder.push(bytes.subarray(split))], messages)
  }
})

test('a parser has no shared partial frame state with another client', () => {
  const first = { jsonrpc: '2.0', id: 1, result: 'first' }
  const second = { jsonrpc: '2.0', id: 1, result: 'second' }
  const bytes = encodeRpcFrame(first)
  const firstDecoder = new RpcFrameDecoder()
  const secondDecoder = new RpcFrameDecoder()
  assert.deepEqual(firstDecoder.push(bytes.subarray(0, 12)), [])
  assert.deepEqual(secondDecoder.push(encodeRpcFrame(second)), [second])
  assert.deepEqual(firstDecoder.push(bytes.subarray(12)), [first])
})

test('invalid and ambiguous lengths fail instead of corrupting a later frame', () => {
  for (const header of [
    'Content-Length: -1',
    'Content-Length: 0',
    'Content-Length: 9007199254740992',
    'Content-Length: 2\r\nContent-Length: 2',
    'Content-Type: application/json'
  ]) {
    assert.throws(() => new RpcFrameDecoder().push(new TextEncoder().encode(`${header}\r\n\r\n{}`)))
  }
  assert.throws(() => new RpcFrameDecoder().push(new Uint8Array(65537).fill(65)))
})

test('invalid JSON, protocol objects and UTF-8 are rejected', () => {
  for (const body of ['not json', '[]', 'null', '{}']) {
    const bytes = new TextEncoder().encode(body)
    const frame = Buffer.concat([Buffer.from(`Content-Length: ${bytes.length}\r\n\r\n`), bytes])
    assert.throws(() => new RpcFrameDecoder().push(frame))
  }
  assert.throws(() => new RpcFrameDecoder().push(Buffer.concat([
    Buffer.from('Content-Length: 1\r\n\r\n'), Buffer.from([255])
  ])))
})
