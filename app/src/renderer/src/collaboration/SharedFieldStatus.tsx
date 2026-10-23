import { useTranslation } from 'react-i18next'
import type { FieldBinding } from './FocusedField'
import type { useFocusedField } from './useFocusedField'
import { useParticipants } from './participants'
import './presence.css'

export function SharedFieldStatus({ binding, field }: { binding: FieldBinding; field: ReturnType<typeof useFocusedField> }): React.JSX.Element {
  const { t } = useTranslation()
  const { controller, view, blur, recover } = field
  const participants = useParticipants(binding.sessionId)
  const owner = participants.find(participant => participant.deviceId === view.ownerDeviceId && participant.itemId === binding.itemId)
    ?? participants.find(participant => participant.deviceId === view.ownerDeviceId)
  const editor = participants.find(participant => participant.itemId === binding.itemId)
  const writer = view.phase === 'locked' ? owner : view.phase === 'idle' ? editor : undefined
  const editingLabel = writer ? t('collaboration.fieldLockedBy', { name: writer.name }) : undefined
  const recoveryPath = view.recoveryPath
  return <>
    {view.phase === 'waiting' && <span role="status" className="settings-hint">{t('collaboration.fieldWaiting')}</span>}
    {writer && <span role="status" className="nv-collaboration-label nv-collaboration-field-label"
      aria-label={editingLabel} title={editingLabel}>{writer.name}</span>}
    {view.phase === 'locked' && !writer && <span role="status" className="settings-hint">{t('collaboration.fieldLocked')}</span>}
    {view.phase === 'expired' && <span role="status" className="settings-hint">{t('collaboration.fieldExpired')}</span>}
    {view.error && <span role="alert">{view.error}</span>}
    {view.phase === 'failed' && <button className="btn-secondary" onClick={blur}>{t('collaboration.retryField')}</button>}
    {controller.hasDraft && (view.phase === 'failed' || view.phase === 'expired') &&
      <button className="btn-secondary" onMouseDown={event => event.preventDefault()} onClick={recover}>{t('collaboration.recoverField')}</button>}
    {recoveryPath && <>
      <span role="status" className="settings-hint">{t('collaboration.fieldRecovered')}</span>
      <button className="btn-secondary" onClick={() => void window.novalist.revealPath(recoveryPath)}>{t('collaboration.showRecovery')}</button>
    </>}
  </>
}
