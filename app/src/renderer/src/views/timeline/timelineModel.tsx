import { rpc } from '../../rpc/client'

import { useShellStore } from '../../stores/shellStore'

import { useProjectStore } from '../../stores/projectStore'
import { useWikiStore } from '../../stores/wikiStore'

import { type TimelineEventDraft } from './TimelineEventEditor'
import type { useTimelineData } from './TimelineView'

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
  /** End of the span as written; empty for something instantaneous. */
  endDateStr: string
  /** The end sortable, or null when it cannot be read. */
  sortEndDate: string | null
  /** Timelines this event sits on. Empty means the first one. */
  timelineIds?: string[]
  /** The event this one hangs off, or empty. */
  dependsOnEventId?: string
  dependsOnOffsetDays?: number
  dependsOnFrom?: string
  dateLocked?: boolean
}

export interface TimelineEntityLink {
  name: string
  entityId: string
  typeKey: string
}

export interface TimelineTrack {
  id: string
  name: string
}

export interface TimelineDto {
  viewMode: string
  zoomLevel: string
  groups: { key: string; label: string; events: TimelineEventDto[] }[]
  entityLinks: TimelineEntityLink[]
  /** The project's named timelines. Always at least one. */
  timelines: TimelineTrack[]
  /** The one being shown, or empty for all of them at once. */
  activeTimelineId: string
}

export const ZOOMS = ['year', 'month', 'day']

export const pad = (n: number, len = 2): string => String(n).padStart(len, '0')

// Mirrors TimelineRpc.GroupKey so a date maps to the same client-side group.
export const groupKeyForDate = (d: Date, zoom: string): string => {
  const y = d.getFullYear()
  if (zoom === 'year') return `${y}`
  if (zoom === 'day') return `${y}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`
  return `${y}-${pad(d.getMonth() + 1)}`
}

// Group key back to a comparable timestamp; null for the "no-date" bucket.
export const parseGroupKey = (key: string): number | null => {
  if (key === 'no-date') return null
  const [y, mo, day] = key.split('-').map(Number)
  if (Number.isNaN(y)) return null
  return new Date(y, (mo || 1) - 1, day || 1).getTime()
}

export const toInputValue = (d: Date): string =>
  `${pad(d.getFullYear(), 4)}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`

export type Pending =
  | { kind: 'create' }
  | { kind: 'edit'; event: TimelineEventDto }
  | { kind: 'delete'; event: TimelineEventDto }

/**
 * The manual events an event could hang its date off: every other one.
 *
 * Never itself - an event waiting on itself is a dependency that can never
 * resolve, and it is the easiest one to pick by accident.
 */
export function anchors(data: TimelineDto, selfId: string | null): { id: string; title: string }[] {
  return data.groups
    .flatMap((g) => g.events)
    .filter((e) => e.isManual)
    .map((e) => ({ id: e.id.replace(/^manual-/, ''), title: e.title }))
    .filter((e) => e.id !== selfId)
}

type TimelineReadyState = ReturnType<typeof useTimelineData> & { data: TimelineDto }

function timelineMutations({ setData, setAddingTimeline }: TimelineReadyState) {
  const setView = async (viewMode: string, zoomLevel: string): Promise<void> => {
    await rpc.request('timeline/setView', [viewMode, zoomLevel])
    setData(await rpc.request<TimelineDto>('timeline/get'))
  }

  // Backstory and the manuscript's own dates shared one stream, so a war
  // three hundred years before chapter one sat between two scenes of a
  // Tuesday. Picking a timeline is what separates them.
  const chooseTimeline = async (value: string): Promise<void> => {
    if (value === '__add') {
      setAddingTimeline(true)
      return
    }
    setData(await rpc.request<TimelineDto>('timeline/setActiveTimeline', [value]))
  }

  const save = async (draft: TimelineEventDraft, id: string | null): Promise<void> => {
    setData(
      await rpc.request<TimelineDto>('timeline/saveEvent', [
        id,
        draft.title,
        draft.date,
        draft.description,
        draft.categoryId,
        draft.linkedChapterGuid,
        draft.characters,
        draft.locations,
        draft.endDate,
        draft.timelineIds,
        draft.dependsOnEventId,
        draft.dependsOnOffsetDays,
        draft.dependsOnFrom,
        draft.dateLocked
      ])
    )
  }

  const manualId = (event: TimelineEventDto): string => event.id.replace(/^manual-/, '')

  return { chooseTimeline, setView, save, manualId }
}

function timelineEventSpans({ data }: TimelineReadyState) {
  const allEvents = data.groups.flatMap((g) => g.events)

  /**
   * The earliest and latest readable dates in the book, which every span bar is
   * measured against. Two bars only mean something next to each other if they
   * share a scale, and the scale that makes sense is the whole story.
   */
  const bookSpan = ((): { start: number; end: number } | null => {
    const stamps = allEvents
      .flatMap((e) => [e.sortDate, e.sortEndDate])
      .filter((d): d is string => !!d)
      .map((d) => Date.parse(d))
      .filter((n) => !Number.isNaN(n))
    if (stamps.length < 2) return null
    const start = Math.min(...stamps)
    const end = Math.max(...stamps)
    return end > start ? { start, end } : null
  })()

  const availableCharacters = [...new Set(allEvents.flatMap((e) => e.characters))].sort()
  const availableLocations = [...new Set(allEvents.flatMap((e) => e.locations))].sort()

  /**
   * Where an event's span sits inside the whole book's span, as percentages.
   *
   * Drawn against the book rather than against the group it is in, because the
   * question a Gantt row answers is "how much of the story does this cover" -
   * and two bars only mean something next to each other if they share a scale.
   * Returns null for anything instantaneous or undated.
   */
  const spanOf = (event: TimelineEventDto): { offset: number; width: number } | null => {
    if (!event.sortDate || !event.sortEndDate || !bookSpan) return null
    const start = Date.parse(event.sortDate)
    const end = Date.parse(event.sortEndDate)
    if (Number.isNaN(start) || Number.isNaN(end) || end <= start) return null

    const total = bookSpan.end - bookSpan.start
    if (total <= 0) return null
    return {
      offset: Math.max(0, ((start - bookSpan.start) / total) * 100),
      // A one-day span in a ten-year book rounds to nothing, so every real
      // span keeps a sliver: an invisible bar reads as no bar at all.
      width: Math.max(1.5, Math.min(100, ((end - start) / total) * 100))
    }
  }

  return { availableCharacters, availableLocations, spanOf }
}

function timelineEntityChips({ data, t }: TimelineReadyState) {
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
  return { renderChip }
}

function timelineLanes({ data, sourceFilter, characterFilter, locationFilter, laneBy, plotlineNames, t }: TimelineReadyState) {
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

  return { matchesFilters, lanes }
}

function timelineNavigation({ data, setAnchorDate, groupRefs, setHighlightedKey, anchorDate }: TimelineReadyState) {
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

  return { pan, scrollToDate, openLinkedChapter }
}

export function timelinePresentation(state: TimelineReadyState) {
  return {
    ...state,
    ...timelineMutations(state),
    ...timelineEventSpans(state),
    ...timelineEntityChips(state),
    ...timelineLanes(state),
    ...timelineNavigation(state)
  }
}
