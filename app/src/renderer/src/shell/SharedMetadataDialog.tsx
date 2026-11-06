import { useState, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { flushPendingWrites } from '../stores/pendingWrites'
import { useDialogKeyboard } from './useDialogKeyboard'
import { useWorkspaceDialogGuard } from './useWorkspaceDialogGuard'

export function SharedMetadataDialog({ title, children, onClose }: {
  title: string; children: ReactNode; onClose(): void
}): React.JSX.Element {
  useWorkspaceDialogGuard()
  const { t } = useTranslation()
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const close = async (): Promise<void> => {
    if (busy) return
    setBusy(true)
    setError('')
    try { await flushPendingWrites(); onClose() }
    catch (failure) { setError(String(failure)) }
    finally { setBusy(false) }
  }
  const keyboard = useDialogKeyboard(() => void close())
  return <div className="dialog-overlay" onPointerDown={event => {
    if (event.target === event.currentTarget) void close()
  }}>
    <div className="dialog-card" role="dialog" aria-label={title} aria-busy={busy}
      ref={keyboard.ref} onKeyDown={keyboard.onKeyDown}>
      <div className="dialog-title">{title}</div>
      <div inert={busy}>{children}</div>
      {error && <p role="alert">{error}</p>}
      <div className="dialog-actions">
        <button className="dialog-button primary" disabled={busy} onClick={() => void close()}>{t('dialog.close')}</button>
      </div>
    </div>
  </div>
}
