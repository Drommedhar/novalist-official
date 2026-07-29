import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { rpc } from '../../rpc/client'

interface Publishing {
  isbn: string
  publisher: string
  description: string
  rights: string
  publicationDate: string
  seriesName: string
  seriesPosition: string
  subjects: string[]
  /** Derived: the bare digits the exported file will carry. Empty when what was
   *  typed is not a usable ISBN. */
  normalizedIsbn: string
}

const EMPTY: Publishing = {
  isbn: '',
  publisher: '',
  description: '',
  rights: '',
  publicationDate: '',
  seriesName: '',
  seriesPosition: '',
  subjects: [],
  normalizedIsbn: ''
}

/**
 * What a shop, a library and a distributor need to know about the book.
 *
 * Novalist wrote four fields into an EPUB's metadata block, so a retailer
 * ingesting one had no ISBN to key on and no way to learn that a book is the
 * second of a trilogy. Everything here is optional; leaving it blank writes
 * nothing extra.
 */
export function PublishingPanel(): React.JSX.Element {
  const { t } = useTranslation()
  const [value, setValue] = useState<Publishing>(EMPTY)
  const [dirty, setDirty] = useState(false)

  useEffect(() => {
    void rpc.request<Publishing>('publishing/get').then(setValue)
  }, [])

  const edit = (patch: Partial<Publishing>): void => {
    setDirty(true)
    setValue({ ...value, ...patch })
  }

  const save = async (): Promise<void> => {
    setValue(await rpc.request<Publishing>('publishing/set', [value]))
    setDirty(false)
  }

  const field = (
    key: keyof Publishing,
    multiline = false
  ): React.JSX.Element =>
    multiline ? (
      <textarea
        className="inspector-input publishing-textarea"
        value={String(value[key])}
        onChange={(e) => edit({ [key]: e.target.value } as Partial<Publishing>)}
      />
    ) : (
      <input
        className="inspector-input"
        value={String(value[key])}
        onChange={(e) => edit({ [key]: e.target.value } as Partial<Publishing>)}
      />
    )

  return (
    <div className="settings-subgroup">
      <div className="settings-hint">{t('publishing.intro')}</div>

      <label className="inspector-label">{t('publishing.isbn')}</label>
      {field('isbn')}
      {/* What a retailer will actually key on, or a warning that the typo
          produced nothing usable. */}
      {value.isbn.trim().length > 0 && (
        <div className="match-hint">
          {value.normalizedIsbn.length > 0
            ? t('publishing.isbnResolved', { isbn: value.normalizedIsbn })
            : t('publishing.isbnUnusable')}
        </div>
      )}

      <label className="inspector-label">{t('publishing.publisher')}</label>
      {field('publisher')}

      <label className="inspector-label">{t('publishing.seriesName')}</label>
      {field('seriesName')}
      <label className="inspector-label">{t('publishing.seriesPosition')}</label>
      {field('seriesPosition')}
      <div className="match-hint">{t('publishing.seriesHint')}</div>

      <label className="inspector-label">{t('publishing.description')}</label>
      {field('description', true)}

      <label className="inspector-label">{t('publishing.subjects')}</label>
      <input
        className="inspector-input"
        value={value.subjects.join(', ')}
        placeholder={t('publishing.subjectsPlaceholder')}
        onChange={(e) =>
          edit({ subjects: e.target.value.split(',').map((sub) => sub.trim()) })
        }
      />
      <div className="match-hint">{t('publishing.subjectsHint')}</div>

      <label className="inspector-label">{t('publishing.rights')}</label>
      {field('rights')}

      <label className="inspector-label">{t('publishing.publicationDate')}</label>
      {field('publicationDate')}

      <div className="settings-button-row">
        <button className="dialog-button" disabled={!dirty} onClick={() => void save()}>
          {t('publishing.save')}
        </button>
      </div>
    </div>
  )
}
