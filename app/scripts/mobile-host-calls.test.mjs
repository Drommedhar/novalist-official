import assert from 'node:assert/strict'
import test from 'node:test'
import { HostCallChannel } from '../src/renderer/src/mobile/hostCalls.ts'

test('mobile host calls resolve the matching reply once', async () => {
  const sent = []
  const calls = new HostCallChannel((message) => sent.push(JSON.parse(message)))
  const first = calls.request('first', [])
  const second = calls.request('second', [])
  calls.receive({ id: sent[1].id, ok: true, result: 'second' })
  calls.receive({ id: sent[0].id, ok: true, result: 'first' })
  calls.receive({ id: sent[0].id, ok: false, error: 'duplicate' })
  assert.deepEqual(await Promise.all([first, second]), ['first', 'second'])
})

test('microphone denial retains the browser permission error contract', async () => {
  let sent
  const calls = new HostCallChannel((message) => { sent = JSON.parse(message) })
  const result = calls.request('microphoneStart', [])
  calls.receive({ id: sent.id, ok: false, error: 'Access denied', errorCode: 'permission-denied' })
  await assert.rejects(result, (error) => error instanceof DOMException && error.name === 'NotAllowedError')
})

test('unavailable native bridge rejects immediately', async () => {
  const calls = new HostCallChannel(() => { throw new Error('unavailable') })
  await assert.rejects(calls.request('save', []), /unavailable/)
})

test('missing acknowledgements time out without replaying a mutation', async () => {
  const sent = []
  const calls = new HostCallChannel((message) => sent.push(JSON.parse(message)))
  await assert.rejects(calls.request('save', [], 5), /result is unknown/)
  calls.receive({ id: sent[0].id, ok: true, result: null })
  assert.equal(sent.length, 1)
})

test('disconnect rejects outstanding calls and late replies stay retired', async () => {
  const sent = []
  const calls = new HostCallChannel((message) => sent.push(JSON.parse(message)))
  const first = assert.rejects(calls.request('first', []), /disconnected/)
  const second = assert.rejects(calls.request('second', []), /disconnected/)
  calls.disconnect(new Error('disconnected'))
  calls.receive({ id: sent[0].id, ok: true })
  await Promise.all([first, second])
  await assert.rejects(calls.request('after-disconnect', []), /disconnected/)
  assert.equal(sent.length, 2)
})
