import assert from 'node:assert/strict'
import { setImmediate } from 'node:timers/promises'
import test from 'node:test'
import { expect } from '@playwright/test'
import { loadRendererSource } from './renderer-source-loader.mjs'

function fixture(release) {
  const window = { novalistRpc: { request: async () => release } }
  const original = window.novalistRpc.request
  let disposed = false
  const page = {
    async evaluateHandle(evaluate) {
      const observed = evaluate()
      return {
        evaluate: async project => project(observed),
        dispose: async () => { disposed = true }
      }
    }
  }
  const { releaseFocusedField } = loadRendererSource('../../e2e/fieldRelease.ts', {
    '@playwright/test': { expect }
  }, { window })
  return { window, original, disposed: () => disposed, run: input => releaseFocusedField(page, input) }
}

test('field-release synchronization waits for the server after local blur makes the input readonly', async t => {
  let acknowledge
  const release = new Promise(resolve => { acknowledge = resolve })
  t.after(() => acknowledge(true))
  const f = fixture(release)
  const input = { readOnly: false, async blur() {
    this.readOnly = true
    void f.window.novalistRpc.request('collaboration/releaseField')
  } }
  let completed = false
  const pending = f.run(input).then(() => { completed = true })
  await setImmediate()
  assert.equal(input.readOnly, true)
  assert.equal(completed, false, 'readonly during saving must not release the next writer')
  acknowledge(true)
  await pending
  assert.equal(completed, true)
  assert.equal(f.window.novalistRpc.request, f.original)
  assert.equal(f.disposed(), true)
})

test('a failed blur restores the original RPC function and disposes its observer', async () => {
  const f = fixture(Promise.resolve(true))
  await assert.rejects(f.run({ blur: async () => { throw new Error('Input detached') } }), /Input detached/)
  assert.equal(f.window.novalistRpc.request, f.original)
  assert.equal(f.disposed(), true)
})
