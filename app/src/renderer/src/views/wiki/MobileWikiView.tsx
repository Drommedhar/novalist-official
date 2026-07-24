import { useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Search, ChevronLeft } from 'lucide-react'
import { useWikiStore, type WikiScopeGroup } from '../../stores/wikiStore'
import { WikiArticle } from './WikiArticle'
import { WikiScopeSection } from './WikiView'
import './wiki.css'

/** Filters the index by name/subtitle/alias, dropping empty groups and scopes. */
function filterIndex(index: WikiScopeGroup[] | null, filter: string): WikiScopeGroup[] | null {
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
}

/**
 * Phone-sized Wiki: a full-width index list that drills down into a single
 * full-screen article with a back button, instead of the desktop two-pane
 * layout. Shown inside the mobile Codex tab's Codex/Wiki toggle.
 */
export function MobileWikiView(): React.JSX.Element {
  const { t } = useTranslation()
  const index = useWikiStore((s) => s.index)
  const loading = useWikiStore((s) => s.loading)
  const currentId = useWikiStore((s) => s.currentId)
  const article = useWikiStore((s) => s.article)
  const articleLoading = useWikiStore((s) => s.articleLoading)
  const loadIndex = useWikiStore((s) => s.loadIndex)
  const openArticle = useWikiStore((s) => s.openArticle)
  const clear = useWikiStore((s) => s.clear)
  const [filter, setFilter] = useState('')

  // Start on the list, not an auto-opened article.
  useEffect(() => {
    void loadIndex(false)
  }, [loadIndex])

  const shown = useMemo(() => filterIndex(index, filter), [index, filter])
  const isEmpty = index != null && index.length === 0
  const noMatches = !isEmpty && shown != null && shown.length === 0

  // Drilled into an article.
  if (currentId) {
    return (
      <div className="wiki-mobile">
        <div className="wiki-mobile-bar">
          <button type="button" className="wiki-mobile-back" onClick={() => clear()}>
            <ChevronLeft size={18} strokeWidth={2} />
            {t('shell.view.wiki')}
          </button>
        </div>
        <div className="wiki-mobile-article">
          {article && <WikiArticle article={article} />}
          {!article && articleLoading && <div className="wiki-main-status">{t('wiki.loading')}</div>}
        </div>
      </div>
    )
  }

  // The index list.
  return (
    <div className="wiki-index wiki-index-mobile" aria-label={t('wiki.index')}>
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
      {shown?.map((scope) => (
        <WikiScopeSection
          key={scope.isWorldBible ? 'wb' : 'book'}
          scope={scope}
          scopeLabel={scope.isWorldBible ? t('wiki.scopeWorldBible') : t('wiki.scopeBook')}
          currentId={currentId}
          onOpen={openArticle}
        />
      ))}
    </div>
  )
}
