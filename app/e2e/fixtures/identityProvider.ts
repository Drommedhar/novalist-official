import { createHash, generateKeyPairSync, randomUUID, sign } from 'node:crypto'
import { createServer, type IncomingMessage, type ServerResponse } from 'node:http'
import { once } from 'node:events'

export async function identityProvider(subject: () => string = () => 'writer', displayName: (subject: string) => string = () => 'Test Writer'): Promise<{ address: string; close(): Promise<void> }> {
  const keys = generateKeyPairSync('rsa', { modulusLength: 2048 })
  const publicKey = { ...keys.publicKey.export({ format: 'jwk' }), kid: 'test', alg: 'RS256', use: 'sig' }
  const codes = new Map<string, { challenge: string; redirect: string; nonce: string; subject: string }>()
  const refreshTokens = new Map<string, string>()
  let address = ''
  const send = (response: ServerResponse, value: unknown): void => {
    response.writeHead(200, { 'Content-Type': 'application/json' })
    response.end(JSON.stringify(value))
  }
  const token = async (request: IncomingMessage, response: ServerResponse): Promise<void> => {
    const chunks: Buffer[] = []
    for await (const chunk of request) chunks.push(Buffer.from(chunk))
    const form = new URLSearchParams(Buffer.concat(chunks).toString())
    let identity: { subject: string; nonce?: string } | undefined
    if (form.get('grant_type') === 'refresh_token') {
      const refresh = form.get('refresh_token') ?? ''
      const remembered = refreshTokens.get(refresh)
      refreshTokens.delete(refresh)
      if (remembered && form.get('client_id') === 'novalist') identity = { subject: remembered }
    } else {
      const code = form.get('code') ?? ''
      const pending = codes.get(code)
      codes.delete(code)
      if (pending && form.get('client_id') === 'novalist' && form.get('redirect_uri') === pending.redirect &&
        createHash('sha256').update(form.get('code_verifier') ?? '').digest('base64url') === pending.challenge) identity = pending
    }
    if (!identity) { response.writeHead(400); response.end(); return }
    const now = Math.floor(Date.now() / 1000)
    const encoded = (value: unknown): string => Buffer.from(JSON.stringify(value)).toString('base64url')
    const unsigned = encoded({ alg: 'RS256', kid: 'test', typ: 'JWT' }) + '.' +
      encoded({ iss: address, aud: 'novalist', sub: identity.subject, name: displayName(identity.subject), nonce: identity.nonce, iat: now, exp: now + 300 })
    const signature = sign('RSA-SHA256', Buffer.from(unsigned), keys.privateKey).toString('base64url')
    const refresh = randomUUID()
    refreshTokens.set(refresh, identity.subject)
    send(response, { access_token: unsigned + '.' + signature, refresh_token: refresh,
      id_token: unsigned + '.' + signature, token_type: 'Bearer', expires_in: 300 })
  }
  const server = createServer((request, response) => {
    const url = new URL(request.url ?? '/', address)
    switch (url.pathname) {
      case '/client-configuration':
        send(response, { protocolVersion: 1, authority: address, clientId: 'novalist', scope: 'openid profile sync offline_access' }); break
      case '/.well-known/openid-configuration':
        send(response, { issuer: address, authorization_endpoint: address + '/authorize', token_endpoint: address + '/token',
          jwks_uri: address + '/keys', response_types_supported: ['code'], subject_types_supported: ['public'],
          id_token_signing_alg_values_supported: ['RS256'], code_challenge_methods_supported: ['S256'] }); break
      case '/keys': send(response, { keys: [publicKey] }); break
      case '/authorize': {
        const redirect = new URL(url.searchParams.get('redirect_uri') ?? 'about:blank')
        if (redirect.hostname !== '127.0.0.1' || url.searchParams.get('code_challenge_method') !== 'S256') {
          response.writeHead(400); response.end(); break
        }
        const code = randomUUID()
        codes.set(code, { challenge: url.searchParams.get('code_challenge') ?? '', redirect: redirect.href, nonce: url.searchParams.get('nonce') ?? '', subject: subject() })
        redirect.searchParams.set('code', code)
        redirect.searchParams.set('state', url.searchParams.get('state') ?? '')
        response.writeHead(302, { Location: redirect.href }); response.end(); break
      }
      case '/token':
        void token(request, response).catch(() => { response.writeHead(500); response.end() }); break
      default: response.writeHead(404); response.end()
    }
  })
  server.listen(0, '127.0.0.1')
  await once(server, 'listening')
  const endpoint = server.address()
  if (!endpoint || typeof endpoint === 'string') throw new Error('The test identity provider has no port.')
  address = `http://127.0.0.1:${endpoint.port}`
  return { address, close: () => new Promise<void>((resolve, reject) => {
    server.close(error => error ? reject(error) : resolve())
    server.closeAllConnections()
  }) }
}
