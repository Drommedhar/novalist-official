import { createHash, randomBytes } from 'node:crypto'

// Supplies a test-provider account to the simulator's Keychain. Browser UI is
// validated separately; this smoke test covers the real relay and native host.
export async function mobileSmokeAccount(server: string, authority: string): Promise<unknown> {
  const verifier = randomBytes(32).toString('base64url')
  const nonce = randomBytes(32).toString('hex')
  const redirect = 'com.novalist.app:/oauth/callback'
  const query = new URLSearchParams({ client_id: 'novalist', redirect_uri: redirect,
    response_type: 'code', scope: 'openid profile sync offline_access', nonce, state: nonce,
    code_challenge: createHash('sha256').update(verifier).digest('base64url'), code_challenge_method: 'S256' })
  const authorization = await fetch(authority + '/authorize?' + query, { redirect: 'manual' })
  const callback = new URL(authorization.headers.get('location')!)
  const response = await fetch(authority + '/token', { method: 'POST', body: new URLSearchParams({
    grant_type: 'authorization_code', client_id: 'novalist', redirect_uri: redirect,
    code: callback.searchParams.get('code')!, code_verifier: verifier
  }) })
  if (!response.ok) throw new Error('The test identity provider did not issue mobile credentials.')
  const token = await response.json() as { refresh_token: string }
  return { action: 'write', key: 'hosted-account:' + server, value: JSON.stringify({ Version: 1,
    Settings: { Authority: authority, ClientId: 'novalist', Scope: 'openid profile sync offline_access' },
    Subject: 'mobile', DisplayName: 'Mobile writer', Nonce: nonce, RefreshToken: token.refresh_token }) }
}
