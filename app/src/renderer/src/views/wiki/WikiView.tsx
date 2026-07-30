import { useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Search } from 'lucide-react'
import { useWikiStore, type WikiScopeGroup } from '../../stores/wikiStore'
import { WikiArticle } from './WikiArticle'
import { WikiPageArticle, WikiPageIndex, type WikiPage } from './WikiPages'
import { rpc } from '../../rpc/client'
import { imageSrc } from './WikiInfobox'
import './wiki.css'

const BUILT_IN_TYPE_KEYS: Record<string, string> = {
  character: 'codexHub.characters',
  location: 'codexHub.locations',
  item: 'codexHub.items',
  lore: 'codexHub.lore'
}

/** Read-only, Wikipedia-style reader over the Codex: a scope/type index on the
 * left, the selected entity's article on the right. */
export function WikiView(): React.JSX.Element {
  const { t } = useTranslation()
  const index = useWikiStore((s) => s.index)
  const loading = useWikiStore((s) => s.loading)
  const currentId = useWikiStore((s) => s.currentId)
  const article = useWikiStore((s) => s.article)
  const articleLoading = useWikiStore((s) => s.articleLoading)
  const loadIndex = useWikiStore((s) => s.loadIndex)
  const openArticle = useWikiStore((s) => s.openArticle)
  const clearArticle = useWikiStore((s) => s.clear)
  const [filter, setFilter] = useState('')
  // Articles about the world rather than about one entry in it. Kept beside
  // the generated index rather than inside it: these are written, not derived.
  const [pages, setPages] = useState<WikiPage[]>([])
  const [pageId, setPageId] = useState<string | null>(null)

  useEffect(() => {
    void loadIndex()
    void rpc
      .request<WikiPage[]>('pages/list')
      .then(setPages)
      .catch(() => setPages([]))
  }, [loadIndex])

  const openPage = (id: string): void => {
    setPageId(id)
    // Opening a written page closes the generated one, so the reader is never
    // looking at two articles and wondering which is selected. Cleared rather
    // than opened with empty ids, which would ask the backend for an article
    // that cannot exist.
    clearArticle()
  }

  const createPage = (parentId: string): void => {
    void rpc
      .request<WikiPage[]>('pages/save', [null, '', '', parentId])
      .then((next) => {
        setPages(next)
        const made = next.find((p) => p.title === '' && p.parentId === parentId)
        if (made) setPageId(made.id)
      })
  }

  const openPageRecord = pages.find((p) => p.id === pageId) ?? null

  // Filter the index by name, subtitle, or alias; groups and scopes that end up
  // empty drop out so the list stays tight while typing.
  const shown = useMemo(() => {
    const needle = filter.trim().toLowerCase()
    if (!index || needle.length === 0) return index
    return index
      .map((scope) => ({
        ...scope,
        types: scope.types
          .map((group) => ({
            ...group,
            entries: group.entries.filter(
              (e) =>
                e.title.toLowerCase().includes(needle) ||
                (e.subtitle?.toLowerCase().includes(needle) ?? false) ||
                e.aliases.some((a) => a.toLowerCase().includes(needle))
            )
          }))
          .filter((group) => group.entries.length > 0)
      }))
      .filter((scope) => scope.types.length > 0)
  }, [index, filter])

  const isEmpty = index != null && index.length === 0
  const noMatches = !isEmpty && shown != null && shown.length === 0

  return (
    <div className="wiki-view">
      <aside className="wiki-index" aria-label={t('wiki.index')}>
        <div className="wiki-index-title">{t('shell.view.wiki')}</div>
        {!isEmpty && index != null && (
          <div className="wiki-index-search">
            <Search size={13} strokeWidth={1.75} aria-hidden="true" />
            <input
              type="search"
              value={filter}
              placeholder={t('wiki.filterPlaceholder')}
              aria-label={t('wiki.filterPlaceholder')}
              onChange={(e) => setFilter(e.target.value)}
            />
          </div>
        )}
        {loading && !index && <div className="wiki-index-status">{t('wiki.loading')}</div>}
        {isEmpty && <div className="wiki-index-status">{t('wiki.empty')}</div>}
        {noMatches && <div className="wiki-index-status">{t('wiki.noMatches')}</div>}
        {/* Written articles first: they are about the world as a whole, and
            the generated entries are about the things inside it. */}
        <WikiPageIndex
          pages={pages}
          currentId={openPageRecord ? openPageRecord.id : null}
          onOpen={openPage}
          onCreate={createPage}
        />
        {shown?.map((scope) => (
          <WikiScopeSection
            key={scope.isWorldBible ? 'wb' : 'book'}
            scope={scope}
            scopeLabel={scope.isWorldBible ? t('wiki.scopeWorldBible') : t('wiki.scopeBook')}
            currentId={currentId}
            onOpen={async (type, id) => {
              // ...and the same the other way, so exactly one is selected.
              setPageId(null)
              await openArticle(type, id)
            }}
          />
        ))}
      </aside>

      <div className="wiki-main">
        {openPageRecord && (
          <WikiPageArticle
            page={openPageRecord}
            pages={pages}
            onChanged={setPages}
            onClosed={() => setPageId(null)}
          />
        )}
        {!openPageRecord && article && <WikiArticle article={article} />}
        {!openPageRecord && !article && articleLoading && (
          <div className="wiki-main-status">{t('wiki.loading')}</div>
        )}
        {!openPageRecord && !article && !articleLoading && isEmpty && (
          <div className="wiki-main-status">{t('wiki.empty')}</div>
        )}
      </div>
    </div>
  )
}

export function WikiScopeSection({
  scope,
  scopeLabel,
  currentId,
  onOpen
}: {
  scope: WikiScopeGroup
  scopeLabel: string
  currentId: string | null
  onOpen: (type: string, id: string) => Promise<void>
}): React.JSX.Element {
  const { t } = useTranslation()
  return (
    <div className="wiki-scope">
      <div className="wiki-scope-label">{scopeLabel}</div>
      {scope.types.map((group) => {
        const label = group.customTypeLabel ?? t(BUILT_IN_TYPE_KEYS[group.typeKey] ?? group.typeKey)
        return (
          <div className="wiki-type-group" key={group.typeKey}>
            <div className="wiki-type-label">{label}</div>
            <ul className="wiki-entry-list">
              {group.entries.map((entry) => (
                <li key={entry.id}>
                  <button
                    type="button"
                    className={`wiki-entry${currentId === entry.id ? ' active' : ''}`}
                    onClick={() => void onOpen(entry.typeKey, entry.id)}
                  >
                    {entry.imageUrl ? (
                      <img className="wiki-entry-thumb" src={imageSrc(entry.imageUrl)} alt="" />
                    ) : (
                      <span className="wiki-entry-thumb wiki-entry-thumb-empty" aria-hidden="true" />
                    )}
                    <span className="wiki-entry-text">
                      <span className="wiki-entry-title">{entry.title}</span>
                      {entry.subtitle && (
                        <span className="wiki-entry-subtitle">{entry.subtitle}</span>
                      )}
                    </span>
                  </button>
                </li>
              ))}
            </ul>
          </div>
        )
      })}
    </div>
  )
}
