import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useDialogKeyboard } from './useDialogKeyboard'
import { useWorkspaceDialogGuard } from './useWorkspaceDialogGuard'

interface InputDialogProps {
  title: string
  placeholder?: string
  /** Prefills the field - for renaming something that already has a name. */
  initialValue?: string
  allowEmpty?: boolean
  onSubmit(value: string): void
  onCancel(): void
}

/** Minimal in-window modal input, matching the host dialog-overlay pattern. */
export function InputDialog({
  title,
  placeholder,
  initialValue,
  allowEmpty = false,
  onSubmit,
  onCancel
}: InputDialogProps): React.JSX.Element {
  useWorkspaceDialogGuard()
  const { t } = useTranslation()
  const [value, setValue] = useState(initialValue ?? '')
  const keyboard = useDialogKeyboard(onCancel)

  const submit = (): void => {
    if (allowEmpty || value.trim().length > 0) onSubmit(value.trim())
  }

  return (
    <div
      className="dialog-overlay"
      onPointerDown={(e) => e.target === e.currentTarget && onCancel()}
    >
      <div className="dialog-card" role="dialog" aria-modal="true" aria-label={title} {...keyboard}>
        <div className="dialog-title">{title}</div>
        <input
          className="dialog-input"
          value={value}
          placeholder={placeholder}
          onChange={(e) => setValue(e.target.value)}
          onKeyDown={(e) => {
            if (e.key === 'Enter') {
              e.preventDefault()
              e.stopPropagation()
              submit()
            }
          }}
        />
        <div className="dialog-actions">
          <button className="dialog-button" onClick={onCancel}>
            {t('dialog.cancel')}
          </button>
          <button className="dialog-button primary" onClick={submit}>
            {t('dialog.ok')}
          </button>
        </div>
      </div>
    </div>
  )
}
