import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useHostedSessionStore } from '../../collaboration/hostedSessionStore'
import { useProjectStore } from '../../stores/projectStore'
import { SharedProjectAccess } from './SharedProjectAccess'

export function SharedProjectCard({ server, signedIn }: { server: string; signedIn: boolean }): React.JSX.Element {
  const { t } = useTranslation()
  const { session, saved, copied, busy, invitation, error, presenceUnavailable, refresh, run } = useHostedSessionStore()
  const loaded = useProjectStore(state => state.isLoaded)
  const [link, setLink] = useState('')
  const [folder, setFolder] = useState('')
  const [name, setName] = useState('')
  const [localError, setLocalError] = useState('')
  useEffect(() => {
    void refresh()
    const timer = setInterval(() => void refresh(), 2000)
    return () => clearInterval(timer)
  }, [refresh])

  const chooseFolder = async (): Promise<void> => {
    try {
      const selected = await window.novalist.pickFolder(t('collaboration.parentFolder'))
      if (selected) { setFolder(selected); setLocalError('') }
    } catch (reason) { setLocalError(String(reason)) }
  }
  const join = async (): Promise<void> => {
    await run('join', [server.trim(), link.trim(), folder.replace(/[\\/]$/, '') + '/' + name.trim()])
    if (useHostedSessionStore.getState().session) setLink('')
  }
  const validName = name.trim().length > 0 && !/[\\/:*?"<>|]/.test(name) && !['.', '..'].includes(name.trim())
  const recoveryPath = session?.recoveryPath || saved?.recoveryPath
  return <div className="settings-subgroup">
    {session ? <>
      <p role="status">{t('collaboration.connection.' + session.connection)}</p>
      {presenceUnavailable && <p className="settings-hint">{t('collaboration.presenceUnavailable')}</p>}
      <p className="settings-hint">{t('collaboration.localCopyHint')}</p>
      <div className="match-row">
        {session.isOwner && <button className="btn-primary" disabled={busy !== null} onClick={() => {
          void run('invite', [new Date(Date.now() + 24 * 60 * 60 * 1000).toISOString()])
        }}>{t('collaboration.invite')}</button>}
        <button className="btn-secondary" disabled={busy !== null} onClick={() => void run('end')}>{t('collaboration.end')}</button>
      </div>
      {invitation && <>
        <label className="inspector-label" htmlFor="set-collaboration-invitation">{t('collaboration.invitation')}</label>
        <textarea id="set-collaboration-invitation" className="inspector-input" value={invitation} readOnly onFocus={event => event.target.select()} />
        <p className="settings-hint">{t('collaboration.invitationHint')}</p>
      </>}
      {session.isOwner && <SharedProjectAccess sessionId={session.sessionId} disabled={busy !== null || session.connection === 'AccessEnded'} />}
    </> : <>
      {saved && <>
        <p className="settings-hint">{t('collaboration.savedServer', { server: saved.server })}</p>
        <button className="btn-primary" disabled={!loaded || busy !== null} onClick={() => void run('resume')}>{t('collaboration.resume')}</button>
        <p className="settings-hint">{t('collaboration.resumeHint')}</p>
      </>}
      <button className="btn-primary" disabled={!signedIn || !loaded || busy !== null} onClick={() => void run('create', [server.trim()])}>
        {t('collaboration.create')}
      </button>
      {!loaded && <p className="settings-hint">{t('collaboration.openProjectHint')}</p>}
      <label className="inspector-label" htmlFor="set-collaboration-join">{t('collaboration.invitation')}</label>
      <textarea id="set-collaboration-join" className="inspector-input" value={link} disabled={busy !== null} autoComplete="off"
        onChange={event => setLink(event.target.value)} />
      <label className="inspector-label" htmlFor="set-collaboration-name">{t('collaboration.localFolderName')}</label>
      <input id="set-collaboration-name" className="inspector-input" value={name} disabled={busy !== null} onChange={event => setName(event.target.value)} />
      <div className="match-row">
        <button className="btn-secondary" disabled={busy !== null} onClick={() => void chooseFolder()}>{t('collaboration.parentFolder')}</button>
        <button className="btn-primary" disabled={!signedIn || busy !== null || !link.trim() || !folder || !validName} onClick={() => void join()}>
          {t('collaboration.join')}
        </button>
        {loaded && <button className="btn-secondary" disabled={busy !== null || !folder || !validName}
          onClick={() => void run('copy', [folder.replace(/[\\/]$/, '') + '/' + name.trim()])}>{t('collaboration.copy')}</button>}
      </div>
      {folder && <p className="settings-hint">{folder}</p>}
      <p className="settings-hint">{t('collaboration.joinHint')}</p>
    </>}
    {recoveryPath && <button className="btn-secondary" onClick={() => {
      void window.novalist.revealPath(recoveryPath).catch(reason => setLocalError(String(reason)))
    }}>{t('collaboration.showRetainedCopy')}</button>}
    {copied && <p role="status">{t('collaboration.copyCreated')}</p>}
    {busy && <p role="status">{t('collaboration.progress.' + busy)}</p>}
    {(error || localError) && <p role="alert">{error || localError}</p>}
  </div>
}
