import { useEffect, useId, useRef, useState } from 'react'
import { NotebookPen, X } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { ScratchpadPanel } from './ScratchpadPanel'

/** Quick access from the library; keep an unfinished note when the dialog closes. */
export function LibraryScratchpad(): React.JSX.Element {
  const { t } = useTranslation()
  const titleId = useId()
  const dialog = useRef<HTMLDialogElement>(null)
  const [open, setOpen] = useState(false)
  const [hasOpened, setHasOpened] = useState(false)

  useEffect(() => {
    if (!open) return
    dialog.current?.showModal()
    dialog.current?.querySelector('textarea')?.focus()
  }, [open])

  return <>
    <button className="library-scratchpad-button" aria-haspopup="dialog" aria-expanded={open}
      onClick={() => { setHasOpened(true); setOpen(true) }}>
      <NotebookPen size={17} /> {t('scratchpad.title')}
    </button>
    <dialog ref={dialog} className="library-scratchpad-dialog" aria-labelledby={titleId}
      onClose={() => setOpen(false)} onKeyDown={(event) => event.stopPropagation()}>
      <div className="library-scratchpad-heading">
        <h2 id={titleId} className="dialog-title">{t('scratchpad.title')}</h2>
        <button aria-label={t('dialog.close')} onClick={() => dialog.current?.close()}><X size={20} /></button>
      </div>
      {hasOpened && <ScratchpadPanel canFile={false} active={open} showTitle={false} />}
    </dialog>
  </>
}
