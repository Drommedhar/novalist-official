import { useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'

interface InputDialogProps {
  title: string
  placeholder?: string
  onSubmit(value: string): void
  onCancel(): void
}

/** Minimal in-window modal input, matching the host dialog-overlay pattern. */
export function InputDialog({
  title,
  placeholder,
  onSubmit,
  onCancel
}: InputDialogProps): React.JSX.Element {
  const { t } = useTranslation()
  const [value, setValue] = useState('')
  const inputRef = useRef<HTMLInputElement>(null)

  useEffect(() => inputRef.current?.focus(), [])

  const submit = (): void => {
    if (value.trim().length > 0) onSubmit(value.trim())
  }

  return (
    <div className="dialog-overlay" onPointerDown={(e) => e.target === e.currentTarget && onCancel()}>
      <div className="dialog-card" role="dialog" aria-label={title}>
        <div className="dialog-title">{title}</div>
        <input
          ref={inputRef}
          className="dialog-input"
          value={value}
          placeholder={placeholder}
          onChange={(e) => setValue(e.target.value)}
          onKeyDown={(e) => {
            if (e.key === 'Enter') submit()
            if (e.key === 'Escape') onCancel()
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
