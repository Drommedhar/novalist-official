import { useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { ArrowLeftRight, ChevronLeft, ChevronRight, FileDown, Plus, ZoomIn } from 'lucide-react'
import { rpc } from '../../rpc/client'
import { useShellStore } from '../../stores/shellStore'
import { useProjectStore } from '../../stores/projectStore'
import { ConfirmDialog } from '../../shell/ConfirmDialog'
import { TimelineEventEditor, type TimelineEventDraft } from './TimelineEventEditor'
import './timeline.css'

export interface TimelineEventDto {
  id: string
  title: string
  dateStr: string
  sortDate: string | null
  description: string
  source: 'act' | 'chapter' | 'scene' | 'manual'
  categoryId: string | null
  chapterGuid: string | null
  sceneId: string | null
  characters: string[]
  locations: string[]
  isManual: boolean
}

interface TimelineDto {
  viewMode: string
  zoomLevel: string
  groups: { key: string; label: string; events: TimelineEventDto[] }[]
}

const ZOOMS = ['year', 'month', 'day']

const pad = (n: number, len = 2): string => String(n).padStart(len, '0')

// Mirrors TimelineRpc.GroupKey so a date maps to the same client-side group.
const groupKeyForDate = (d: Date, zoom: string): string => {
  const y = d.getFullYear()
  if (zoom === 'year') return `${y}`
  if (zoom === 'day') return `${y}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`
  return `${y}-${pad(d.getMonth() + 1)}`
}

// Group key back to a comparable timestamp; null for the "no-date" bucket.
const parseGroupKey = (key: string): number | null => {
  if (key === 'no-date') return null
  const [y, mo, day] = key.split('-').map(Number)
  if (Number.isNaN(y)) return null
  return new Date(y, (mo || 1) - 1, day || 1).getTime()
}

const toInputValue = (d: Date): string =>
  `${pad(d.getFullYear(), 4)}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`

type Pending =
  | { kind: 'create' }
  | { kind: 'edit'; event: TimelineEventDto }
  | { kind: 'delete'; event: TimelineEventDto }

export function TimelineView(): React.JSX.Element {
  const { t } = useTranslation()
  const mainView = useShellStore((s) => s.mainView)
  const [data, setData] = useState<TimelineDto | null>(null)
  const [pending, setPending] = useState<Pending | null>(null)
  const [sourceFilter, setSourceFilter] = useState('all')
  const [characterFilter, setCharacterFilter] = useState('')
  const [locationFilter, setLocationFilter] = useState('')
  const [structures, setStructures] = useState<
    { id: string; displayName: string; description: string }[]
  >([])
  const [anchorDate, setAnchorDate] = useState<Date | null>(null)
  const [highlightedKey, setHighlightedKey] = useState<string | null>(null)
  const groupRefs = useRef<Map<string, HTMLDivElement>>(new Map())

  useEffect(() => {
    if (mainView !== 'timeline') return
    void rpc.request<TimelineDto>('timeline/get').then(setData)
    void rpc
      .request<{ id: string; displayName: string; description: string }[]>(
        'timeline/structureTemplates'
      )
      .then(setStructures)
      .catch(() => setStructures([]))
  }, [mainView])

  if (!data) return <div className="main-placeholder">{t('shell.backendConnecting')}</div>

  const setView = async (viewMode: string, zoomLevel: string): Promise<void> => {
    await rpc.request('timeline/setView', [viewMode, zoomLevel])
    setData(await rpc.request<TimelineDto>('timeline/get'))
  }

  const save = async (draft: TimelineEventDraft, id: string | null): Promise<void> => {
    setData(
      await rpc.request<TimelineDto>('timeline/saveEvent', [
        id,
        draft.title,
        draft.date,
        draft.description,
        draft.categoryId,
        draft.linkedChapterGuid
      ])
    )
  }

  const manualId = (event: TimelineEventDto): string => event.id.replace(/^manual-/, '')

  const allEvents = data.groups.flatMap((g) => g.events)
  const availableCharacters = [...new Set(allEvents.flatMap((e) => e.characters))].sort()
  const availableLocations = [...new Set(allEvents.flatMap((e) => e.locations))].sort()
  const matchesFilters = (event: TimelineEventDto): boolean =>
    (sourceFilter === 'all' || event.source === sourceFilter) &&
    (!characterFilter || event.characters.includes(characterFilter)) &&
    (!locationFilter || event.locations.includes(locationFilter))

  // Scroll the visible window to the group matching a date; falls back to the
  // nearest dated group when nothing sits in that exact bucket.
  const scrollToDate = (date: Date): void => {
    setAnchorDate(date)
    const exactKey = groupKeyForDate(date, data.zoomLevel)
    let targetKey = groupRefs.current.has(exactKey) ? exactKey : null
    if (!targetKey) {
      const target = date.getTime()
      let bestDiff = Infinity
      for (const group of data.groups) {
        const t = parseGroupKey(group.key)
        if (t === null) continue
        const diff = Math.abs(t - target)
        if (diff < bestDiff) {
          bestDiff = diff
          targetKey = group.key
        }
      }
    }
    if (!targetKey) return
    setHighlightedKey(targetKey)
    groupRefs.current
      .get(targetKey)
      ?.scrollIntoView({ behavior: 'smooth', block: 'start', inline: 'start' })
  }

  const pan = (direction: number): void => {
    const next = new Date(anchorDate ?? new Date())
    if (data.zoomLevel === 'year') next.setFullYear(next.getFullYear() + direction)
    else if (data.zoomLevel === 'day') next.setDate(next.getDate() + direction)
    else next.setMonth(next.getMonth() + direction)
    scrollToDate(next)
  }

  const openLinkedChapter = (chapterGuid: string): void => {
    const chapter = useProjectStore.getState().chapters.find((c) => c.guid === chapterGuid)
    const firstScene = chapter?.scenes[0]
    if (firstScene) void useProjectStore.getState().openScene(chapterGuid, firstScene.id)
  }

  return (
    <div className="timeline">
      <div className="timeline-toolbar">
        <button className="toolbar-button toolbar-action" onClick={() => setPending({ kind: 'create' })}>
          <Plus size={14} strokeWidth={2} />
          {t('timeline.addEvent')}
        </button>
        <button
          className="toolbar-button toolbar-action"
          onClick={() =>
            void (async () => {
              const output = await window.novalist.saveFile('outline.md')
              if (output) await rpc.request('export/timelineOutline', [output])
            })()
          }
        >
          <FileDown size={14} strokeWidth={2} />
          {t('timeline.exportOutline')}
        </button>
        <select
          className="dialog-input findreplace-scope"
          value=""
          aria-label={t('timeline.applyStructure')}
          onChange={(e) => {
            const id = e.target.value
            if (!id) return
            void rpc.request<TimelineDto>('timeline/applyStructureTemplate', [id]).then(setData)
          }}
        >
          <option value="">{t('timeline.applyStructure')}</option>
          {structures.map((s) => (
            <option key={s.id} value={s.id} title={s.description}>
              {s.displayName}
            </option>
          ))}
        </select>
        <div className="toolbar-spacer" />
        {availableCharacters.length > 0 && (
          <select
            className="dialog-input findreplace-scope"
            value={characterFilter}
            onChange={(e) => setCharacterFilter(e.target.value)}
          >
            <option value="">{t('timeline.filterCharacter')}</option>
            {availableCharacters.map((name) => (
              <option key={name} value={name}>
                {name}
              </option>
            ))}
          </select>
        )}
        {availableLocations.length > 0 && (
          <select
            className="dialog-input findreplace-scope"
            value={locationFilter}
            onChange={(e) => setLocationFilter(e.target.value)}
          >
            <option value="">{t('timeline.filterLocation')}</option>
            {availableLocations.map((name) => (
              <option key={name} value={name}>
                {name}
              </option>
            ))}
          </select>
        )}
        <select
          className="dialog-input findreplace-scope"
          value={sourceFilter}
          onChange={(e) => setSourceFilter(e.target.value)}
        >
          {['all', 'act', 'chapter', 'scene', 'manual'].map((s) => (
            <option key={s} value={s}>
              {s === 'all' ? t('timeline.filterSource') : t(`timeline.source${s.charAt(0).toUpperCase()}${s.slice(1)}`)}
            </option>
          ))}
        </select>
        <button
          className="toolbar-button toolbar-action"
          onClick={() =>
            void setView(data.viewMode === 'vertical' ? 'horizontal' : 'vertical', data.zoomLevel)
          }
        >
          <ArrowLeftRight size={14} strokeWidth={2} />
          {data.viewMode === 'vertical' ? t('timeline.viewVertical') : t('timeline.viewHorizontal')}
        </button>
        <button
          className="toolbar-button toolbar-action"
          onClick={() =>
            void setView(
              data.viewMode,
              ZOOMS[(ZOOMS.indexOf(data.zoomLevel) + 1) % ZOOMS.length]
            )
          }
        >
          <ZoomIn size={14} strokeWidth={2} />
          {t(`timeline.zoom${data.zoomLevel.charAt(0).toUpperCase() + data.zoomLevel.slice(1)}`)}
        </button>
        <div className="timeline-nav">
          <button
            className="toolbar-button timeline-nav-arrow"
            aria-label={t('timeline.prev')}
            title={t('timeline.prev')}
            onClick={() => pan(-1)}
          >
            <ChevronLeft size={14} strokeWidth={2} />
          </button>
          <button
            className="toolbar-button timeline-nav-arrow"
            aria-label={t('timeline.next')}
            title={t('timeline.next')}
            onClick={() => pan(1)}
          >
            <ChevronRight size={14} strokeWidth={2} />
          </button>
          <button
            className="toolbar-button toolbar-action"
            onClick={() => scrollToDate(new Date())}
          >
            {t('timeline.today')}
          </button>
          <input
            type="date"
            className="dialog-input timeline-jump-input"
            aria-label={t('timeline.jumpTo')}
            title={t('timeline.jumpTo')}
            value={anchorDate ? toInputValue(anchorDate) : ''}
            onChange={(e) => {
              const v = e.target.value
              if (!v) return
              const [y, m, d] = v.split('-').map(Number)
              scrollToDate(new Date(y, m - 1, d))
            }}
          />
        </div>
      </div>
      <div className={`timeline-body ${data.viewMode}`}>
        {data.groups.map((group) => (
          <div
            key={group.key}
            className={`timeline-group${highlightedKey === group.key ? ' highlighted' : ''}`}
            ref={(el) => {
              if (el) groupRefs.current.set(group.key, el)
              else groupRefs.current.delete(group.key)
            }}
          >
            <div className="timeline-group-label">{group.label}</div>
            {group.events.filter(matchesFilters).map((event) => (
              <div
                key={event.id}
                className={`timeline-event source-${event.source}`}
                role={event.sceneId || event.isManual || event.chapterGuid ? 'button' : undefined}
                onClick={() => {
                  if (event.isManual) setPending({ kind: 'edit', event })
                  else if (event.chapterGuid && event.sceneId)
                    void useProjectStore.getState().openScene(event.chapterGuid, event.sceneId)
                  else if (event.chapterGuid) openLinkedChapter(event.chapterGuid)
                }}
                onContextMenu={(e) => {
                  if (!event.isManual) return
                  e.preventDefault()
                  setPending({ kind: 'delete', event })
                }}
              >
                <span className="timeline-event-dot" />
                <div className="timeline-event-body">
                  <div className="timeline-event-head">
                    <div className="timeline-event-title">{event.title}</div>
                    <span className={`timeline-source-pill source-${event.source}`}>
                      {t(`timeline.${event.source}Event`)}
                    </span>
                  </div>
                  {event.dateStr && <div className="timeline-event-date">{event.dateStr}</div>}
                  {event.description && (
                    <div className="timeline-event-desc">{event.description}</div>
                  )}
                  {(event.characters.length > 0 || event.locations.length > 0) && (
                    <div className="timeline-event-chips">
                      {event.characters.map((name) => (
                        <span key={`c-${name}`} className="entity-chip">
                          {name}
                        </span>
                      ))}
                      {event.locations.map((name) => (
                        <span key={`l-${name}`} className="entity-chip">
                          {name}
                        </span>
                      ))}
                    </div>
                  )}
                  {event.isManual && event.chapterGuid && (
                    <button
                      className="timeline-open-chapter"
                      onClick={(e) => {
                        e.stopPropagation()
                        if (event.chapterGuid) openLinkedChapter(event.chapterGuid)
                      }}
                    >
                      {t('timeline.openChapter')}
                    </button>
                  )}
                </div>
              </div>
            ))}
          </div>
        ))}
        {data.groups.length === 0 && <p className="codex-empty">{t('timeline.noEvents')}</p>}
      </div>
      {pending?.kind === 'create' && (
        <TimelineEventEditor
          initial={null}
          onCancel={() => setPending(null)}
          onSubmit={(draft) => {
            setPending(null)
            void save(draft, null)
          }}
        />
      )}
      {pending?.kind === 'edit' && (
        <TimelineEventEditor
          initial={pending.event}
          onCancel={() => setPending(null)}
          onDelete={() => {
            const event = pending.event
            setPending({ kind: 'delete', event })
          }}
          onSubmit={(draft) => {
            const id = manualId(pending.event)
            setPending(null)
            void save(draft, id)
          }}
        />
      )}
      {pending?.kind === 'delete' && (
        <ConfirmDialog
          title={t('explorer.deleteTitle')}
          message={pending.event.title}
          onCancel={() => setPending(null)}
          onConfirm={() => {
            const id = manualId(pending.event)
            setPending(null)
            void rpc.request<TimelineDto>('timeline/deleteEvent', [id]).then(setData)
          }}
        />
      )}
    </div>
  )
}
