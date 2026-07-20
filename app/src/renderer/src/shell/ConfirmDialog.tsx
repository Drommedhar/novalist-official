import { useTranslation } from 'react-i18next'

interface ConfirmDialogProps {
  title: string
  message: string
  onConfirm(): void
  onCancel(): void
}

export function ConfirmDialog({
  title,
  message,
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
            {t('explorer.contextDelete')}
          </button>
        </div>
      </div>
    </div>
  )
}
