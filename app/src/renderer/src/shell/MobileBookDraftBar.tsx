import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useProjectStore } from '../stores/projectStore'
import { InputDialog } from './InputDialog'

/**
 * Book + Draft selectors for mobile. On desktop these live in the toolbar (hidden
 * on mobile), so the mobile Binder header surfaces them. Native <select> renders
 * as an iOS wheel picker, which is a good touch target; "New..." opens an input
 * dialog, mirroring the toolbar's create flow.
 */
export function MobileBookDraftBar(): React.JSX.Element | null {
  const { t } = useTranslation()
  const books = useProjectStore((s) => s.books)
  const activeBookId = useProjectStore((s) => s.activeBookId)
  const drafts = useProjectStore((s) => s.drafts)
  const activeDraft = drafts.find((d) => d.isActive) ?? null
  const [dialog, setDialog] = useState<'book' | 'draft' | null>(null)

  if (books.length === 0 && drafts.length === 0) return null

  return (
    <div className="mobile-bookdraft">
      {books.length > 0 && (
        <select
          className="mobile-bookdraft-select"
          aria-label={t('book.pickerTooltip')}
          value={activeBookId ?? ''}
          onChange={(e) => {
            if (e.target.value === '__new__') setDialog('book')
            else void useProjectStore.getState().switchBook(e.target.value)
          }}
        >
          {books.map((book) => (
            <option key={book.id} value={book.id}>
              {book.name}
            </option>
          ))}
          <option value="__new__">{t('book.addBook')}</option>
        </select>
      )}
      {drafts.length > 0 && (
        <select
          className="mobile-bookdraft-select"
          aria-label={t('draft.add')}
          value={activeDraft?.id ?? ''}
          onChange={(e) => {
            if (e.target.value === '__new__') setDialog('draft')
            else void useProjectStore.getState().switchDraft(e.target.value)
          }}
        >
          {drafts.map((draft) => (
            <option key={draft.id} value={draft.id}>
              {draft.name}
            </option>
          ))}
          <option value="__new__">{t('draft.add')}</option>
        </select>
      )}
      {dialog === 'book' && (
        <InputDialog
          title={t('book.addBookTitle')}
          onCancel={() => setDialog(null)}
          onSubmit={(name) => {
            setDialog(null)
            void useProjectStore.getState().createBook(name)
          }}
        />
      )}
      {dialog === 'draft' && (
        <InputDialog
          title={t('draft.newTitle')}
          onCancel={() => setDialog(null)}
          onSubmit={(name) => {
            setDialog(null)
            void useProjectStore.getState().createDraft(name)
          }}
        />
      )}
    </div>
  )
}
