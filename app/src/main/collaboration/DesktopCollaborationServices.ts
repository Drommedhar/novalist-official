import { shell } from 'electron'
import { CredentialStore } from './CredentialStore'
import { DesktopDocumentBridge } from './DocumentHostProcess'

export class DesktopCollaborationServices {
  private readonly documents = new DesktopDocumentBridge()
  private readonly credentials: CredentialStore

  constructor(directory: string) { this.credentials = new CredentialStore(directory) }

  async request(method: string, params: unknown): Promise<unknown> {
    if (method === 'collaboration/documents') return this.documents.handle(method, params)
    if (!params || typeof params !== 'object' || Array.isArray(params)) throw new Error('Invalid collaboration operation.')
    if (method === 'collaboration/openBrowser') {
      if (!('url' in params) || typeof params.url !== 'string' || params.url.length > 16_384)
        throw new Error('Invalid sign-in address.')
      const url = new URL(params.url)
      if ((url.protocol !== 'https:' && !(url.protocol === 'http:' && ['127.0.0.1', '[::1]', 'localhost'].includes(url.hostname))) ||
        url.username || url.password || url.hash) throw new Error('Use an HTTPS sign-in address.')
      await shell.openExternal(url.href)
      return null
    }
    if (method !== 'collaboration/credentials' || !('key' in params) || typeof params.key !== 'string' || !('action' in params))
      throw new Error('Unknown collaboration operation.')
    switch (params.action) {
      case 'read': return this.credentials.read(params.key)
      case 'write':
        if (!('value' in params) || typeof params.value !== 'string') throw new Error('Invalid collaboration credential.')
        await this.credentials.write(params.key, params.value)
        return null
      case 'remove':
        await this.credentials.remove(params.key)
        return null
      default: throw new Error('Unknown credential operation.')
    }
  }

  async reset(): Promise<void> { await this.documents.reset() }
}
