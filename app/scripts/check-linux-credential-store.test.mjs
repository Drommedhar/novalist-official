import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { runInNewContext } from 'node:vm'
import test from 'node:test'

async function probe({ available = true, backend = 'gnome_libsecret', decrypted, plainCipher = false, imported = false } = {}) {
  const errors = []
  let encrypted = 0
  let finish
  const exited = new Promise(resolve => { finish = resolve })
  const module = { exports: {} }
  const context = {
    module,
    Buffer,
    process: { platform: 'linux' },
    console: { log() {}, error: message => errors.push(message) },
    require(name) {
      assert.equal(name, 'electron')
      return {
        app: { whenReady: async () => {}, exit: finish },
        safeStorage: {
          isEncryptionAvailable: () => available,
          getSelectedStorageBackend: () => backend,
          encryptString(value) { encrypted++; return Buffer.from(plainCipher ? value : value.split('').reverse().join('')) },
          decryptString(value) { return decrypted ?? value.toString().split('').reverse().join('') }
        }
      }
    }
  }
  context.require.main = imported ? {} : module
  runInNewContext(readFileSync(new URL('./check-linux-credential-store.cjs', import.meta.url), 'utf8'), context)
  if (imported) {
    try {
      // Playwright serializes this function into the main-process inspector;
      // that context has standard globals but no CommonJS require binding.
      const result = runInNewContext(`(${module.exports.checkLinuxCredentialStore})(electron)`, {
        Buffer, process: { platform: 'linux' }, electron: context.require('electron')
      })
      assert.equal(result, backend)
      finish(0)
    } catch (error) {
      errors.push(error.message)
      finish(1)
    }
  }
  return { status: await exited, errors, encrypted }
}

test('Linux E2E preflight permits a secure store with a verified encrypted round trip', async () => {
  assert.deepEqual(await probe(), { status: 0, errors: [], encrypted: 1 })
})

for (const options of [{ available: false }, { backend: 'basic_text' }, { backend: 'unknown' }]) {
  test(`Linux E2E preflight refuses insecure storage ${JSON.stringify(options)}`, async () => {
    const result = await probe(options)
    assert.equal(result.status, 1)
    assert.equal(result.encrypted, 0, 'never use an insecure store even for the probe')
    assert.match(result.errors.join('\n'), /secure credential store/i)
  })
}

for (const options of [{ decrypted: 'wrong result' }, { plainCipher: true }]) {
  test(`Linux E2E preflight verifies encryption rather than trusting availability ${JSON.stringify(options)}`, async () => {
    const result = await probe(options)
    assert.equal(result.status, 1)
    assert.match(result.errors.join('\n'), /round trip/i)
  })
}

test('the Playwright-serialized validator verifies the same secure encrypted round trip', async () => {
  assert.deepEqual(await probe({ imported: true }), { status: 0, errors: [], encrypted: 1 })
})

for (const options of [{ available: false }, { backend: 'basic_text' }, { backend: 'unknown' }]) {
  test(`the Playwright-serialized validator fails closed ${JSON.stringify(options)}`, async () => {
    const result = await probe({ ...options, imported: true })
    assert.equal(result.status, 1)
    assert.equal(result.encrypted, 0)
    assert.match(result.errors.join('\n'), /secure credential store/i)
  })
}
