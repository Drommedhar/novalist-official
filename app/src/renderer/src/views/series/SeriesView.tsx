import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { rpc } from '../../rpc/client'
import { useShellStore } from '../../stores/shellStore'
import './series.css'

interface SeriesBookDto {
  id: string
  name: string
  /** Who wrote this one, when that is not who wrote the project. */
  author: string
  chapters: number
  scenes: number
  words: number
  stagedScenes: number
}

interface SeriesEntityDto {
  id: string
  name: string
  bookIds: string[]
  bookCount: number
}

interface SeriesOverviewDto {
  books: SeriesBookDto[]
  entities: SeriesEntityDto[]
}

/**
 * The project above the book.
 *
 * Every analytical read path in Novalist goes through the book that is open,
 * so a World Bible character in a trilogy showed one book's appearances and a
 * writer planning a series had nowhere to see the series. This reads all the
 * books, which means opening each one in turn - so it is loaded on demand
 * rather than kept live.
 */
export function SeriesView(): React.JSX.Element {
  const { t } = useTranslation()
  const mainView = useShellStore((s) => s.mainView)
  const [overview, setOverview] = useState<SeriesOverviewDto | null>(null)

  useEffect(() => {
    if (mainView !== 'series') return
    void rpc
      .request<SeriesOverviewDto>('series/overview')
      .then(setOverview)
      .catch(() => setOverview(null))
  }, [mainView])

  if (!overview) return <div className="main-placeholder">{t('shell.backendConnecting')}</div>

  const total = overview.books.length

  return (
    <div className="dashboard series">
      <h1 className="dashboard-title">{t('series.title')}</h1>
      <p className="settings-hint">{t('series.intro')}</p>

      <div className="dashboard-card">
        <div className="git-section-header">
          <span className="git-section-title">{t('series.books')}</span>
        </div>
        {overview.books.map((book) => (
          <div key={book.id} className="series-book">
            <span className="series-book-name">{book.name}</span>
            <span className="series-book-meta">
              {book.chapters} {t('series.chapters')} - {book.scenes} {t('series.scenes')} -{' '}
              {book.words.toLocaleString()} {t('series.words')} - {book.stagedScenes}{' '}
              {t('series.staged')}
            </span>
            {/* An anthology's volumes are by different people. Left empty, the
                book goes out under the project's author, which is what every
                book that is not part of a collection wants. */}
            <input
              className="dialog-input series-book-author"
              defaultValue={book.author}
              placeholder={t('series.bookAuthorPlaceholder')}
              aria-label={t('series.bookAuthor')}
              onBlur={(e) => {
                if (e.target.value === book.author) return
                void rpc
                  .request<SeriesOverviewDto>('series/setBookAuthor', [book.id, e.target.value])
                  .then(setOverview)
              }}
            />
          </div>
        ))}
        {total === 1 && <p className="settings-hint">{t('series.oneBook')}</p>}
      </div>

      <div className="dashboard-card">
        <div className="git-section-header">
          <span className="git-section-title">{t('series.appearances')}</span>
        </div>
        <p className="settings-hint">{t('series.appearancesIntro')}</p>

        {overview.entities.length === 0 && <p className="codex-empty">{t('series.empty')}</p>}

        {overview.entities.map((entity) => (
          <div key={entity.id} className="series-entity">
            <span className="series-entity-name">{entity.name}</span>
            <span className="series-entity-books">
              {overview.books.map((book) => (
                <span
                  key={book.id}
                  className={`series-cell${entity.bookIds.includes(book.id) ? ' present' : ''}`}
                  title={book.name}
                />
              ))}
            </span>
            <span className="series-book-meta">
              {t('series.inBooks', { count: entity.bookCount, total })}
            </span>
          </div>
        ))}
      </div>
    </div>
  )
}
