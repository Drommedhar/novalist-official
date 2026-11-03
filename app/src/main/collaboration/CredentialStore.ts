import { safeStorage } from 'electron'
import { createHash, randomUUID } from 'node:crypto'
import { mkdir, open, readFile, rename, rm } from 'node:fs/promises'
import { join } from 'node:path'

const maxCredentialBytes = 256 * 1024

export class CredentialStore {
  constructor(private readonly directory: string) {}

  async read(key: string): Promise<string | null> {
    const path = this.path(key)
    let encrypted: Buffer
    try { encrypted = await readFile(path) }
    catch (error) {
      if (missing(error)) return null
      throw error
    }
    requireEncryption()
    if (encrypted.length > maxCredentialBytes * 2) throw new Error('The saved credential is too large.')
    const payload: unknown = JSON.parse(safeStorage.decryptString(encrypted))
    if (!payload || typeof payload !== 'object' || !('version' in payload) || payload.version !== 1 ||
      !('key' in payload) || payload.key !== key || !('value' in payload) || typeof payload.value !== 'string')
      throw new Error('The saved credential does not match this account.')
    return payload.value
  }

  async write(key: string, value: string): Promise<void> {
    const path = this.path(key)
    requireEncryption()
    if (Buffer.byteLength(value) > maxCredentialBytes) throw new Error('The credential is too large.')
    const encrypted = safeStorage.encryptString(JSON.stringify({ version: 1, key, value }))
    await mkdir(this.directory, { recursive: true, mode: 0o700 })
    const temporary = path + '.' + randomUUID() + '.tmp'
    try {
      const file = await open(temporary, 'wx', 0o600)
      try { await file.writeFile(encrypted); await file.sync() }
      finally { await file.close() }
      await rename(temporary, path)
    } finally { await rm(temporary, { force: true }) }
  }

  async remove(key: string): Promise<void> { await rm(this.path(key), { force: true }) }

  private path(key: string): string {
    if (!key.length || key.length > 4096) throw new Error('Invalid credential identity.')
    return join(this.directory, createHash('sha256').update(key).digest('hex') + '.secret')
  }
}

function requireEncryption(): void {
  if (!safeStorage.isEncryptionAvailable() || (process.platform === 'linux' &&
    ['basic_text', 'unknown'].includes(safeStorage.getSelectedStorageBackend())))
    throw new Error('The operating system credential store is unavailable.')
}

function missing(error: unknown): boolean {
  return typeof error === 'object' && error !== null && 'code' in error && error.code === 'ENOENT'
}
