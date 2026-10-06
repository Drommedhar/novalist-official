import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { createRequire } from 'node:module'
import { test } from 'node:test'
import { fileURLToPath } from 'node:url'
import { runInNewContext } from 'node:vm'

const mapRoot = new URL('../src/renderer/public/map/', import.meta.url)
const pageSize = 65536
const heapLimit = 2147483648

function functionSource(codec) {
  const file = codec === 'Basis' ? 'basis/basis_transcoder.js' : 'draco/draco_wasm_wrapper.js'
  const source = readFileSync(new URL(file, mapRoot), 'utf8')
  const startMarker = codec === 'Basis' ? 'var getHeapMax=' : 'e:function(e){var b=ea.length'
  const endMarker = codec === 'Basis' ? 'var _fd_close=' : ',f:function(e){return 52}'
  const start = source.indexOf(startMarker)
  const end = source.indexOf(endMarker, start)
  assert.ok(start >= 0 && end > start, 'The bundled allocator must be found in its codec module')
  return codec === 'Basis'
    ? source.slice(start, end) + ';_emscripten_resize_heap'
    : '(' + source.slice(start + 2, end) + ')'
}

function createAllocator(codec, initialBytes, failures) {
  const attempts = []
  const memory = {
    buffer: { byteLength: initialBytes },
    grow(pages) {
      const committedPages = Math.trunc(pages)
      attempts.push(committedPages)
      if (attempts.length <= failures) throw new RangeError('Allocation refused')
      const previousPages = this.buffer.byteLength / pageSize
      this.buffer = { byteLength: this.buffer.byteLength + committedPages * pageSize }
      return previousPages
    }
  }
  const heap = { length: initialBytes }
  let refreshes = 0
  const refresh = () => { heap.length = memory.buffer.byteLength; refreshes++ }
  const resize = runInNewContext(functionSource(codec), {
    wasmMemory: memory, HEAPU8: heap, updateMemoryViews: refresh,
    ja: memory, ea: heap, A: refresh
  })
  return { resize, attempts, memory, refreshes: () => refreshes }
}

for (const codec of ['Basis', 'Draco']) {
  for (const failures of [0, 1, 2]) {
    test(`${codec} grows memory after ${failures} refused reservations`, () => {
      const allocator = createAllocator(codec, 100 * pageSize, failures)
      const requested = 101 * pageSize
      assert.equal(allocator.resize(requested), true)
      assert.equal(allocator.attempts.length, failures + 1)
      assert.ok(allocator.memory.buffer.byteLength >= requested)
      assert.equal(allocator.refreshes(), 1)
      for (let i = 1; i < allocator.attempts.length; i++) {
        assert.ok(allocator.attempts[i] < allocator.attempts[i - 1], 'Retries request smaller reservations')
      }
    })
  }

  test(`${codec} reports failure without refreshing heap views when every growth attempt fails`, () => {
    const initial = 100 * pageSize
    const allocator = createAllocator(codec, initial, Infinity)
    assert.equal(allocator.resize(101 * pageSize), false)
    assert.equal(allocator.attempts.length, 3)
    assert.equal(allocator.memory.buffer.byteLength, initial)
    assert.equal(allocator.refreshes(), 0)
  })

  test(`${codec} rejects requests beyond the heap limit before attempting growth`, () => {
    const allocator = createAllocator(codec, pageSize, 0)
    assert.equal(allocator.resize(heapLimit + 1), false)
    assert.deepEqual(allocator.attempts, [])
    assert.equal(allocator.refreshes(), 0)
  })

  test(`${codec} permits growth up to the exact heap limit`, () => {
    const allocator = createAllocator(codec, heapLimit - pageSize, 0)
    assert.equal(allocator.resize(heapLimit), true)
    assert.deepEqual(allocator.attempts, [1])
    assert.equal(allocator.memory.buffer.byteLength, heapLimit)
  })
}

test('bundled Basis handles recognize aliases without writing the read-only clone record', async () => {
  const require = createRequire(import.meta.url)
  const createBasis = require(fileURLToPath(new URL('basis/basis_transcoder.js', mapRoot)))
  const basis = await createBasis({ wasmBinary: readFileSync(new URL('basis/basis_transcoder.wasm', mapRoot)) })
  basis.initializeBasis()
  const first = new basis.KTX2File(new Uint8Array())
  const clone = first.clone()
  const separate = new basis.KTX2File(new Uint8Array())
  try {
    assert.equal(Object.getOwnPropertyDescriptor(clone, '$$').writable, false)
    assert.equal(first.isAliasOf(clone), true)
    assert.equal(clone.isAliasOf(first), true)
    assert.equal(first.isAliasOf(separate), false)
    assert.equal(first.isAliasOf({}), false)
  } finally {
    clone.delete()
    first.delete()
    separate.delete()
  }
})

test('Basis preserves its exported dynCall wrapper and replaces it with the instantiated function after the first call', async () => {
  const require = createRequire(import.meta.url)
  const createBasis = require(fileURLToPath(new URL('basis/basis_transcoder.js', mapRoot)))
  const wasm = new WebAssembly.Module(readFileSync(new URL('basis/basis_transcoder.wasm', mapRoot)))
  const calls = []
  function exportedDynCall(a0, a1, a2, a3, a4) {
    calls.push([a0, a1, a2, a3, a4])
    return 17
  }
  const basis = await createBasis({
    instantiateWasm(imports, receiveInstance) {
      const instance = new WebAssembly.Instance(wasm, imports)
      const exports = { ...instance.exports, S: exportedDynCall }
      receiveInstance({ exports })
      return exports
    }
  })
  const initialWrapper = basis.dynCall_jiji
  assert.equal(initialWrapper.length, 5)
  assert.notEqual(initialWrapper, exportedDynCall)
  assert.equal(initialWrapper(1, 2, 3, 4, 5), 17)
  assert.equal(basis.dynCall_jiji, exportedDynCall)
  assert.equal(basis.dynCall_jiji(6, 7, 8, 9, 10), 17)
  assert.deepEqual(calls, [[1, 2, 3, 4, 5], [6, 7, 8, 9, 10]])
})
