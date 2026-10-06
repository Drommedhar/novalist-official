import assert from 'node:assert/strict'
import { register } from 'node:module'
import { test } from 'node:test'

register('./map-module-loader.mjs', import.meta.url)

const mapRoot = new URL('../src/renderer/public/map/', import.meta.url)
const {
  createDefaultContainer, write, KHR_SUPERCOMPRESSION_ZSTD,
  VK_FORMAT_R8G8B8A8_UNORM, VK_FORMAT_R32_SFLOAT
} = await import(new URL('libs/ktx-parse.module.js', mapRoot))
const { ZSTDDecoder } = await import(new URL('libs/zstddec.module.js', mapRoot))
const { FloatType, RedFormat, RGBAFormat, UnsignedByteType } = await import(new URL('three.webgpu.min.js', mapRoot))

async function createLoader(name) {
  const { KTX2Loader } = await import(new URL('KTX2Loader.js?case=' + name, mapRoot))
  return new KTX2Loader().detectSupport({ isWebGPURenderer: true, hasFeature: () => false })
}

function textureBuffer({ compressed = false, floating = false } = {}) {
  const container = createDefaultContainer()
  container.vkFormat = floating ? VK_FORMAT_R32_SFLOAT : VK_FORMAT_R8G8B8A8_UNORM
  container.typeSize = floating ? 4 : 1
  container.pixelWidth = 1
  container.pixelHeight = 1
  container.levelCount = 1
  let levelData = floating ? new Uint8Array(new Float32Array([0.25]).buffer) : new Uint8Array([1, 2, 3, 4])
  if (compressed) {
    container.supercompressionScheme = KHR_SUPERCOMPRESSION_ZSTD
    // Single-segment Zstd frame containing one final raw block of four RGBA bytes.
    levelData = new Uint8Array([0x28, 0xb5, 0x2f, 0xfd, 0x20, 4, 0x21, 0, 0, 1, 2, 3, 4])
  }
  container.levels = [{ levelData, uncompressedByteLength: 4 }]
  const data = write(container)
  return data.buffer.slice(data.byteOffset, data.byteOffset + data.byteLength)
}

function parseTexture(loader, buffer) {
  return new Promise((resolve, reject) => loader.parse(buffer, resolve, reject))
}

test('Zstd initialization rejection reaches every current and subsequent parse callback', { timeout: 2000 }, async (t) => {
  const failure = new Error('WASM initialization failed')
  const init = t.mock.method(ZSTDDecoder.prototype, 'init', () => Promise.reject(failure))
  const loader = await createLoader('rejection')
  await Promise.all([1, 2].map(() => assert.rejects(parseTexture(loader, textureBuffer({ compressed: true })), failure)))
  await assert.rejects(parseTexture(loader, textureBuffer({ compressed: true })), failure)
  assert.equal(init.mock.callCount(), 1)
})

test('a synchronous decoder initialization failure reaches the parse error callback', { timeout: 2000 }, async (t) => {
  const failure = new Error('WASM initialization unavailable')
  t.mock.method(ZSTDDecoder.prototype, 'init', () => { throw failure })
  const loader = await createLoader('synchronous-failure')
  await assert.rejects(parseTexture(loader, textureBuffer({ compressed: true })), failure)
})

test('concurrent Zstd textures share initialization and decode using the bundled WASM', async (t) => {
  const originalInit = ZSTDDecoder.prototype.init
  const init = t.mock.method(ZSTDDecoder.prototype, 'init', function () { return originalInit.call(this) })
  const loader = await createLoader('success')
  const textures = await Promise.all([1, 2].map(() => parseTexture(loader, textureBuffer({ compressed: true }))))
  assert.equal(init.mock.callCount(), 1)
  for (const texture of textures) {
    assert.equal(texture.isDataTexture, true)
    assert.equal(texture.type, UnsignedByteType)
    assert.equal(texture.format, RGBAFormat)
    assert.deepEqual([...texture.image.data], [1, 2, 3, 4])
    assert.equal(texture.image.width, 1)
    assert.equal(texture.image.height, 1)
    texture.dispose()
  }
})

test('uncompressed floating-point textures retain their type and pixel values', async () => {
  const loader = await createLoader('float')
  const texture = await parseTexture(loader, textureBuffer({ floating: true }))
  assert.equal(texture.type, FloatType)
  assert.equal(texture.format, RedFormat)
  assert.ok(texture.image.data instanceof Float32Array)
  assert.deepEqual([...texture.image.data], [0.25])
  texture.dispose()
})

test('an unsupported Vulkan format reaches the parse error callback before creating a texture', async () => {
  const loader = await createLoader('unsupported-format')
  const buffer = textureBuffer()
  new DataView(buffer).setUint32(12, 0xffffffff, true)
  await assert.rejects(parseTexture(loader, buffer), /Unsupported vkFormat: 4294967295/)
})
