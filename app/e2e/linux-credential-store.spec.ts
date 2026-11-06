import { expect, test } from '@playwright/test'
import { launchApp } from './harness'

// Pass the self-contained function to Playwright for serialization: require is
// available in this test process, but not in Electron's inspector evaluation.
const { checkLinuxCredentialStore } = require('../scripts/check-linux-credential-store.cjs') as {
  checkLinuxCredentialStore(electron: typeof import('electron')): string
}

test('Playwright launches the real Linux app with secure credential storage', async () => {
  test.skip(process.platform !== 'linux', 'Linux uses the isolated CI keyring.')
  const h = await launchApp('novalist-linux-keyring-')
  try {
    const backend = await h.app.evaluate(checkLinuxCredentialStore)
    expect(await h.app.evaluate(({ app }) => app.commandLine.getSwitchValue('password-store'))).toBe('')
    console.log(`Playwright Linux credential store ready: ${backend}`)
  } finally {
    await h.close()
  }
})
