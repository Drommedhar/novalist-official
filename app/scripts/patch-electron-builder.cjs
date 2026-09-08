const { readFileSync, writeFileSync } = require('node:fs')

// electron-builder 26.15.3 imports the certificate correctly, but then passes
// the certificate password to set-key-partition-list. macOS 26.6.2 rejects it:
// that command needs the random password used to create the temporary keychain.
// Keep this workaround tied to the pinned version and remove it once an upstream
// release fixes packages/app-builder-lib/src/codeSign/macCodeSign.ts.
const replacements = [
  [
    'return await importCerts(keychainFile, certPaths, cscPasswords);',
    'return await importCerts(keychainFile, certPaths, cscPasswords, keychainPassword);'
  ],
  [
    'async function importCerts(keychainFile, paths, keyPasswords) {',
    'async function importCerts(keychainFile, paths, keyPasswords, keychainPassword) {'
  ],
  [
    '["set-key-partition-list", "-S", "apple-tool:,apple:", "-s", "-k", password, keychainFile]',
    '["set-key-partition-list", "-S", "apple-tool:,apple:", "-s", "-k", keychainPassword, keychainFile]'
  ]
]

function patchMacCodeSign(source) {
  const matches = (text) => source.split(text).length - 1
  if (replacements.every(([before, after]) => matches(before) === 0 && matches(after) === 1)) {
    return source
  }
  if (!replacements.every(([before, after]) => matches(before) === 1 && matches(after) === 0)) {
    throw new Error('Unexpected electron-builder signing code; review the keychain password patch before packaging.')
  }
  return replacements.reduce((patched, [before, after]) => patched.replace(before, after), source)
}

function applyPatch() {
  const { version } = require('app-builder-lib/package.json')
  if (version !== '26.15.3') {
    throw new Error(`Review the keychain password workaround for app-builder-lib ${version}; expected 26.15.3.`)
  }
  const file = require.resolve('app-builder-lib/out/codeSign/macCodeSign.js')
  const source = readFileSync(file, 'utf8')
  const patched = patchMacCodeSign(source)
  if (patched !== source) writeFileSync(file, patched)
  console.log('[electron-builder] temporary keychain password fix applied (26.15.3)')
}

module.exports = { patchMacCodeSign }
if (require.main === module) applyPatch()
