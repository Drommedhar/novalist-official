import { useCallback, useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { ChevronLeft, ChevronRight } from 'lucide-react'
import { rpc } from '../../rpc/client'
import { CalendarConfigPanel } from './CalendarConfigPanel'
import { useProjectStore } from '../../stores/projectStore'
import { handleSceneClick, useSelectionStore } from '../../stores/selectionStore'
import { SceneBulkBar } from '../../shell/SceneBulkBar'
import './calendar.css'

interface CalendarEventDto {
  date: string
  chapterGuid: string
  sceneId: string
  title: string
  chapterTitle: string
  synopsis: string | null
  note: string | null
  allDay: boolean
  startHour: number
  startMinute: number
  endHour: number
  endMinute: number
}

type CalendarMode = 'week' | 'month' | 'year'

/** Pixel height of one hour row in the week timed grid. */
const HOUR_PX = 48

const iso = (d: Date): string =>
  `${d.getFullYear().toString().padStart(4, '0')}-${(d.getMonth() + 1)
    .toString()
    .padStart(2, '0')}-${d.getDate().toString().padStart(2, '0')}`

const startOfWeek = (d: Date): Date => {
  const copy = new Date(d)
  const day = (copy.getDay() + 6) % 7
  copy.setDate(copy.getDate() - day)
  return copy
}

const addDays = (d: Date, days: number): Date => {
  const copy = new Date(d)
  copy.setDate(copy.getDate() + days)
  return copy
}

const isToday = (d: Date): boolean => {
  const now = new Date()
  return (
    d.getFullYear() === now.getFullYear() &&
    d.getMonth() === now.getMonth() &&
    d.getDate() === now.getDate()
  )
}

const pad2 = (n: number): string => n.toString().padStart(2, '0')

const startMinutes = (e: CalendarEventDto): number => e.startHour * 60 + e.startMinute

const eventTime = (e: CalendarEventDto): string =>
  e.allDay ? '' : `${pad2(e.startHour)}:${pad2(e.startMinute)}`

const eventTooltip = (e: CalendarEventDto): string => {
  const parts = [e.title]
  if (e.note) parts.push(e.note)
  if (e.synopsis) parts.push(e.synopsis)
  return parts.join('\n')
}

const openScene = (e: CalendarEventDto): void => {
  void useProjectStore.getState().openScene(e.chapterGuid, e.sceneId)
}

interface TimedLayout {
  event: CalendarEventDto
  topPx: number
  heightPx: number
  leftPct: number
  widthPct: number
}

/**
 * Positions timed events in a day column: top/height by time-of-day and splits
 * overlapping events into side-by-side columns. Mirrors the Avalonia
 * CalendarDayColumn.LayoutOverlaps sweep. Fractions are resolved to percentages
 * of the day-track width.
 */
const layoutTimed = (events: CalendarEventDto[]): TimedLayout[] => {
  const items = events
    .map((event) => {
      const start = startMinutes(event)
      let end = event.endHour * 60 + event.endMinute
      if (end <= start) end = start + 60
      const topPx = (start / 60) * HOUR_PX
      const heightPx = Math.max(20, ((end - start) / 60) * HOUR_PX)
      return { event, topPx, heightPx, start: topPx, end: topPx + heightPx, leftPct: 0, widthPct: 100 }
    })
    .sort((a, b) => a.start - b.start)

  const colEnds: number[] = []
  let groupStart = 0
  let currentMaxEnd = -Infinity

  const flush = (endIdx: number): void => {
    if (endIdx <= groupStart) return
    let cols = 0
    for (let i = groupStart; i < endIdx; i++) cols = Math.max(cols, items[i].leftPct + 1)
    const widthFrac = 1 / cols
    for (let i = groupStart; i < endIdx; i++) {
      const col = items[i].leftPct
      items[i].leftPct = col * widthFrac * 100
      items[i].widthPct = widthFrac * 100
    }
    groupStart = endIdx
  }

  for (let idx = 0; idx < items.length; idx++) {
    const entry = items[idx]
    if (entry.start >= currentMaxEnd) {
      flush(idx)
      colEnds.length = 0
      currentMaxEnd = entry.end
    } else if (entry.end > currentMaxEnd) {
      currentMaxEnd = entry.end
    }

    let placedCol = -1
    for (let c = 0; c < colEnds.length; c++) {
      if (colEnds[c] <= entry.start) {
        placedCol = c
        colEnds[c] = entry.end
        break
      }
    }
    if (placedCol < 0) {
      placedCol = colEnds.length
      colEnds.push(entry.end)
    }
    // Stash the raw column index in leftPct until flush() resolves the fraction.
    entry.leftPct = placedCol
  }
  flush(items.length)

  return items
}

const HOURS = Array.from({ length: 24 }, (_, i) => i)
const WEEKDAY_KEYS = [
  'weekdayMon',
  'weekdayTue',
  'weekdayWed',
  'weekdayThu',
  'weekdayFri',
  'weekdaySat',
  'weekdaySun'
] as const
const MONTHS = Array.from({ length: 12 }, (_, i) => i)

export function CalendarView(): React.JSX.Element {
  const { t, i18n } = useTranslation()
  const [mode, setMode] = useState<CalendarMode>('week')
  const [configOpen, setConfigOpen] = useState(false)
  const [anchor, setAnchor] = useState<Date>(new Date())
  const [events, setEvents] = useState<CalendarEventDto[]>([])
  const [anchorLoaded, setAnchorLoaded] = useState(false)
  const [dragging, setDragging] = useState<CalendarEventDto | null>(null)

  const reschedule = async (target: string): Promise<void> => {
    if (!dragging) return
    const selection = useSelectionStore.getState().sceneIds
    // Dragging one chip out of a selection moves the whole selection, keeping
    // the gaps between the scenes. Dropping them all on the target day would
    // collapse a week of story into one afternoon.
    if (selection.length > 1 && selection.includes(dragging.sceneId)) {
      const days = Math.round(
        (Date.parse(target) - Date.parse(dragging.date)) / 86_400_000
      )
      if (days !== 0) await rpc.request('sceneBulk/shiftDates', [selection, days])
    } else {
      await rpc.request('calendar/reschedule', [dragging.chapterGuid, dragging.sceneId, target])
    }
    setDragging(null)
    await load()
  }

  const load = useCallback(async (): Promise<void> => {
    let from: Date
    let to: Date
    if (mode === 'week') {
      from = startOfWeek(anchor)
      to = addDays(from, 6)
    } else if (mode === 'month') {
      from = startOfWeek(new Date(anchor.getFullYear(), anchor.getMonth(), 1))
      to = addDays(from, 41)
    } else {
      from = new Date(anchor.getFullYear(), 0, 1)
      to = new Date(anchor.getFullYear(), 11, 31)
    }
    setEvents(await rpc.request<CalendarEventDto[]>('calendar/get', [iso(from), iso(to)]))
  }, [anchor, mode])

  useEffect(() => {
    if (!anchorLoaded) {
      void rpc.request<string | null>('calendar/getAnchor').then((saved) => {
        if (saved) {
          const parsed = new Date(saved)
          if (!Number.isNaN(parsed.getTime())) setAnchor(parsed)
        }
        setAnchorLoaded(true)
      })
      return
    }
    void load()
  }, [anchorLoaded, load])

  const pan = (direction: -1 | 1): void => {
    const next = new Date(anchor)
    if (mode === 'week') next.setDate(next.getDate() + 7 * direction)
    else if (mode === 'month') next.setMonth(next.getMonth() + direction)
    else next.setFullYear(next.getFullYear() + direction)
    setAnchor(next)
    void rpc.request('calendar/setAnchor', [iso(next)])
  }

  const jumpTo = (date: Date, newMode?: CalendarMode): void => {
    setAnchor(date)
    if (newMode) setMode(newMode)
    void rpc.request('calendar/setAnchor', [iso(date)])
  }

  const byDate = useMemo(() => {
    const map = new Map<string, CalendarEventDto[]>()
    for (const event of events) {
      const list = map.get(event.date) ?? []
      list.push(event)
      map.set(event.date, list)
    }
    for (const list of map.values()) {
      list.sort((a, b) =>
        a.allDay === b.allDay ? startMinutes(a) - startMinutes(b) : a.allDay ? -1 : 1
      )
    }
    return map
  }, [events])

  // Year view: dedup multi-day scenes to one entry per month, keeping order.
  const eventsByMonth = useMemo(() => {
    const map = new Map<number, CalendarEventDto[]>()
    const seen = new Map<number, Set<string>>()
    for (const event of events) {
      const month = Number(event.date.slice(5, 7))
      let list = map.get(month)
      if (!list) {
        list = []
        map.set(month, list)
      }
      let ids = seen.get(month)
      if (!ids) {
        ids = new Set()
        seen.set(month, ids)
      }
      if (!ids.has(event.sceneId)) {
        ids.add(event.sceneId)
        list.push(event)
      }
    }
    return map
  }, [events])

  const monthName = (m: number): string =>
    new Date(2000, m, 1).toLocaleString(i18n.language, { month: 'long' })

  const headerLabel =
    mode === 'week'
      ? `${iso(startOfWeek(anchor))} - ${iso(addDays(startOfWeek(anchor), 6))}`
      : mode === 'month'
        ? `${monthName(anchor.getMonth())} ${anchor.getFullYear()}`
        : String(anchor.getFullYear())

  const weekStart = startOfWeek(anchor)
  const weekDays = Array.from({ length: 7 }, (_, i) => addDays(weekStart, i))
  const monthGridStart = startOfWeek(new Date(anchor.getFullYear(), anchor.getMonth(), 1))

  return (
    <div className="calendar">
      <div className="timeline-toolbar">
        {(['week', 'month', 'year'] as CalendarMode[]).map((m) => (
          <button
            key={m}
            className={`dashboard-range${mode === m ? ' active' : ''}`}
            onClick={() => setMode(m)}
          >
            {t(`calendar.${m}View`)}
          </button>
        ))}
        <div className="toolbar-spacer" />
        <button className="dashboard-range" onClick={() => jumpTo(new Date())}>
          {t('calendar.today')}
        </button>
        <button className="toolbar-button" onClick={() => pan(-1)} title={t('timeline.prev')}>
          <ChevronLeft size={15} strokeWidth={2} />
        </button>
        <span className="calendar-header-label">{headerLabel}</span>
        <button className="toolbar-button" onClick={() => pan(1)} title={t('timeline.next')}>
          <ChevronRight size={15} strokeWidth={2} />
        </button>
        <button
          className={`dashboard-range${configOpen ? ' active' : ''}`}
          onClick={() => setConfigOpen((open) => !open)}
        >
          {t('calendarConfig.title')}
        </button>
      </div>

      {configOpen && (
        <div className="calendar-config-shell">
          <CalendarConfigPanel />
        </div>
      )}

      {mode === 'week' && (
        <div className="calendar-week-grid">
          <div className="calendar-week-allday">
            <div className="calendar-week-corner" />
            {weekDays.map((day) => {
              const key = iso(day)
              const allDay = (byDate.get(key) ?? []).filter((e) => e.allDay)
              return (
                <div
                  key={key}
                  className={`calendar-allday-col${isToday(day) ? ' today' : ''}`}
                  onDragOver={(e) => e.preventDefault()}
                  onDrop={() => void reschedule(key)}
                >
                  <div className="calendar-week-daylabel">
                    {day.toLocaleString(i18n.language, { weekday: 'short' })} {day.getDate()}
                  </div>
                  {allDay.map((event) => (
                    <EventChip key={`${event.sceneId}-${key}`} event={event} compact onDragStart={setDragging} />
                  ))}
                </div>
              )
            })}
          </div>
          <div className="calendar-week-body">
            <div className="calendar-week-hours">
              <div className="calendar-hour-gutter">
                {HOURS.map((h) => (
                  <div key={h} className="calendar-hour-label">
                    {pad2(h)}:00
                  </div>
                ))}
              </div>
              {weekDays.map((day) => {
                const key = iso(day)
                const timed = layoutTimed((byDate.get(key) ?? []).filter((e) => !e.allDay))
                return (
                  <div
                    key={key}
                    className={`calendar-day-track${isToday(day) ? ' today' : ''}`}
                    onDragOver={(e) => e.preventDefault()}
                    onDrop={() => void reschedule(key)}
                  >
                    {HOURS.map((h) => (
                      <div key={h} className="calendar-hour-line" />
                    ))}
                    {timed.map((item) => (
                      <button
                        key={`${item.event.sceneId}-${key}`}
                        className="calendar-timed-event"
                        draggable
                        onDragStart={() => setDragging(item.event)}
                        style={{
                          top: `${item.topPx}px`,
                          height: `${item.heightPx}px`,
                          left: `${item.leftPct}%`,
                          width: `${item.widthPct}%`
                        }}
                        title={eventTooltip(item.event)}
                        onClick={() => openScene(item.event)}
                      >
                        <span className="calendar-timed-title">
                          {eventTime(item.event)} {item.event.title}
                        </span>
                        <span className="calendar-timed-chapter">{item.event.chapterTitle}</span>
                        {item.event.note && (
                          <span className="calendar-timed-note">{item.event.note}</span>
                        )}
                      </button>
                    ))}
                  </div>
                )
              })}
            </div>
          </div>
        </div>
      )}

      {mode === 'month' && (
        <div className="calendar-month-wrap">
          <div className="calendar-month-weekdays">
            {WEEKDAY_KEYS.map((key) => (
              <div key={key} className="calendar-weekday">
                {t(`calendar.${key}`)}
              </div>
            ))}
          </div>
          <div className="calendar-month">
            {Array.from({ length: 42 }, (_, i) => {
              const day = addDays(monthGridStart, i)
              const key = iso(day)
              const dayEvents = byDate.get(key) ?? []
              const inMonth = day.getMonth() === anchor.getMonth()
              return (
                <div
                  key={key}
                  className={`calendar-cell${inMonth ? '' : ' outside'}${isToday(day) ? ' today' : ''}`}
                  role="button"
                  tabIndex={0}
                  onDragOver={(e) => e.preventDefault()}
                  onDrop={() => void reschedule(key)}
                  onClick={() => jumpTo(day, 'week')}
                  onKeyDown={(e) => {
                    if (e.key === 'Enter' || e.key === ' ') {
                      e.preventDefault()
                      jumpTo(day, 'week')
                    }
                  }}
                >
                  <div className="calendar-cell-date">{day.getDate()}</div>
                  {dayEvents.slice(0, 3).map((event) => (
                    <EventChip key={`${event.sceneId}-${key}`} event={event} compact onDragStart={setDragging} />
                  ))}
                  {dayEvents.length > 3 && (
                    <div className="calendar-overflow">+{dayEvents.length - 3}</div>
                  )}
                </div>
              )
            })}
          </div>
        </div>
      )}

      {mode === 'year' && (
        <div className="calendar-year">
          {MONTHS.map((m) => {
            const monthEvents = eventsByMonth.get(m + 1) ?? []
            return (
              <div key={m} className="calendar-year-month dashboard-card">
                <button
                  className="calendar-year-head"
                  onClick={() => jumpTo(new Date(anchor.getFullYear(), m, 1), 'month')}
                >
                  <span className="calendar-year-title">{monthName(m)}</span>
                  <span className="calendar-year-scenecount">
                    {t('calendar.sceneCount', { count: monthEvents.length })}
                  </span>
                </button>
                <div className="calendar-year-list">
                  {monthEvents.map((event) => (
                    <button
                      key={event.sceneId}
                      className="calendar-event"
                      title={eventTooltip(event)}
                      onClick={() => openScene(event)}
                    >
                      <span className="calendar-event-title">{event.title}</span>
                    </button>
                  ))}
                </div>
              </div>
            )
          })}
        </div>
      )}
      <SceneBulkBar />
    </div>
  )
}

function EventChip({
  event,
  compact,
  onDragStart
}: {
  event: CalendarEventDto
  compact?: boolean
  onDragStart?(event: CalendarEventDto): void
}): React.JSX.Element {
  const selected = useSelectionStore((s) => s.sceneIds).includes(event.sceneId)
  const time = event.allDay ? '' : `${eventTime(event)} `
  return (
    <button
      className={`calendar-event${compact ? ' compact' : ''}${
        selected ? ' selected' : ''
      }`}
      draggable={Boolean(onDragStart)}
      onDragStart={() => onDragStart?.(event)}
      title={eventTooltip(event)}
      onClick={(e) => {
        e.stopPropagation()
        if (handleSceneClick(event.sceneId, e)) return
        openScene(event)
      }}
    >
      <span className="calendar-event-title">
        {time}
        {event.title}
      </span>
      {event.note && <span className="calendar-event-note">{event.note}</span>}
    </button>
  )
}
