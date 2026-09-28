import { useEffect, useId, useRef } from 'react'
import { useTranslation } from 'react-i18next'
import { X } from 'lucide-react'
import { useBookshelfStore } from '../stores/bookshelfStore'
import type { RecentProjectDto } from '../stores/projectStore'

export function RemovedLibraryItems({ open, onClose, recentProjects }: {
  open: boolean
  onClose(): void
  recentProjects: RecentProjectDto[]
}): React.JSX.Element {
  const { t } = useTranslation()
  const dialog = useRef<HTMLDialogElement>(null)
  const titleId = useId()
  const descriptionId = useId()
  const removed = useBookshelfStore((state) => state.removed)
  const restore = useBookshelfStore((state) => state.restoreItem)
  const removedProjects = new Set(removed.filter((item) => !item.bookId).map((item) => item.projectKey))
  // A removed project already represents all of its books. Individually removed
  // books remain removed when their project is restored.
  const items = removed.filter((item) => !item.bookId || !removedProjects.has(item.projectKey))
  useEffect(() => { if (open) dialog.current?.showModal(); else dialog.current?.close() }, [open])
  return <dialog ref={dialog} className="library-scratchpad-dialog library-removed-dialog" aria-labelledby={titleId}
    aria-describedby={descriptionId} onClose={onClose} onKeyDown={(event) => event.stopPropagation()}>
    <div className="library-scratchpad-heading">
      <h2 id={titleId} className="dialog-title">{t('bookshelf.removedItems')}</h2>
      <button aria-label={t('dialog.close')} onClick={() => dialog.current?.close()}><X size={20} /></button>
    </div>
    <p id={descriptionId}>{t('bookshelf.removedItemsDescription')}</p>
    {items.length === 0 ? <p>{t('bookshelf.removedEmpty')}</p> : <ul className="library-removed-list">
      {items.map((item) => {
        const project = recentProjects.find((entry) => entry.path === item.projectKey)
        const projectName = project?.name ?? item.projectName
        const name = item.bookId ? project?.books?.find((book) => book.id === item.bookId)?.name ?? item.bookName! : projectName
        return <li key={JSON.stringify([item.projectKey, item.bookId])}>
          <div><strong>{name}</strong><span>{item.bookId ? projectName : t('bookshelf.project')}</span></div>
          <button className="library-restore-button" aria-label={t('bookshelf.restoreItem', { name })}
            onClick={() => {
              restore(item)
              // The restored row disappears; keep keyboard focus in the dialog.
              dialog.current?.querySelector<HTMLButtonElement>('button')?.focus()
            }}>{t('bookshelf.restore')}</button>
        </li>
      })}
    </ul>}
  </dialog>
}
