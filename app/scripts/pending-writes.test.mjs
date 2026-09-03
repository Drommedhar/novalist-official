import assert from 'node:assert/strict'
import test from 'node:test'
import {
  flushPendingWrites,
  persistPendingWrite,
  retainPendingWrite
} from '../src/renderer/src/stores/pendingWrites.ts'

const turn = () => new Promise((resolve) => setImmediate(resolve))

test('a newer mounted save supersedes a failed retained payload', async () => {
  const key = 'test:supersedes-stale'
  const attempts = []
  let markAttempted
  const attempted = new Promise((resolve) => {
    markAttempted = resolve
  })

  retainPendingWrite(key, async () => {
    attempts.push('old')
    markAttempted()
    throw new Error('transient failure')
  })
  await attempted
  await turn()

  await persistPendingWrite(key, async () => {
    attempts.push('new')
  })
  await flushPendingWrites()

  assert.deepEqual(attempts, ['old', 'new'])
})

test('a newer same-key write waits for the older in-flight request', async () => {
  const key = 'test:serial-order'
  const order = []
  let releaseOld
  let markStarted
  const oldStarted = new Promise((resolve) => {
    markStarted = resolve
  })
  const oldGate = new Promise((resolve) => {
    releaseOld = resolve
  })

  retainPendingWrite(key, async () => {
    order.push('old:start')
    markStarted()
    await oldGate
    order.push('old:end')
  })
  await oldStarted

  const newer = persistPendingWrite(key, async () => {
    order.push('new')
  })
  await turn()
  assert.deepEqual(order, ['old:start'])

  releaseOld()
  await newer
  await flushPendingWrites()
  assert.deepEqual(order, ['old:start', 'old:end', 'new'])
})
