const assert = require('node:assert/strict')
const { readFileSync } = require('node:fs')
const { createRequire } = require('node:module')
const { dirname } = require('node:path')
const test = require('node:test')
const { runInNewContext } = require('node:vm')
const { patchMacCodeSign } = require('./patch-electron-builder.cjs')

const signerFile = require.resolve('app-builder-lib/out/codeSign/macCodeSign.js')
const installedSource = readFileSync(signerFile, 'utf8')
const patchedSource = patchMacCodeSign(installedSource)

// Execute the installed signing implementation with a simulated security tool.
// No Apple credentials, keychains, or macOS host are needed to check that each
// command receives the correct password, including MAS's second certificate.
async function importCertificates(source, passwords) {
  const calls = []
  let keychainPassword
  const signerRequire = createRequire(signerFile)
  const context = {
    exports: {},
    process,
    __dirname: dirname(signerFile),
    require(name) {
      if (name === 'builder-util') {
        return {
          async exec(file, args) {
            assert.equal(file, '/usr/bin/security')
            calls.push(Array.from(args))
            const option = (flag) => args[args.indexOf(flag) + 1]
            if (args[0] === 'create-keychain') keychainPassword = option('-p')
            if (args[0] === 'unlock-keychain') assert.equal(option('-p'), keychainPassword)
            if (args[0] === 'import') assert.equal(option('-P'), passwords[args[1]])
            if (args[0] === 'set-key-partition-list') {
              assert.equal(option('-k'), keychainPassword, 'partition list requires the keychain password')
            }
            return ''
          }
        }
      }
      if (name === './codesign') return { importCertificate: async (link) => link }
      // Bundled root certificate setup is independent of the imported private keys.
      if (name === 'lazy-val') return { Lazy: class { value = Promise.resolve() } }
      return signerRequire(name)
    }
  }
  runInNewContext(source, context, { filename: signerFile })
  const result = await context.exports.createKeychain({
    currentDir: '/novalist/app',
    cscLink: 'application.p12',
    cscKeyPassword: passwords['application.p12'],
    ...(Object.hasOwn(passwords, 'installer.p12') ? {
      cscILink: 'installer.p12',
      cscIKeyPassword: passwords['installer.p12']
    } : {})
  })
  assert.ok(result.keychainFile.endsWith('.keychain'))
  assert.equal(calls.filter(([command]) => command === 'import').length, Object.keys(passwords).length)
  assert.equal(calls.filter(([command]) => command === 'set-key-partition-list').length, Object.keys(passwords).length)
  for (const password of Object.values(passwords)) assert.notEqual(keychainPassword, password)
}

test('Developer ID signing uses the keychain password for key access', async () => {
  await importCertificates(patchedSource, { 'application.p12': 'developer-id-password' })
})

test('MAS imports both certificates with their own passwords and grants access with the keychain password', async () => {
  await importCertificates(patchedSource, {
    'application.p12': 'distribution-password',
    'installer.p12': 'different-installer-password'
  })
})

test('an empty certificate password does not become the keychain password', async () => {
  await importCertificates(patchedSource, { 'application.p12': '' })
})

test('the regression check rejects the original incorrect password', async () => {
  const broken = patchedSource.replace(
    '"-s", "-k", keychainPassword, keychainFile]',
    '"-s", "-k", password, keychainFile]'
  )
  assert.notEqual(broken, patchedSource)
  await assert.rejects(importCertificates(broken, { 'application.p12': 'certificate-password' }),
    /partition list requires the keychain password/)
})

test('the patch is safe to apply again after npm install or npm rebuild', () => {
  assert.equal(patchMacCodeSign(patchedSource), patchedSource)
})

test('changed or partially patched upstream code fails before writing', () => {
  assert.throws(() => patchMacCodeSign(''), /Unexpected electron-builder signing code/)
  assert.throws(() => patchMacCodeSign(patchedSource.replace(
    '"-s", "-k", keychainPassword, keychainFile]',
    '"-s", "-k", password, keychainFile]'
  )), /Unexpected electron-builder signing code/)
})
