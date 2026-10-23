import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { rpc } from '../rpc/client'
import { useShellStore } from '../stores/shellStore'
import { useDialogKeyboard } from '../shell/useDialogKeyboard'
import { CollaborationDestination, type CollaborationDestinationValue } from '../views/settings/CollaborationDestination'
import { CollaborationTransferStatus } from '../views/settings/CollaborationTransferStatus'
import { useHostedSessionStore } from './hostedSessionStore'
import { useSteamInvitations, type SteamJoinRequest } from './steamInvitations'
import './steamInvitations.css'

export function SteamInvitationDialog(): React.JSX.Element | null {
  const ready = useShellStore(state => state.backendVersion)
  const pending = useSteamInvitations(state => state.pending)
  useEffect(() => {
    if (!ready || window.novalist.isMobile) return
    let stopped = false
    let timer: ReturnType<typeof setTimeout>
    const poll = async (): Promise<void> => {
      await useSteamInvitations.getState().refresh()
      if (!stopped) timer = setTimeout(() => void poll(), 1000)
    }
    void poll()
    return () => { stopped = true; clearTimeout(timer) }
  }, [ready])
  return pending ? <InvitationPrompt key={pending.id} request={pending} /> : null
}

function InvitationPrompt({ request }: { request: SteamJoinRequest }): React.JSX.Element {
  const { t } = useTranslation()
  const { session, busy, run, error } = useHostedSessionStore()
  const [name, setName] = useState('')
  const [localError, setLocalError] = useState('')
  const [destination, setDestination] = useState<CollaborationDestinationValue>({ path: '', valid: false, existing: false })
  useEffect(() => {
    void rpc.request<{ displayName: string | null }>('collaboration/steam/status')
      .then(status => setName(status.displayName ?? '')).catch(reason => setLocalError(String(reason)))
    void useHostedSessionStore.getState().refresh()
  }, [])
  const dismiss = async (): Promise<void> => {
    if (busy) return
    try { await useSteamInvitations.getState().dismiss(request.id) }
    catch (reason) { setLocalError(String(reason)) }
  }
  const keyboard = useDialogKeyboard(() => void dismiss())
  const join = async (): Promise<void> => {
    await run('steamJoin', [request.link, destination.path, name.trim()])
    if (useHostedSessionStore.getState().session) {
      try { await useSteamInvitations.getState().dismiss(request.id) }
      catch (reason) { setLocalError(String(reason)) }
    }
  }
  if (busy === 'steamJoin') return <></>
  return <div className="dialog-overlay">
    <div className="dialog-card steam-invitation-dialog" role="dialog" aria-modal="true"
      aria-label={t('collaboration.steamInvitationTitle')} {...keyboard}>
      <h2 className="dialog-title">{t('collaboration.steamInvitationTitle')}</h2>
      <p>{request.friendName ? t('collaboration.steamInvitationFrom', { name: request.friendName }) : t('collaboration.steamInvitationReceived')}</p>
      {request.error ? <p role="alert">{request.error}</p> : session ? <p>{t('collaboration.steamEndCurrentFirst')}</p> : <>
        <p className="dialog-message">{t('collaboration.steamInvitationDestination')}</p>
        <label className="inspector-label" htmlFor="steam-invitation-name">{t('collaboration.peerName')}</label>
        <input id="steam-invitation-name" className="dialog-input" value={name} maxLength={128} disabled={busy !== null}
          onChange={event => setName(event.target.value)} />
        <CollaborationDestination id="steam-invitation-destination" busy={busy !== null} onChange={setDestination} />
      </>}
      <div className="dialog-actions">
        <button className="dialog-button" disabled={busy !== null} onClick={() => void dismiss()}>{t('dialog.cancel')}</button>
        <button className="dialog-button primary" disabled={busy !== null || session !== null || !request.link || !destination.valid || !name.trim()}
          onClick={() => void join()}>{t('collaboration.steamAcceptInvitation')}</button>
      </div>
      <CollaborationTransferStatus />
      {(localError || error) && <p role="alert">{localError || error}</p>}
    </div>
  </div>
}
