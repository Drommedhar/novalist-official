import { useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Search } from 'lucide-react'
import { useWikiStore, type WikiScopeGroup } from '../../stores/wikiStore'
import { WikiArticle } from './WikiArticle'
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
  const [filter, setFilter] = useState('')

  useEffect(() => {
    void loadIndex()
  }, [loadIndex])

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
        {shown?.map((scope) => (
          <WikiScopeSection
            key={scope.isWorldBible ? 'wb' : 'book'}
            scope={scope}
            scopeLabel={scope.isWorldBible ? t('wiki.scopeWorldBible') : t('wiki.scopeBook')}
            currentId={currentId}
            onOpen={openArticle}
          />
        ))}
      </aside>

      <div className="wiki-main">
        {article && <WikiArticle article={article} />}
        {!article && articleLoading && <div className="wiki-main-status">{t('wiki.loading')}</div>}
        {!article && !articleLoading && isEmpty && (
          <div className="wiki-main-status">{t('wiki.empty')}</div>
        )}
      </div>
    </div>
  )
}

function WikiScopeSection({
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
