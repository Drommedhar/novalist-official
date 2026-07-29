import { useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { ArrowLeftRight, ChevronLeft, ChevronRight, FileDown, Plus, ZoomIn, Milestone } from 'lucide-react'
import { rpc } from '../../rpc/client'
import { StructurePanel } from './StructurePanel'
import { useShellStore } from '../../stores/shellStore'
import { useProjectStore } from '../../stores/projectStore'
import { useWikiStore } from '../../stores/wikiStore'
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
  pov: string
  plotlineIds: string[]
  narrativeMode: string
  readingIndex: number
}

interface TimelineEntityLink {
  name: string
  entityId: string
  typeKey: string
}

interface TimelineDto {
  viewMode: string
  zoomLevel: string
  groups: { key: string; label: string; events: TimelineEventDto[] }[]
  entityLinks: TimelineEntityLink[]
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
  // Filtering hides the threads being compared, which is exactly wrong for
  // "does this POV vanish for eighty pages". Lanes show them side by side.
  const [laneBy, setLaneBy] = useState('none')
  // A flashback sorts by its date like everything else, which is right for
  // chronology and wrong for "what does the reader meet next".
  const [readingOrder, setReadingOrder] = useState(false)
  // Plotlines are stored by id; a lane headed with a GUID says nothing.
  const [plotlineNames, setPlotlineNames] = useState<Record<string, string>>({})
  const [characterFilter, setCharacterFilter] = useState('')
  const [locationFilter, setLocationFilter] = useState('')
  const [structureOpen, setStructureOpen] = useState(false)
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

  // Plotlines are stored by id; a lane headed with a GUID says nothing.
  useEffect(() => {
    void rpc
      .request<{ plotlines: { id: string; name: string }[] }>('plot/grid')
      .then((grid) =>
        setPlotlineNames(Object.fromEntries(grid.plotlines.map((p) => [p.id, p.name])))
      )
      .catch(() => setPlotlineNames({}))
  }, [])

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

  // Names the backend resolved to exactly one entity become links to its article;
  // ambiguous or unknown names stay plain chips.
  const entityLinks = new Map(data.entityLinks.map((l) => [l.name.toLowerCase(), l]))
  const openEntity = (link: TimelineEntityLink): void => {
    useShellStore.getState().setMainView('wiki')
    void useWikiStore.getState().openArticle(link.typeKey, link.entityId)
  }
  const renderChip = (name: string, key: string): React.JSX.Element => {
    const link = entityLinks.get(name.trim().toLowerCase())
    if (!link)
      return (
        <span key={key} className="entity-chip">
          {name}
        </span>
      )
    return (
      <button
        key={key}
        type="button"
        className="entity-chip entity-chip-link"
        title={t('timeline.openEntity', { name })}
        onClick={(e) => {
          e.stopPropagation()
          openEntity(link)
        }}
      >
        {name}
      </button>
    )
  }
  const matchesFilters = (event: TimelineEventDto): boolean =>
    (sourceFilter === 'all' || event.source === sourceFilter) &&
    (!characterFilter || event.characters.includes(characterFilter)) &&
    (!locationFilter || event.locations.includes(locationFilter))

  // One lane per value the chosen dimension takes, in reading order. An event
  // with several values appears in every lane it belongs to - which is the
  // point: a scene shared by two POVs is a scene where the threads meet.
  const laneValues = (event: TimelineEventDto): string[] => {
    if (laneBy === 'character') return event.characters
    if (laneBy === 'location') return event.locations
    if (laneBy === 'pov') return event.pov ? [event.pov] : []
    if (laneBy === 'plotline') return event.plotlineIds
    return []
  }

  const laneLabel = (key: string): string =>
    laneBy === 'plotline' ? (plotlineNames[key] ?? key) : key

  const lanes = ((): { key: string; label: string; events: TimelineEventDto[] }[] => {
    if (laneBy === 'none') return []
    const ordered = data.groups.flatMap((g) => g.events).filter(matchesFilters)
    const byKey = new Map<string, TimelineEventDto[]>()
    const ungrouped: TimelineEventDto[] = []
    for (const event of ordered) {
      const values = laneValues(event)
      if (values.length === 0) {
        ungrouped.push(event)
        continue
      }
      for (const value of values) {
        const list = byKey.get(value)
        if (list) list.push(event)
        else byKey.set(value, [event])
      }
    }
    return [
      ...[...byKey.entries()]
        .sort((a, b) => laneLabel(a[0]).localeCompare(laneLabel(b[0])))
        .map(([key, events]) => ({ key, label: laneLabel(key), events })),
      // Always shown: a lane view that drops everything unclassified reads as
      // though the whole book is accounted for.
      { key: '', label: t('timeline.laneUngrouped'), events: ungrouped }
    ]
  })()

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
        <button
          className={`toolbar-button toolbar-action${structureOpen ? ' active' : ''}`}
          onClick={() => setStructureOpen(!structureOpen)}
        >
          <Milestone size={14} strokeWidth={2} />
          {t('structure.title')}
        </button>
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
        <button
          className={`toolbar-button toolbar-action${readingOrder ? ' active' : ''}`}
          onClick={() => setReadingOrder(!readingOrder)}
        >
          {t(readingOrder ? 'timeline.orderReading' : 'timeline.orderChronological')}
        </button>
        <select
          className="dialog-input findreplace-scope"
          aria-label={t('timeline.lanes')}
          value={laneBy}
          onChange={(e) => setLaneBy(e.target.value)}
        >
          {['none', 'character', 'location', 'pov', 'plotline'].map((key) => (
            <option key={key} value={key}>
              {t(`timeline.lane_${key}`)}
            </option>
          ))}
        </select>
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

      {/* A sub-view of the Timeline rather than its own place: structure is
          what the timeline is about, and it has no meaning without one. */}
      {structureOpen && <StructurePanel />}
      {laneBy !== 'none' && (
        <div className="timeline-lanes">
          {lanes.map((lane) => (
            <div key={lane.key || 'ungrouped'} className="timeline-lane">
              <div className="timeline-lane-head">
                <span className="timeline-lane-title">{lane.label}</span>
                <span className="timeline-lane-count">{lane.events.length}</span>
              </div>
              <div className="timeline-lane-events">
                {lane.events.map((event) => (
                  <button
                    key={`${lane.key}-${event.id}`}
                    className={`timeline-lane-event source-${event.source}`}
                    title={event.dateStr}
                    onClick={() => {
                      if (event.isManual) setPending({ kind: 'edit', event })
                      else if (event.chapterGuid && event.sceneId)
                        void useProjectStore
                          .getState()
                          .openScene(event.chapterGuid, event.sceneId)
                      else if (event.chapterGuid) openLinkedChapter(event.chapterGuid)
                    }}
                  >
                    {event.title}
                  </button>
                ))}
              </div>
            </div>
          ))}
        </div>
      )}
      {readingOrder && laneBy === 'none' && (
        <div className="timeline-reading">
          {data.groups
            .flatMap((g) => g.events)
            .filter(matchesFilters)
            .filter((event) => event.readingIndex > 0)
            .sort((a, b) => a.readingIndex - b.readingIndex)
            .map((event) => (
              <button
                key={event.id}
                className="timeline-reading-row"
                onClick={() => {
                  if (event.chapterGuid && event.sceneId)
                    void useProjectStore.getState().openScene(event.chapterGuid, event.sceneId)
                }}
              >
                {/* R is where the reader meets it, C the date it happens on.
                    Seeing both is the only way to read a flashback correctly. */}
                <span className="timeline-order-badge">R:{event.readingIndex}</span>
                <span className="timeline-order-badge chrono">
                  {event.dateStr || t('timeline.undated')}
                </span>
                {event.narrativeMode && (
                  <span className="timeline-mode-pill">
                    {t(`timeline.mode_${event.narrativeMode}`)}
                  </span>
                )}
                <span className="timeline-reading-title">{event.title}</span>
              </button>
            ))}
        </div>
      )}
      <div
        className={`timeline-body ${data.viewMode}`}
        hidden={laneBy !== 'none' || readingOrder}
      >
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
                    {event.narrativeMode && (
                      <span className="timeline-mode-pill">
                        {t(`timeline.mode_${event.narrativeMode}`)}
                      </span>
                    )}
                  </div>
                  {event.dateStr && <div className="timeline-event-date">{event.dateStr}</div>}
                  {event.description && (
                    <div className="timeline-event-desc">{event.description}</div>
                  )}
                  {(event.characters.length > 0 || event.locations.length > 0) && (
                    <div className="timeline-event-chips">
                      {event.characters.map((name) => renderChip(name, `c-${name}`))}
                      {event.locations.map((name) => renderChip(name, `l-${name}`))}
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
