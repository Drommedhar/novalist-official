// Run in Electron inside the isolated Linux E2E D-Bus/keyring session.
const { app, safeStorage } = require('electron')

app.whenReady().then(() => {
  if (process.platform !== 'linux') throw new Error('The Linux credential preflight requires Linux.')
  const available = safeStorage.isEncryptionAvailable()
  const backend = safeStorage.getSelectedStorageBackend()
  if (!available || ['basic_text', 'unknown'].includes(backend)) {
    throw new Error(`A secure credential store is required for Linux E2E (backend: ${backend}, available: ${available}).`)
  }
  const plain = 'novalist-ci-credential-round-trip'
  const encrypted = safeStorage.encryptString(plain)
  if (encrypted.includes(Buffer.from(plain)) || safeStorage.decryptString(encrypted) !== plain) {
    throw new Error('The secure credential store failed its encrypted round trip.')
  }
  console.log(`Linux E2E secure credential store ready: ${backend}`)
  app.exit(0)
}).catch(error => {
  console.error(error instanceof Error ? error.message : String(error))
  app.exit(1)
})
