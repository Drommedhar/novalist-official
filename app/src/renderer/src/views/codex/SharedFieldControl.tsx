import { useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { FocusedField, type FieldBinding, type FieldView } from '../../collaboration/FocusedField'
import { persistPendingWrite, registerPendingWrite, retainPendingWrite } from '../../stores/pendingWrites'
import { useCodexStore } from '../../stores/codexStore'
import { useParticipants } from '../../collaboration/participants'

const retainedFields = new Map<string, FocusedField>()

export function SharedFieldControl({ binding, value, label, type, list, options, disabled }: {
  binding: FieldBinding; value: string; label?: string; type?: string; list?: string; options?: string[]; disabled?: boolean
}): React.JSX.Element {
  const { t } = useTranslation()
  const fieldKey = `${binding.sessionId}:${binding.itemId}`
  const controller = useMemo(() => retainedFields.get(fieldKey) ?? new FocusedField(binding, value), [fieldKey])
  const [view, setView] = useState<FieldView>(controller.snapshot)
  const participants = useParticipants(binding.sessionId)
  const owner = participants.find(participant => participant.deviceId === view.ownerDeviceId && participant.itemId === binding.itemId)
    ?? participants.find(participant => participant.deviceId === view.ownerDeviceId)
  const key = controller.pendingKey
  const flush = async (preserveFocus = false): Promise<void> => {
    await controller.finish(preserveFocus)
    if (retainedFields.get(fieldKey) === controller) retainedFields.delete(fieldKey)
    const codex = useCodexStore.getState()
    if (codex.selectedId) await codex.select(codex.selectedId)
  }
  useEffect(() => {
    controller.onChange = setView
    const stop = registerPendingWrite(() => persistPendingWrite(key, flush))
    return () => {
      stop()
      controller.onChange = undefined
      retainedFields.set(fieldKey, controller)
      retainPendingWrite(key, flush)
    }
  }, [controller, key])
  useEffect(() => controller.stored(value), [controller, value])
  const editable = view.phase === 'editing'
  const recoveryPath = view.recoveryPath
  const blur = (): void => {
    controller.blur()
    void persistPendingWrite(key, () => flush(true)).catch(() => {})
  }
  const recover = (): void => {
    void persistPendingWrite(key, async () => { await controller.recover(); await flush() }).catch(() => {})
  }
  return <>
    {options ? <select className="dialog-input codex-field-input" aria-label={label} aria-disabled={!editable} disabled={disabled}
      value={view.value || options[0]} onFocus={() => controller.focus()} onBlur={blur}
      onKeyDown={event => { if (!editable && event.key !== 'Tab') event.preventDefault() }}
      onChange={event => controller.change(event.target.value)}>
      {options.map(option => <option key={option} value={option}>{option}</option>)}
    </select> : <input className="outliner-input codex-field-input" type={type ?? 'text'} aria-label={label}
      list={list} value={view.value} readOnly={!editable} disabled={disabled} onFocus={() => controller.focus()}
      onChange={event => controller.change(event.target.value)} onBlur={blur} />}
    {view.phase === 'waiting' && <span role="status" className="settings-hint">{t('collaboration.fieldWaiting')}</span>}
    {view.phase === 'locked' && <span role="status" className="settings-hint">{owner
      ? t('collaboration.fieldLockedBy', { name: owner.name }) : t('collaboration.fieldLocked')}</span>}
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
