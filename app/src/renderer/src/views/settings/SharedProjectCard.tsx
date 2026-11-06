import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useHostedSessionStore } from '../../collaboration/hostedSessionStore'
import { useProjectStore } from '../../stores/projectStore'
import { SharedProjectAccess } from './SharedProjectAccess'
import { CollaborationDestination, type CollaborationDestinationValue } from './CollaborationDestination'
import { CollaborationTransferStatus } from './CollaborationTransferStatus'
import { SteamFriendsInvite } from './SteamFriendsInvite'
import type { CollaborationIntent } from './CollaborationCard'

export function SharedProjectCard({ server, canConnect, intent, name, prepareConnection }: {
  server: string; canConnect: boolean; intent: CollaborationIntent; name?: string
  prepareConnection?: (server: string) => Promise<boolean>
}): React.JSX.Element {
  const { t } = useTranslation()
  const { session, saved, copied, busy, invitation, error, presenceUnavailable, run } = useHostedSessionStore()
  const loaded = useProjectStore(state => state.isLoaded)
  const [link, setLink] = useState('')
  const [destination, setDestination] = useState<CollaborationDestinationValue>({ path: '', valid: false, existing: false })
  const [localError, setLocalError] = useState('')

  const connect = async (operation: 'create' | 'join' | 'resume', params: unknown[], address = server.trim()): Promise<void> => {
    if (prepareConnection && !await prepareConnection(address)) return
    await run(operation, params)
  }

  const join = async (): Promise<void> => {
    await connect('join', [server.trim(), link.trim(), destination.path, name])
    if (useHostedSessionStore.getState().session) setLink('')
  }
  const recoveryPath = session?.recoveryPath || saved?.recoveryPath
  return <div className="collaboration-project">
    {session ? <>
      <p role="status">{t('collaboration.connection.' + session.connection)}</p>
      {presenceUnavailable && <p className="settings-hint">{t('collaboration.presenceUnavailable')}</p>}
      <div className="settings-button-row">
        {session.isOwner && <button className="dialog-button primary" disabled={busy !== null || session.connection !== 'Connected'} onClick={() => {
          void run('invite', [new Date(Date.now() + 24 * 60 * 60 * 1000).toISOString()])
        }}>{t('collaboration.invite')}</button>}
        <button className="dialog-button" disabled={busy !== null} onClick={() => void run('end')}>{t('collaboration.end')}</button>
        {session.connection === 'KeyRefreshRequired' && <button className="dialog-button primary" disabled={busy !== null} onClick={() => {
          void run('end').then(() => { if (!useHostedSessionStore.getState().session) return run('resume') })
        }}>{t('collaboration.refreshKeys')}</button>}
      </div>
      {session.mode === 'steam' && session.isOwner && <SteamFriendsInvite disabled={busy !== null || session.connection !== 'Connected'} />}
      {invitation && <>
        <label className="inspector-label" htmlFor="set-collaboration-invitation">{t('collaboration.invitation')}</label>
        <textarea id="set-collaboration-invitation" className="dialog-input" value={invitation} readOnly onFocus={event => event.target.select()} />
        <p className="settings-hint">{t(session.mode === 'hosted' ? 'collaboration.invitationHint' : 'collaboration.peerInvitationHint')}</p>
      </>}
      {session.isOwner && <details><summary tabIndex={0}>{t('collaboration.access.title')}</summary>
        <SharedProjectAccess sessionId={session.sessionId} disabled={busy !== null || session.connection === 'AccessEnded'} />
      </details>}
    </> : <>
      {saved && <>
        <p className="settings-hint">{t('collaboration.savedServer', { server: saved.server })}</p>
        <button className="dialog-button primary" disabled={!canConnect || !loaded || busy !== null}
          onClick={() => void connect('resume', [name], saved.server)}>{t('collaboration.resume')}</button>
        <p className="settings-hint">{t('collaboration.resumeHint')}</p>
      </>}
      <div className="collaboration-options">
      {intent === 'share' ? <section className="collaboration-option" aria-labelledby="collaboration-host-title">
      <h4 id="collaboration-host-title">{t('collaboration.hostTitle')}</h4>
      <p className="settings-hint">{t('collaboration.hostHint')}</p>
      <button className="dialog-button primary" disabled={!canConnect || !loaded || busy !== null} onClick={() => void connect('create', [server.trim(), name])}>
        {t('collaboration.create')}
      </button>
      {!loaded && <p className="settings-hint">{t('collaboration.openProjectHint')}</p>}
      </section> : <section className="collaboration-option" aria-labelledby="collaboration-join-title">
      <h4 id="collaboration-join-title">{t('collaboration.joinTitle')}</h4>
      <label className="inspector-label" htmlFor="set-collaboration-join">{t('collaboration.invitation')}</label>
      <textarea id="set-collaboration-join" className="dialog-input" value={link} disabled={busy !== null} autoComplete="off"
        onChange={event => setLink(event.target.value)} />
      <CollaborationDestination id="set-collaboration-destination" busy={busy !== null} onChange={setDestination} />
      <div className="settings-button-row">
        <button className="dialog-button primary" disabled={!canConnect || busy !== null || !link.trim() || !destination.valid} onClick={() => void join()}>
          {t('collaboration.join')}
        </button>
        {loaded && !destination.existing && <button className="dialog-button" disabled={busy !== null || !destination.valid}
          onClick={() => void run('copy', [destination.path])}>{t('collaboration.copy')}</button>}
      </div>
      <p className="settings-hint">{t('collaboration.joinHint')}</p>
      </section>}
      </div>
    </>}
    {recoveryPath && <button className="dialog-button" onClick={() => {
      void window.novalist.revealPath(recoveryPath).catch(reason => setLocalError(String(reason)))
    }}>{t('collaboration.showRetainedCopy')}</button>}
    {copied && <p role="status">{t('collaboration.copyCreated')}</p>}
    <CollaborationTransferStatus />
    {(error || localError) && <p role="alert">{error || localError}</p>}
  </div>
}
