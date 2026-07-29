import { useTranslation } from 'react-i18next'

interface ConfirmDialogProps {
  title: string
  message: string
  /** What the confirming button says. Defaults to "Delete", which is what every
   *  caller wanted until archiving started asking too. */
  confirmLabel?: string
  onConfirm(): void
  onCancel(): void
}

export function ConfirmDialog({
  title,
  message,
  confirmLabel,
  onConfirm,
  onCancel
}: ConfirmDialogProps): React.JSX.Element {
  const { t } = useTranslation()
  return (
    <div className="dialog-overlay" onPointerDown={(e) => e.target === e.currentTarget && onCancel()}>
      <div className="dialog-card" role="dialog" aria-label={title}>
        <div className="dialog-title">{title}</div>
        <p className="dialog-message">{message}</p>
        <div className="dialog-actions">
          <button className="dialog-button" onClick={onCancel}>
            {t('dialog.cancel')}
          </button>
          <button className="dialog-button danger" onClick={onConfirm}>
            {confirmLabel ?? t('explorer.contextDelete')}
          </button>
        </div>
      </div>
    </div>
  )
}
