import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useHostedSessionStore } from '../../collaboration/hostedSessionStore'
import { useProjectStore } from '../../stores/projectStore'
import { SharedProjectCard } from './SharedProjectCard'
import { rpc } from '../../rpc/client'

interface SteamStatus { available: boolean; reason: string; displayName: string | null }

export function PeerProjectCard({ steam = false }: { steam?: boolean }): React.JSX.Element {
  const { t } = useTranslation()
  const { session, copied, busy, error, refresh, run } = useHostedSessionStore()
  const loaded = useProjectStore(state => state.isLoaded)
  const [name, setName] = useState('')
  const [address, setAddress] = useState('127.0.0.1')
  const [port, setPort] = useState('0')
  const [link, setLink] = useState('')
  const [folder, setFolder] = useState('')
  const [folderName, setFolderName] = useState('')
  const [localError, setLocalError] = useState('')
  const [steamStatus, setSteamStatus] = useState<SteamStatus | null>(null)
  useEffect(() => { void refresh() }, [refresh])
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
  const validFolder = folderName.trim().length > 0 && !/[\\/:*?"<>|]/.test(folderName) && !['.', '..'].includes(folderName.trim())
  const validPort = /^\d+$/.test(port) && Number(port) >= 0 && Number(port) <= 65535
  const chooseFolder = async (): Promise<void> => {
    try {
      const selected = await window.novalist.pickFolder(t('collaboration.parentFolder'))
      if (selected) { setFolder(selected); setLocalError('') }
    } catch (reason) { setLocalError(String(reason)) }
  }
  if (session) return <SharedProjectCard server="" signedIn={false} />
  return <div className="settings-subgroup">
    <p className="settings-hint">{t(steam ? 'collaboration.steamHint' : 'collaboration.peerHint')}</p>
    {steam && steamStatus && !steamStatus.available && <p role="status">{steamStatus.reason}</p>}
    <label className="inspector-label" htmlFor="set-peer-name">{t('collaboration.peerName')}</label>
    <input id="set-peer-name" className="inspector-input" value={name} disabled={busy !== null} maxLength={128}
      onChange={event => setName(event.target.value)} />
    {!steam && <><label className="inspector-label" htmlFor="set-peer-address">{t('collaboration.peerAddress')}</label>
    <input id="set-peer-address" className="inspector-input" value={address} disabled={busy !== null}
      onChange={event => setAddress(event.target.value)} />
    <label className="inspector-label" htmlFor="set-peer-port">{t('collaboration.peerPort')}</label>
    <input id="set-peer-port" className="inspector-input" type="number" min={0} max={65535} value={port} disabled={busy !== null}
      onChange={event => setPort(event.target.value)} /></>}
    <button className="btn-primary" disabled={busy !== null || !available || !loaded || !validName || !address.trim() || !validPort}
      onClick={() => void (steam ? run('steamHost', [name.trim()]) : run('peerHost', [address.trim(), Number(port), name.trim()]))}>
      {t(steam ? 'collaboration.steamHost' : 'collaboration.peerHost')}</button>
    {!loaded && <p className="settings-hint">{t('collaboration.openProjectHint')}</p>}
    <label className="inspector-label" htmlFor="set-peer-link">{t('collaboration.invitation')}</label>
    <textarea id="set-peer-link" className="inspector-input" value={link} disabled={busy !== null} autoComplete="off"
      onChange={event => setLink(event.target.value)} />
    <label className="inspector-label" htmlFor="set-peer-folder-name">{t('collaboration.localFolderName')}</label>
    <input id="set-peer-folder-name" className="inspector-input" value={folderName} disabled={busy !== null}
      onChange={event => setFolderName(event.target.value)} />
    <div className="match-row">
      <button className="btn-secondary" disabled={busy !== null} onClick={() => void chooseFolder()}>{t('collaboration.parentFolder')}</button>
      <button className="btn-primary" disabled={busy !== null || !available || !validName || !link.trim() || !folder || !validFolder}
        onClick={() => void run(steam ? 'steamJoin' : 'peerJoin', [link.trim(), folder.replace(/[\\/]$/, '') + '/' + folderName.trim(), name.trim()])}>
        {t(steam ? 'collaboration.steamJoin' : 'collaboration.peerJoin')}</button>
      {loaded && <button className="btn-secondary" disabled={busy !== null || !folder || !validFolder}
        onClick={() => void run('copy', [folder.replace(/[\\/]$/, '') + '/' + folderName.trim()])}>{t('collaboration.copy')}</button>}
    </div>
    {folder && <p className="settings-hint">{folder}</p>}
    {copied && <p role="status">{t('collaboration.copyCreated')}</p>}
    {busy && <p role="status">{t('collaboration.progress.' + busy)}</p>}
    {(error || localError) && <p role="alert">{error || localError}</p>}
  </div>
}
