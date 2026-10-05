import assert from 'node:assert/strict'
import test from 'node:test'
import { capturePendingWrites, registerPendingWrite } from '../src/renderer/src/stores/pendingWrites.ts'

test('recovery capture copies live prose without starting or consuming persistence', () => {
  let dirty = '', writes = 0
  const unregister = registerPendingWrite(() => { writes++ }, () => { dirty = 'live pending prose' })
  capturePendingWrites()
  assert.equal(dirty, 'live pending prose')
  assert.equal(writes, 0)
  unregister()
  dirty = ''
  capturePendingWrites()
  assert.equal(dirty, '')
})
