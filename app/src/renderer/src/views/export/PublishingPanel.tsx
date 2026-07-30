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
  /** Where the book can be bought, one entry per store. */
  retailers: Retailer[]
  /** Derived: the bare digits the exported file will carry. Empty when what was
   *  typed is not a usable ISBN. */
  normalizedIsbn: string
}

/** One store's page for this book. */
interface Retailer {
  key: string
  name: string
  url: string
  productId: string
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
  retailers: [],
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

      {/* One format, one path, one file was the whole model, so every copy of
          a book sold in five shops carried the same back-matter link - and
          Amazon refuses a book whose back matter links to a rival store. */}
      <label className="inspector-label">{t('publishing.retailers')}</label>
      <div className="match-hint">{t('publishing.retailersHint')}</div>
      {value.retailers.map((retailer, index) => (
        <div key={index} className="match-row">
          <input
            className="inspector-input"
            value={retailer.key}
            placeholder={t('publishing.retailerKey')}
            aria-label={t('publishing.retailerKey')}
            onChange={(e) =>
              edit({
                retailers: value.retailers.map((r, i) =>
                  i === index ? { ...r, key: e.target.value } : r
                )
              })
            }
          />
          <input
            className="inspector-input"
            value={retailer.name}
            placeholder={t('publishing.retailerName')}
            aria-label={t('publishing.retailerName')}
            onChange={(e) =>
              edit({
                retailers: value.retailers.map((r, i) =>
                  i === index ? { ...r, name: e.target.value } : r
                )
              })
            }
          />
          <input
            className="inspector-input"
            value={retailer.url}
            placeholder={t('publishing.retailerUrl')}
            aria-label={t('publishing.retailerUrl')}
            onChange={(e) =>
              edit({
                retailers: value.retailers.map((r, i) =>
                  i === index ? { ...r, url: e.target.value } : r
                )
              })
            }
          />
          <input
            className="inspector-input"
            value={retailer.productId}
            placeholder={t('publishing.retailerProductId')}
            aria-label={t('publishing.retailerProductId')}
            onChange={(e) =>
              edit({
                retailers: value.retailers.map((r, i) =>
                  i === index ? { ...r, productId: e.target.value } : r
                )
              })
            }
          />
          <button
            className="binder-row-action"
            aria-label={t('publishing.retailerRemove')}
            title={t('publishing.retailerRemove')}
            onClick={() =>
              edit({ retailers: value.retailers.filter((_, i) => i !== index) })
            }
          >
            &times;
          </button>
        </div>
      ))}
      <button
        className="btn-secondary"
        onClick={() =>
          edit({
            retailers: [...value.retailers, { key: '', name: '', url: '', productId: '' }]
          })
        }
      >
        {t('publishing.retailerAdd')}
      </button>

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
