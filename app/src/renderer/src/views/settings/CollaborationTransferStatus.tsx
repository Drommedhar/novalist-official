import { useTranslation } from 'react-i18next'
import { useHostedSessionStore } from '../../collaboration/hostedSessionStore'
import './collaboration.css'

export function CollaborationTransferDialog(): React.JSX.Element | null {
  const { t } = useTranslation()
  const busy = useHostedSessionStore(state => state.busy)
  if (!busy || ['copy', 'invite', 'end'].includes(busy)) return null
  return <div className="dialog-overlay" role="dialog" aria-modal="true" aria-label={t('collaboration.transfer.label')}>
    <div className="dialog-card"><CollaborationTransferStatus active /></div>
  </div>
}

export function CollaborationTransferStatus({ active = false }: { active?: boolean }): React.JSX.Element | null {
  const { t, i18n } = useTranslation()
  const { busy, session, transfer, cancelling, cancel } = useHostedSessionStore()
  if (!busy && transfer?.phase !== 'complete') return null
  if (busy && !['copy', 'invite', 'end'].includes(busy) && !active) return null
  const bytes = (value: number): string => {
    const units = ['B', 'KB', 'MB', 'GB', 'TB']
    const exponent = value > 0 ? Math.min(Math.floor(Math.log(value) / Math.log(1024)), units.length - 1) : 0
    return new Intl.NumberFormat(i18n.language, { maximumFractionDigits: 1 }).format(value / 1024 ** exponent) + ' ' + units[exponent]
  }
  const hosting = busy ? ['create', 'peerHost', 'steamHost'].includes(busy) : session?.isOwner === true
  const starting = busy !== null && transfer?.phase === 'complete'
  const phase = hosting ? (busy ? (starting ? 'hostStarting' : 'hostPreparing') : 'hostReady')
    : starting ? 'starting' : transfer?.phase
  const total = starting ? 0 : transfer?.phase === 'transferring' ? transfer.totalBytes : transfer?.totalFiles ?? 0
  const done = transfer?.phase === 'transferring' ? transfer.completedBytes : transfer?.completedFiles ?? 0
  const canCancel = busy !== null && !['copy', 'invite', 'end'].includes(busy)
  return <div className="collaboration-transfer" aria-live="polite">
    <p role="status">{cancelling ? t('collaboration.transfer.cancelling') : phase
      ? t('collaboration.transfer.' + phase) : t('collaboration.progress.' + busy)}</p>
    {busy && <progress aria-label={t('collaboration.transfer.label')} max={Math.max(total, 1)}
      value={total > 0 ? Math.min(done, total) : undefined} />}
    {transfer && <>
      <p className="settings-hint">{t(hosting ? 'collaboration.transfer.localFiles' : 'collaboration.transfer.files', {
        done: transfer.completedFiles, total: transfer.totalFiles, reused: transfer.reusedFiles
      })}</p>
      {transfer.totalBytes > 0 && <p className="settings-hint">{t(hosting ? 'collaboration.transfer.localBytes' : 'collaboration.transfer.bytes', {
        done: bytes(transfer.completedBytes), total: bytes(transfer.totalBytes)
      })}</p>}
    </>}
    {canCancel && <button className="dialog-button" disabled={cancelling} onClick={() => void cancel()}>
      {t('collaboration.transfer.cancel')}
    </button>}
  </div>
}
