import { useLayoutEffect, useRef } from 'react'
import { useTranslation } from 'react-i18next'
import type { WindowCloseStage } from './useWorkspaceWindow'
import './windowClosing.css'

/** A modal also freezes existing top-layer dialogs, which escape shell inertness. */
export function WindowClosingDialog({ stage }: { stage: WindowCloseStage }): React.JSX.Element {
  const { t } = useTranslation()
  const dialogRef = useRef<HTMLDialogElement>(null)
  useLayoutEffect(() => {
    const dialog = dialogRef.current!
    dialog.showModal()
    return () => dialog.close()
  }, [])

  return (
    <dialog ref={dialogRef} className="dialog-card window-closing-dialog" tabIndex={-1}
      aria-labelledby="window-closing-title" aria-describedby="window-closing-detail"
      onCancel={(event) => event.preventDefault()}
      onKeyDown={(event) => {
        event.stopPropagation()
        if (event.key === 'Escape' || event.key === 'Tab') event.preventDefault()
      }}>
      <div className="dialog-title" id="window-closing-title">{t('shell.closingWindow')}</div>
      <p id="window-closing-detail" role="status" aria-live="polite">
        {t(stage === 'backup' ? 'shell.backupBeforeClose' : 'shell.savingBeforeClose')}
      </p>
    </dialog>
  )
}
