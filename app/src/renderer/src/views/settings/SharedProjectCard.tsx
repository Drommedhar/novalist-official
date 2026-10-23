import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useHostedSessionStore } from '../../collaboration/hostedSessionStore'
import { useProjectStore } from '../../stores/projectStore'
import { SharedProjectAccess } from './SharedProjectAccess'
import { CollaborationDestination, type CollaborationDestinationValue } from './CollaborationDestination'
import { CollaborationTransferStatus } from './CollaborationTransferStatus'

export function SharedProjectCard({ server, signedIn }: { server: string; signedIn: boolean }): React.JSX.Element {
  const { t } = useTranslation()
  const { session, saved, copied, busy, invitation, error, presenceUnavailable, refresh, run } = useHostedSessionStore()
  const loaded = useProjectStore(state => state.isLoaded)
  const [link, setLink] = useState('')
  const [destination, setDestination] = useState<CollaborationDestinationValue>({ path: '', valid: false, existing: false })
  const [localError, setLocalError] = useState('')
  useEffect(() => {
    void refresh()
    const timer = setInterval(() => void refresh(), 2000)
    return () => clearInterval(timer)
  }, [refresh])

  const join = async (): Promise<void> => {
    await run('join', [server.trim(), link.trim(), destination.path])
    if (useHostedSessionStore.getState().session) setLink('')
  }
  const recoveryPath = session?.recoveryPath || saved?.recoveryPath
  return <div className="collaboration-project">
    {session ? <>
      <p role="status">{t('collaboration.connection.' + session.connection)}</p>
      {presenceUnavailable && <p className="settings-hint">{t('collaboration.presenceUnavailable')}</p>}
      <p className="settings-hint">{t(session.mode === 'hosted' ? 'collaboration.localCopyHint' : 'collaboration.peerLocalCopyHint')}</p>
      <div className="settings-button-row">
        {session.isOwner && <button className="dialog-button primary" disabled={busy !== null || session.connection !== 'Connected'} onClick={() => {
          void run('invite', [new Date(Date.now() + 24 * 60 * 60 * 1000).toISOString()])
        }}>{t('collaboration.invite')}</button>}
        <button className="dialog-button" disabled={busy !== null} onClick={() => void run('end')}>{t('collaboration.end')}</button>
        {session.connection === 'KeyRefreshRequired' && <button className="dialog-button primary" disabled={busy !== null} onClick={() => {
          void run('end').then(() => { if (!useHostedSessionStore.getState().session) return run('resume') })
        }}>{t('collaboration.refreshKeys')}</button>}
      </div>
      {invitation && <>
        <label className="inspector-label" htmlFor="set-collaboration-invitation">{t('collaboration.invitation')}</label>
        <textarea id="set-collaboration-invitation" className="dialog-input" value={invitation} readOnly onFocus={event => event.target.select()} />
        <p className="settings-hint">{t(session.mode === 'hosted' ? 'collaboration.invitationHint' : 'collaboration.peerInvitationHint')}</p>
      </>}
      {session.isOwner && <SharedProjectAccess sessionId={session.sessionId} disabled={busy !== null || session.connection === 'AccessEnded'} />}
    </> : <>
      {saved && <>
        <p className="settings-hint">{t('collaboration.savedServer', { server: saved.server })}</p>
        <button className="dialog-button primary" disabled={!loaded || busy !== null} onClick={() => void run('resume')}>{t('collaboration.resume')}</button>
        <p className="settings-hint">{t('collaboration.resumeHint')}</p>
      </>}
      {!signedIn && <p className="settings-hint">{t('collaboration.signInHint')}</p>}
      <div className="collaboration-options">
      <section className="collaboration-option" aria-labelledby="collaboration-host-title">
      <h4 id="collaboration-host-title">{t('collaboration.hostTitle')}</h4>
      <p className="settings-hint">{t('collaboration.hostHint')}</p>
      <button className="dialog-button primary" disabled={!signedIn || !loaded || busy !== null} onClick={() => void run('create', [server.trim()])}>
        {t('collaboration.create')}
      </button>
      {!loaded && <p className="settings-hint">{t('collaboration.openProjectHint')}</p>}
      </section>
      <section className="collaboration-option" aria-labelledby="collaboration-join-title">
      <h4 id="collaboration-join-title">{t('collaboration.joinTitle')}</h4>
      <label className="inspector-label" htmlFor="set-collaboration-join">{t('collaboration.invitation')}</label>
      <textarea id="set-collaboration-join" className="dialog-input" value={link} disabled={busy !== null} autoComplete="off"
        onChange={event => setLink(event.target.value)} />
      <CollaborationDestination id="set-collaboration-destination" busy={busy !== null} onChange={setDestination} />
      <div className="settings-button-row">
        <button className="dialog-button primary" disabled={!signedIn || busy !== null || !link.trim() || !destination.valid} onClick={() => void join()}>
          {t('collaboration.join')}
        </button>
        {loaded && !destination.existing && <button className="dialog-button" disabled={busy !== null || !destination.valid}
          onClick={() => void run('copy', [destination.path])}>{t('collaboration.copy')}</button>}
      </div>
      <p className="settings-hint">{t('collaboration.joinHint')}</p>
      </section>
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
