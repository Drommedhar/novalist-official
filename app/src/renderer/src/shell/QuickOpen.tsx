import { useEffect, useMemo, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { rpc } from '../rpc/client'
import { useShellStore } from '../stores/shellStore'
import { useProjectStore } from '../stores/projectStore'
import { useWikiStore } from '../stores/wikiStore'

export interface GlobalSearchHit {
  kind: string
  title: string
  subtitle: string | null
  snippet: string | null
  chapterGuid: string | null
  sceneId: string | null
  entityTypeKey: string | null
  entityId: string | null
  researchId: string | null
}

// Display order and heading for each result kind.
const KIND_ORDER: { kind: string; labelKey: string }[] = [
  { kind: 'scene', labelKey: 'quickOpen.kindScene' },
  { kind: 'entity', labelKey: 'quickOpen.kindEntity' },
  { kind: 'sceneText', labelKey: 'quickOpen.kindSceneText' },
  { kind: 'sceneNote', labelKey: 'quickOpen.kindSceneNote' },
  { kind: 'annotation', labelKey: 'quickOpen.kindAnnotation' },
  { kind: 'research', labelKey: 'quickOpen.kindResearch' },
  { kind: 'timeline', labelKey: 'quickOpen.kindTimeline' }
]

/**
 * One search box over everything the writer has written. Unlike Find & Replace
 * (scene prose only) this also reaches Codex entries, synopses and notes,
 * comments and footnotes, research, and manual timeline events — and opening a
 * result navigates straight to it.
 */
export function QuickOpen({ onClose }: { onClose(): void }): React.JSX.Element {
  const { t } = useTranslation()
  const [query, setQuery] = useState('')
  const [hits, setHits] = useState<GlobalSearchHit[]>([])
  const [loading, setLoading] = useState(false)
  const [index, setIndex] = useState(0)
  const inputRef = useRef<HTMLInputElement>(null)
  const requestSeq = useRef(0)

  useEffect(() => inputRef.current?.focus(), [])

  // Debounced query: searching scans every scene file, so we wait for a pause.
  useEffect(() => {
    const needle = query.trim()
    if (needle.length < 2) {
      setHits([])
      setLoading(false)
      return
    }
    setLoading(true)
    const seq = ++requestSeq.current
    const handle = window.setTimeout(() => {
      void rpc
        .request<GlobalSearchHit[]>('search/global', [needle, 20])
        .then((result) => {
          // Drop responses overtaken by a newer keystroke.
          if (seq !== requestSeq.current) return
          setHits(result)
          setIndex(0)
          setLoading(false)
        })
        .catch(() => {
          if (seq === requestSeq.current) setLoading(false)
        })
    }, 250)
    return () => window.clearTimeout(handle)
  }, [query])

  // Flatten into render order, keeping a flat index for keyboard navigation.
  const ordered = useMemo(() => {
    const groups: { labelKey: string; items: GlobalSearchHit[] }[] = []
    for (const { kind, labelKey } of KIND_ORDER) {
      const items = hits.filter((h) => h.kind === kind)
      if (items.length > 0) groups.push({ labelKey, items })
    }
    return groups
  }, [hits])

  const flat = useMemo(() => ordered.flatMap((g) => g.items), [ordered])

  const open = (hit: GlobalSearchHit): void => {
    onClose()
    if (hit.chapterGuid && hit.sceneId) {
      void useProjectStore.getState().openScene(hit.chapterGuid, hit.sceneId)
      return
    }
    if (hit.entityTypeKey && hit.entityId) {
      useShellStore.getState().setMainView('wiki')
      void useWikiStore.getState().openArticle(hit.entityTypeKey, hit.entityId)
      return
    }
    if (hit.researchId) {
      useShellStore.getState().navigateToResearch(hit.researchId)
      return
    }
    useShellStore.getState().setMainView('timeline')
  }

  const showEmpty = !loading && query.trim().length >= 2 && flat.length === 0

  return (
    <div
      className="dialog-overlay palette-overlay"
      onPointerDown={(e) => e.target === e.currentTarget && onClose()}
    >
      <div className="dialog-card palette-card" role="dialog" aria-label={t('quickOpen.placeholder')}>
        <input
          ref={inputRef}
          className="dialog-input"
          // The syntax is the feature; a placeholder that shows it is the
          // only discovery a search box gets.
          placeholder={t('quickOpen.placeholderStructured')}
          value={query}
          onChange={(e) => setQuery(e.target.value)}
          onKeyDown={(e) => {
            if (e.key === 'Escape') onClose()
            if (e.key === 'ArrowDown') {
              e.preventDefault()
              setIndex((i) => Math.min(i + 1, flat.length - 1))
            }
            if (e.key === 'ArrowUp') {
              e.preventDefault()
              setIndex((i) => Math.max(i - 1, 0))
            }
            if (e.key === 'Enter' && flat[index]) open(flat[index])
          }}
        />
        <div className="palette-results">
          {loading && <p className="codex-empty">{t('quickOpen.searching')}</p>}
          {showEmpty && <p className="codex-empty">{t('quickOpen.noResults')}</p>}
          {!loading && query.trim().length < 2 && (
            <p className="codex-empty">{t('quickOpen.hint')}</p>
          )}
          {ordered.map((group) => (
            <div key={group.labelKey} className="quickopen-group">
              <div className="quickopen-group-label">{t(group.labelKey)}</div>
              {group.items.map((hit) => {
                const flatIndex = flat.indexOf(hit)
                return (
                  <button
                    key={`${hit.kind}-${hit.entityId ?? hit.researchId ?? hit.sceneId ?? ''}-${flatIndex}`}
                    className={`palette-item quickopen-item${flatIndex === index ? ' active' : ''}`}
                    onClick={() => open(hit)}
                    onPointerEnter={() => setIndex(flatIndex)}
                  >
                    <span className="quickopen-main">
                      <span className="quickopen-title">{hit.title}</span>
                      {hit.subtitle && <span className="quickopen-subtitle">{hit.subtitle}</span>}
                    </span>
                    {hit.snippet && <span className="quickopen-snippet">{hit.snippet}</span>}
                  </button>
                )
              })}
            </div>
          ))}
        </div>
      </div>
    </div>
  )
}
