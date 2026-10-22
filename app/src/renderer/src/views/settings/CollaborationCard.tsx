import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { rpc } from '../../rpc/client'
import { SharedProjectCard } from './SharedProjectCard'
import { useHostedSessionStore } from '../../collaboration/hostedSessionStore'

interface Account { subject: string; displayName: string }
const serverPreference = 'novalist.collaboration.server'

export function CollaborationCard(): React.JSX.Element {
  const { t } = useTranslation()
  const [server, setServer] = useState(() => localStorage.getItem(serverPreference) ?? '')
  const [account, setAccount] = useState<Account | null>(null)
  const [busy, setBusy] = useState<'checking' | 'signIn' | 'signOut' | null>(null)
  const [error, setError] = useState('')
  const sharing = useHostedSessionStore(state => state.session !== null || state.busy !== null)
  const savedServer = useHostedSessionStore(state => state.saved?.server)

  useEffect(() => {
    const saved = localStorage.getItem(serverPreference) || savedServer
    if (!saved) return
    setServer(saved)
    let current = true
    setBusy('checking')
    void rpc.request<Account | null>('collaboration/account/status', [saved])
      .then(value => { if (current) setAccount(value) })
      .catch(reason => { if (current) setError(String(reason)) })
      .finally(() => { if (current) setBusy(null) })
    return () => { current = false }
  }, [savedServer])

  const signIn = async (): Promise<void> => {
    setError('')
    setBusy('checking')
    const address = server.trim()
    try {
      let value = await rpc.request<Account | null>('collaboration/account/status', [address])
      if (!value) {
        setBusy('signIn')
        value = await rpc.request<Account>('collaboration/account/signIn', [address])
      }
      localStorage.setItem(serverPreference, address)
      setAccount(value)
    } catch (reason) { setError(String(reason)) }
    finally { setBusy(null) }
  }

  const signOut = async (): Promise<void> => {
    setError('')
    setBusy('signOut')
    try {
      await rpc.request('collaboration/account/signOut', [server.trim()])
      setAccount(null)
    } catch (reason) { setError(String(reason)) }
    finally { setBusy(null) }
  }

  return <div className="settings-subgroup">
    <p className="settings-hint">{t('collaboration.serverHint')}</p>
    <label className="inspector-label" htmlFor="set-collaboration-server">{t('collaboration.server')}</label>
    <input id="set-collaboration-server" className="inspector-input" type="url" value={server} disabled={busy !== null || sharing}
      onChange={event => { setServer(event.target.value); setAccount(null); setError('') }} />
    {account && <p role="status">{t('collaboration.signedIn', { name: account.displayName })}</p>}
    {busy === 'signIn' && <p role="status">{t('collaboration.browserPending')}</p>}
    {error && <p role="alert">{error}</p>}
    <div className="match-row">
      {account
        ? <button className="btn-secondary" disabled={busy !== null || sharing} onClick={() => void signOut()}>{t('collaboration.signOut')}</button>
        : <button className="btn-primary" disabled={busy !== null || !server.trim()} onClick={() => void signIn()}>{t('collaboration.signIn')}</button>}
      {busy === 'signIn' && <button className="btn-secondary" onClick={() => {
        void rpc.request('collaboration/account/cancel').catch(reason => setError(String(reason)))
      }}>{t('collaboration.cancelSignIn')}</button>}
    </div>
    <SharedProjectCard server={server} signedIn={account !== null && busy === null} />
  </div>
}
