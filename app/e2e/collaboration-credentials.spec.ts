import { test, expect, _electron as electron } from '@playwright/test'
import { build } from 'esbuild'
import { mkdir, writeFile, readFile, readdir } from 'node:fs/promises'
import { resolve } from 'node:path'

interface CredentialTestEnvironment {
  DesktopCollaborationServices: typeof import('../src/main/collaboration/DesktopCollaborationServices').DesktopCollaborationServices
}

test('collaboration credentials survive restart encrypted by the operating system', async () => {
  const output = resolve('out/collaboration-credential-tests')
  const directory = resolve(output, `credentials-${Date.now()}`)
  await mkdir(output, { recursive: true })
  await build({ entryPoints: ['src/main/collaboration/DesktopCollaborationServices.ts'], outfile: resolve(output, 'store.cjs'),
    bundle: true, packages: 'external', platform: 'node', format: 'cjs' })
  const fixture = resolve(output, 'main.cjs')
  await writeFile(fixture, `const { app } = require('electron'); app.setPath('userData', ${JSON.stringify(resolve(output, 'profile'))});
globalThis.credentialTest = require('./store.cjs'); app.whenReady().then(() => {});
`)
  const env: Record<string, string> = {}
  for (const [key, value] of Object.entries(process.env)) if (value !== undefined && key !== 'ELECTRON_RUN_AS_NODE') env[key] = value
  let application = await electron.launch({ args: [fixture], env })
  const credential = 'test-only-refresh-token-and-private-key'
  try {
    const result = await application.evaluate(async ({ app, safeStorage }, { directory, credential }) => {
      await app.whenReady()
      const { DesktopCollaborationServices } = (globalThis as typeof globalThis & { credentialTest: CredentialTestEnvironment }).credentialTest
      const store = new DesktopCollaborationServices(directory)
      const available = safeStorage.isEncryptionAvailable() && (process.platform !== 'linux' || !['basic_text', 'unknown'].includes(safeStorage.getSelectedStorageBackend()))
      if (!available) {
        let refused = false
        try { await store.request('collaboration/credentials', { action: 'write', key: 'account', value: credential }) } catch { refused = true }
        return { available, refused }
      }
      await store.request('collaboration/credentials', { action: 'write', key: 'account', value: credential })
      return { available }
    }, { directory, credential })
    if (!result.available) { expect(result.refused).toBe(true); return }
    const files = await readdir(directory)
    expect(files).toHaveLength(1)
    expect((await readFile(resolve(directory, files[0]))).includes(Buffer.from(credential))).toBe(false)
    await application.close()
    application = await electron.launch({ args: [fixture], env })
    const restored = await application.evaluate(async ({ app }, directory) => {
      await app.whenReady()
      const { DesktopCollaborationServices } = (globalThis as typeof globalThis & { credentialTest: CredentialTestEnvironment }).credentialTest
      const store = new DesktopCollaborationServices(directory)
      const saved = await store.request('collaboration/credentials', { action: 'read', key: 'account' })
      const missing = await store.request('collaboration/credentials', { action: 'read', key: 'unknown' })
      await store.request('collaboration/credentials', { action: 'remove', key: 'account' })
      if (await store.request('collaboration/credentials', { action: 'read', key: 'account' }) !== null)
        throw new Error('The removed credential is still available.')
      return { saved, missing }
    }, directory)
    expect(restored.saved).toBe(credential)
    expect(restored.missing).toBeNull()
    expect(await readdir(directory)).toEqual([])
  } finally { await application.close() }
})
