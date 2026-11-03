import { createHash, generateKeyPairSync, randomBytes, randomUUID, sign } from 'node:crypto'
import { createServer, type IncomingMessage, type ServerResponse } from 'node:http'
import { createServer as createHttpsServer, type ServerOptions } from 'node:https'
import { once } from 'node:events'

export const introspectionClient = { id: 'novalist-local-relay', secret: randomBytes(32).toString('hex') }

export async function identityProvider(subject: () => string = () => 'writer', displayName: (subject: string) => string = () => 'Test Writer', port = 0, advertiseClientConfiguration = true, tls?: ServerOptions): Promise<{ address: string; disable(subject: string): void; close(): Promise<void> }> {
  const keys = generateKeyPairSync('rsa', { modulusLength: 2048 })
  const publicKey = { ...keys.publicKey.export({ format: 'jwk' }), kid: 'test', alg: 'RS256', use: 'sig' }
  const codes = new Map<string, { challenge: string; redirect: string; nonce: string; subject: string }>()
  const refreshTokens = new Map<string, string>()
  const accessTokens = new Map<string, { sub: string; exp: number }>()
  const disabledAccounts = new Set<string>()
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
    if (!identity || disabledAccounts.has(identity.subject)) {
      response.writeHead(400, { 'Content-Type': 'application/json' })
      response.end(JSON.stringify({ error: 'invalid_grant' }))
      return
    }
    const now = Math.floor(Date.now() / 1000)
    const encoded = (value: unknown): string => Buffer.from(JSON.stringify(value)).toString('base64url')
    const unsigned = encoded({ alg: 'RS256', kid: 'test', typ: 'JWT' }) + '.' +
      encoded({ iss: address, aud: 'novalist', sub: identity.subject, name: displayName(identity.subject), nonce: identity.nonce, iat: now, exp: now + 300 })
    const signature = sign('RSA-SHA256', Buffer.from(unsigned), keys.privateKey).toString('base64url')
    const refresh = randomUUID()
    refreshTokens.set(refresh, identity.subject)
    for (const [issued, value] of accessTokens) if (value.exp <= now) accessTokens.delete(issued)
    accessTokens.set(unsigned + '.' + signature, { sub: identity.subject, exp: now + 300 })
    send(response, { access_token: unsigned + '.' + signature, refresh_token: refresh,
      id_token: unsigned + '.' + signature, token_type: 'Bearer', expires_in: 300 })
  }
  const introspect = async (request: IncomingMessage, response: ServerResponse): Promise<void> => {
    const expected = 'Basic ' + Buffer.from(introspectionClient.id + ':' + introspectionClient.secret).toString('base64')
    if (request.method !== 'POST' || request.headers.authorization !== expected) {
      response.writeHead(401); response.end(); return
    }
    const chunks: Buffer[] = []
    for await (const chunk of request) chunks.push(Buffer.from(chunk))
    const form = new URLSearchParams(Buffer.concat(chunks).toString())
    const issued = accessTokens.get(form.get('token') ?? '')
    const active = issued !== undefined && issued.exp > Math.floor(Date.now() / 1000) && !disabledAccounts.has(issued.sub)
    send(response, active ? { active, ...issued, iss: address, aud: 'novalist' } : { active: false })
  }
  const handle = (request: IncomingMessage, response: ServerResponse): void => {
    const url = new URL(request.url ?? '/', address)
    switch (url.pathname) {
      case '/client-configuration':
        if (!advertiseClientConfiguration) { response.writeHead(404); response.end(); break }
        send(response, { protocolVersion: 1, authority: address, clientId: 'novalist', scope: 'openid profile sync offline_access' }); break
      case '/.well-known/openid-configuration':
        send(response, { issuer: address, authorization_endpoint: address + '/authorize', token_endpoint: address + '/token', introspection_endpoint: address + '/introspect',
          jwks_uri: address + '/keys', response_types_supported: ['code'], subject_types_supported: ['public'],
          id_token_signing_alg_values_supported: ['RS256'], code_challenge_methods_supported: ['S256'] }); break
      case '/keys': send(response, { keys: [publicKey] }); break
      case '/authorize': {
        const redirect = new URL(url.searchParams.get('redirect_uri') ?? 'about:blank')
        if (!(redirect.protocol === 'http:' && redirect.hostname === '127.0.0.1' || redirect.href === 'com.novalist.app:/oauth/callback') || url.searchParams.get('code_challenge_method') !== 'S256') {
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
      case '/introspect':
        void introspect(request, response).catch(() => { response.writeHead(500); response.end() }); break
      default: response.writeHead(404); response.end()
    }
  }
  const server = tls ? createHttpsServer(tls, handle) : createServer(handle)
  server.listen(port, '127.0.0.1')
  await once(server, 'listening')
  const endpoint = server.address()
  if (!endpoint || typeof endpoint === 'string') throw new Error('The test identity provider has no port.')
  address = `${tls ? 'https' : 'http'}://127.0.0.1:${endpoint.port}`
  return { address, disable: subject => { disabledAccounts.add(subject) }, close: () => new Promise<void>((resolve, reject) => {
    server.close(error => error ? reject(error) : resolve())
    server.closeAllConnections()
  }) }
}
