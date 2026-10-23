import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useHostedSessionStore } from '../../collaboration/hostedSessionStore'
import { useProjectStore } from '../../stores/projectStore'
import { SharedProjectCard } from './SharedProjectCard'
import { rpc } from '../../rpc/client'
import { CollaborationDestination, type CollaborationDestinationValue } from './CollaborationDestination'
import { CollaborationTransferStatus } from './CollaborationTransferStatus'
import type { CollaborationIntent } from './CollaborationCard'

interface SteamStatus { available: boolean; reason: string; displayName: string | null }

export function PeerProjectCard({ steam = false, intent }: { steam?: boolean; intent: CollaborationIntent }): React.JSX.Element {
  const { t } = useTranslation()
  const { session, copied, busy, error, run } = useHostedSessionStore()
  const loaded = useProjectStore(state => state.isLoaded)
  const [name, setName] = useState('')
  const [address, setAddress] = useState('127.0.0.1')
  const [port, setPort] = useState('0')
  const [link, setLink] = useState('')
  const [destination, setDestination] = useState<CollaborationDestinationValue>({ path: '', valid: false, existing: false })
  const [localError, setLocalError] = useState('')
  const [steamStatus, setSteamStatus] = useState<SteamStatus | null>(null)
  useEffect(() => {
    if (!steam) return
    let current = true
    void rpc.request<SteamStatus>('collaboration/steam/status').then(status => {
      if (current) { setSteamStatus(status); setName(status.displayName ?? '') }
    }).catch(reason => { if (current) setLocalError(String(reason)) })
    return () => { current = false }
  }, [steam])
  const available = !steam || steamStatus?.available === true
  const validName = name.trim().length > 0 && name.trim().length <= 128
  const validPort = /^\d+$/.test(port) && Number(port) >= 0 && Number(port) <= 65535
  if (session) return <SharedProjectCard server="" canConnect={false} intent={intent} />
  return <div className="collaboration-project">
    {steam && steamStatus && !steamStatus.available && <p role="status">{steamStatus.reason}</p>}
    <label className="inspector-label" htmlFor="set-peer-name">{t('collaboration.peerName')}</label>
    <input id="set-peer-name" className="dialog-input" value={name} disabled={busy !== null} maxLength={128}
      onChange={event => setName(event.target.value)} />
    <div className="collaboration-options">
    {intent === 'share' ? <section className="collaboration-option" aria-labelledby="collaboration-host-title">
    <h4 id="collaboration-host-title">{t('collaboration.hostTitle')}</h4>
    <p className="settings-hint">{t('collaboration.hostHint')}</p>
    {!steam && <><label className="inspector-label" htmlFor="set-peer-address">{t('collaboration.peerAddress')}</label>
    <input id="set-peer-address" className="dialog-input" value={address} disabled={busy !== null}
      onChange={event => setAddress(event.target.value)} />
    <label className="inspector-label" htmlFor="set-peer-port">{t('collaboration.peerPort')}</label>
    <input id="set-peer-port" className="dialog-input" type="number" min={0} max={65535} value={port} disabled={busy !== null}
      onChange={event => setPort(event.target.value)} /></>}
    <button className="dialog-button primary" disabled={busy !== null || !available || !loaded || !validName || !address.trim() || !validPort}
      onClick={() => void (steam ? run('steamHost', [name.trim()]) : run('peerHost', [address.trim(), Number(port), name.trim()]))}>
      {t(steam ? 'collaboration.steamHost' : 'collaboration.peerHost')}</button>
    {!loaded && <p className="settings-hint">{t('collaboration.openProjectHint')}</p>}
    </section> : <section className="collaboration-option" aria-labelledby="collaboration-join-title">
    <h4 id="collaboration-join-title">{t('collaboration.joinTitle')}</h4>
    <label className="inspector-label" htmlFor="set-peer-link">{t('collaboration.invitation')}</label>
    <textarea id="set-peer-link" className="dialog-input" value={link} disabled={busy !== null} autoComplete="off"
      onChange={event => setLink(event.target.value)} />
    <CollaborationDestination id="set-peer-destination" busy={busy !== null} onChange={setDestination} />
    <div className="settings-button-row">
      <button className="dialog-button primary" disabled={busy !== null || !available || !validName || !link.trim() || !destination.valid}
        onClick={() => void run(steam ? 'steamJoin' : 'peerJoin', [link.trim(), destination.path, name.trim()])}>
        {t(steam ? 'collaboration.steamJoin' : 'collaboration.peerJoin')}</button>
      {loaded && !destination.existing && <button className="dialog-button" disabled={busy !== null || !destination.valid}
        onClick={() => void run('copy', [destination.path])}>{t('collaboration.copy')}</button>}
    </div>
    <p className="settings-hint">{t('collaboration.joinHint')}</p>
    </section>}
    </div>
    {copied && <p role="status">{t('collaboration.copyCreated')}</p>}
    <CollaborationTransferStatus />
    {(error || localError) && <p role="alert">{error || localError}</p>}
  </div>
}
