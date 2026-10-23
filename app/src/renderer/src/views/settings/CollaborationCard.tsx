import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { rpc } from '../../rpc/client'
import { SharedProjectCard } from './SharedProjectCard'
import { PeerProjectCard } from './PeerProjectCard'
import { useHostedSessionStore } from '../../collaboration/hostedSessionStore'
import { useProjectStore } from '../../stores/projectStore'
import { useCollaborationAccount } from '../../collaboration/useCollaborationAccount'

export type CollaborationIntent = 'share' | 'join'

export function CollaborationCard(): React.JSX.Element {
  const { t } = useTranslation()
  const loaded = useProjectStore(state => state.isLoaded)
  const session = useHostedSessionStore(state => state.session)
  const sharing = useHostedSessionStore(state => state.busy !== null)
  const [intent, setIntent] = useState<CollaborationIntent>(loaded ? 'share' : 'join')
  const [mode, setMode] = useState('hosted')
  const [steamAvailable, setSteamAvailable] = useState(false)
  const account = useCollaborationAccount(mode === 'hosted' && !session)
  useEffect(() => {
    if (session) { setMode(session.mode); setIntent('share') }
  }, [session?.sessionId, session?.mode])
  useEffect(() => {
    let current = true
    void rpc.request<{ available: boolean }>('collaboration/steam/status')
      .then(status => { if (current) setSteamAvailable(status.available) }, () => { if (current) setSteamAvailable(false) })
    return () => { current = false }
  }, [])
  if (session) return <div className="collaboration-settings">
    <p className="settings-hint">{t('collaboration.mode' + (session.mode === 'hosted' ? 'Hosted' : session.mode === 'steam' ? 'Steam' : 'Tcp'))}</p>
    <SharedProjectCard server={session.server} signedIn={false} intent={intent} />
  </div>
  return <div className="collaboration-settings">
    <div className="collaboration-intent" role="group" aria-label={t('collaboration.title')}>
      {(['share', 'join'] as const).map(value => <button key={value} className={'dialog-button' + (intent === value ? ' primary' : '')}
        aria-pressed={intent === value} disabled={sharing || account.busy !== null || value === 'share' && !loaded}
        onClick={() => setIntent(value)}>{t('collaboration.' + value + 'Tab')}</button>)}
    </div>
    <label className="inspector-label" htmlFor="set-collaboration-mode">{t('collaboration.mode')}</label>
    <select id="set-collaboration-mode" className="dialog-input" value={mode} disabled={sharing || account.busy !== null}
      onChange={event => setMode(event.target.value)}>
      <option value="hosted">{t('collaboration.modeHosted')}</option>
      {steamAvailable && <option value="steam">{t('collaboration.modeSteam')}</option>}
      <option value="tcp">{t('collaboration.modeTcp')}</option>
    </select>
    {mode !== 'hosted' ? <PeerProjectCard key={mode} steam={mode === 'steam'} intent={intent} /> : <>
      <label className="inspector-label" htmlFor="set-collaboration-server">{t('collaboration.server')}</label>
      <input id="set-collaboration-server" className="dialog-input" type="url" value={account.server} disabled={account.busy !== null || sharing}
        placeholder="https://" onChange={event => account.changeServer(event.target.value)} />
      {account.account && <p role="status">{t('collaboration.signedIn', { name: account.account.displayName })}</p>}
      {account.busy === 'signIn' && <p role="status">{t('collaboration.browserPending')}</p>}
      {account.error && <p role="alert">{account.error}</p>}
      {account.account ? <details><summary tabIndex={0}>{t('collaboration.accountDetails')}</summary>
        <p className="settings-hint">{t('collaboration.nameHint')}</p>
        <button className="dialog-button" disabled={account.busy !== null || sharing} onClick={() => void account.signOut()}>{t('collaboration.signOut')}</button>
      </details> : <div className="settings-button-row">
        <button className="dialog-button primary" disabled={account.busy !== null || sharing || !account.server.trim()}
          onClick={() => void account.signIn()}>{t('collaboration.signIn')}</button>
        {account.error && <button className="dialog-button" disabled={account.busy !== null || sharing} onClick={() => void account.signOut()}>{t('collaboration.forgetSignIn')}</button>}
        {account.busy === 'signIn' && <button className="dialog-button" onClick={() => void account.cancel()}>{t('collaboration.cancelSignIn')}</button>}
      </div>}
      <SharedProjectCard server={account.server} signedIn={account.account !== null && account.busy === null} intent={intent} />
    </>}
  </div>
}
