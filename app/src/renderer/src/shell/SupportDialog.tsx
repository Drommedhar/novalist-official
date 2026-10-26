import { useEffect, useId, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import './support.css'

const PROVIDERS = [
  { name: 'Patreon', url: 'https://patreon.com/Drommedhar' },
  { name: 'Ko-fi', url: 'https://ko-fi.com/drommedhar' }
]

export function SupportDialog({ onClose }: { onClose(): void }): React.JSX.Element {
  const { t } = useTranslation()
  const titleId = useId()
  const messageId = useId()
  const dialogRef = useRef<HTMLDialogElement>(null)
  const [linkFailed, setLinkFailed] = useState(false)

  useEffect(() => {
    const previous = document.activeElement
    const dialog = dialogRef.current
    if (!dialog) return
    dialog.showModal()
    return () => {
      dialog.close()
      if (previous instanceof HTMLElement) previous.focus()
    }
  }, [])

  const openProvider = async (url: string): Promise<void> => {
    setLinkFailed(false)
    try {
      setLinkFailed(!(await window.novalist.openExternal(url)))
    } catch {
      setLinkFailed(true)
    }
  }

  return (
    <dialog ref={dialogRef} className="dialog-card support-dialog" aria-labelledby={titleId} aria-describedby={messageId}
      onCancel={(event) => { event.preventDefault(); onClose() }} onKeyDown={(event) => event.stopPropagation()}>
      <h2 className="dialog-title" id={titleId}>{t('support.title')}</h2>
      <p id={messageId}>{t('support.dialogMessage')}</p>
      <div className="support-providers">
        {PROVIDERS.map(({ name, url }) => (
          <a key={name} className="dialog-button support-provider" href={url}
            onClick={(event) => { event.preventDefault(); void openProvider(url) }}>{name}</a>
        ))}
      </div>
      <p className="support-hint">{t('support.optional')}</p>
      {linkFailed && <p role="alert">{t('support.openFailed')}</p>}
      <div className="dialog-actions">
        <button className="dialog-button" type="button" onClick={onClose}>{t('dialog.close')}</button>
      </div>
    </dialog>
  )
}
