import { useCallback, useEffect, useMemo, useRef, useState, type CSSProperties } from 'react'
import { useTranslation } from 'react-i18next'
import { ChevronLeft, ChevronRight } from 'lucide-react'
import { rpc } from '../../rpc/client'
import { CalendarConfigPanel } from './CalendarConfigPanel'
import { CalendarDates, type CalendarConfig, type CalendarDate } from './calendarDates'
import { useBookScope, useProjectStore } from '../../stores/projectStore'
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
export function CalendarView(): React.JSX.Element {
  const scope = useBookScope()
  return <BookCalendarView key={scope} />
}

function BookCalendarView(): React.JSX.Element {
  const { t, i18n } = useTranslation()
  const [mode, setMode] = useState<CalendarMode>('week')
  const [configOpen, setConfigOpen] = useState(false)
  const [config, setConfig] = useState<CalendarConfig | null>(null)
  const dates = useMemo(() => config ? new CalendarDates(config, i18n.language) : null, [config, i18n.language])
  const [anchor, setAnchor] = useState<CalendarDate | null>(null)
  const [events, setEvents] = useState<CalendarEventDto[]>([])
  const [dragging, setDragging] = useState<CalendarEventDto | null>(null)

  const configRevision = useRef(0)

  const savedConfig = async (next: CalendarConfig): Promise<void> => {
    const revision = ++configRevision.current
    const calendar = new CalendarDates(next, i18n.language)
    const typeChanged = next.type !== config?.type
    setConfig(next)
    setAnchor((current) => {
      if (!current || typeChanged) return calendar.today()
      const month = Math.min(current.month, calendar.months.length)
      return { ...current, month, day: Math.min(current.day, calendar.daysInMonth({ ...current, month })) }
    })
    if (typeChanged) {
      const saved = await rpc.request<string | null>('calendar/getAnchor')
      if (configRevision.current === revision) setAnchor(calendar.parse(saved) ?? calendar.today())
    }
  }

  useEffect(() => {
    let disposed = false
    void Promise.all([
      rpc.request<CalendarConfig>('calendar/getConfig'),
      rpc.request<string | null>('calendar/getAnchor')
    ]).then(([loaded, saved]) => {
      if (disposed) return
      const calendar = new CalendarDates(loaded, i18n.language)
      setConfig(loaded)
      setAnchor(calendar.parse(saved) ?? calendar.today())
    })
    return () => { disposed = true }
  }, [])

  const reschedule = async (target: string): Promise<void> => {
    if (!dragging || !dates) return
    const selection = useSelectionStore.getState().sceneIds
    // Dragging one chip out of a selection moves the whole selection, keeping
    // the gaps between the scenes. Dropping them all on the target day would
    // collapse a week of story into one afternoon.
    if (selection.length > 1 && selection.includes(dragging.sceneId)) {
      const days = dates.ordinal(dates.parse(target)!) - dates.ordinal(dates.parse(dragging.date)!)
      if (days !== 0) await rpc.request('sceneBulk/shiftDates', [selection, days])
    } else {
      await rpc.request('calendar/reschedule', [dragging.chapterGuid, dragging.sceneId, target])
    }
    setDragging(null)
    setEvents(await load())
  }

  const load = useCallback(async (): Promise<CalendarEventDto[]> => {
    if (!dates || !anchor || (dates.custom && !dates.config.monthNames.length)) return []
    let from: CalendarDate
    let to: CalendarDate
    if (mode === 'week') {
      from = dates.startOfWeek(anchor)
      to = dates.addDays(from, dates.weekdays.length - 1)
    } else if (mode === 'month') {
      from = dates.startOfWeek({ ...anchor, day: 1 })
      const cells = Math.ceil((dates.weekday({ ...anchor, day: 1 }) + dates.daysInMonth(anchor)) / dates.weekdays.length) * dates.weekdays.length
      to = dates.addDays(from, cells - 1)
    } else {
      from = { year: anchor.year, month: 1, day: 1 }
      to = { year: anchor.year, month: dates.months.length, day: 1 }
      to.day = dates.daysInMonth(to)
    }
    return rpc.request<CalendarEventDto[]>('calendar/get', [dates.key(from), dates.key(to)])
  }, [anchor, dates, mode])

  useEffect(() => {
    let disposed = false
    setEvents([])
    void load().then((loaded) => { if (!disposed) setEvents(loaded) })
    return () => { disposed = true }
  }, [load])

  const jumpTo = (date: CalendarDate, newMode?: CalendarMode): void => {
    setAnchor(date)
    if (newMode) setMode(newMode)
    void rpc.request('calendar/setAnchor', [dates!.key(date)])
  }

  const pan = (direction: -1 | 1): void => {
    if (dates && anchor) jumpTo(dates.shift(anchor, mode, direction))
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
      const month = dates?.parse(event.date)?.month
      if (month === undefined) continue
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
  }, [events, dates])

  if (!dates || !anchor) return <p className="settings-hint">{t('calendarConfig.loading')}</p>
  const monthName = (m: number): string => dates.months[m - 1]
  const weekStart = dates.startOfWeek(anchor)
  const weekDays = Array.from({ length: dates.weekdays.length }, (_, i) => dates.addDays(weekStart, i))
  const monthStart = { ...anchor, day: 1 }
  const monthGridStart = dates.startOfWeek(monthStart)
  const monthCells = Math.ceil((dates.weekday(monthStart) + dates.daysInMonth(anchor)) / dates.weekdays.length) * dates.weekdays.length
  const headerLabel = mode === 'week'
    ? `${dates.label(weekStart)} - ${dates.label(weekDays[weekDays.length - 1])}`
    : mode === 'month' ? `${monthName(anchor.month)} ${dates.formatYear(anchor.year)}` : dates.formatYear(anchor.year)

  return (
    <div className="calendar" style={{ '--cal-weekdays': dates.weekdays.length } as CSSProperties}>
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
        {!dates.custom && <button className="dashboard-range" onClick={() => jumpTo(dates.today())}>
          {t('calendar.today')}
        </button>}
        {/* Back, where you are, forward: one control, so a phone wrapping the
            toolbar onto a second line keeps them together instead of leaving an
            arrow stranded at the end of each line. */}
        <div className="calendar-nav">
          <button className="toolbar-button" onClick={() => pan(-1)} title={t('timeline.prev')}>
            <ChevronLeft size={15} strokeWidth={2} />
          </button>
          <span className="calendar-header-label">{headerLabel}</span>
          <button className="toolbar-button" onClick={() => pan(1)} title={t('timeline.next')}>
            <ChevronRight size={15} strokeWidth={2} />
          </button>
        </div>
        <button
          className={`dashboard-range${configOpen ? ' active' : ''}`}
          onClick={() => setConfigOpen((open) => !open)}
        >
          {t('calendarConfig.title')}
        </button>
      </div>

      {configOpen && (
        <div className="calendar-config-shell">
          <CalendarConfigPanel onSaved={(next) => void savedConfig(next)} />
        </div>
      )}

      {mode === 'week' && (
        <div className="calendar-week-grid">
          <div className="calendar-week-allday">
            <div className="calendar-week-corner" />
            {weekDays.map((day) => {
              const key = dates.key(day)
              const allDay = (byDate.get(key) ?? []).filter((e) => e.allDay)
              return (
                <div
                  key={key}
                  className={`calendar-allday-col${dates.isToday(day) ? ' today' : ''}`}
                  onDragOver={(e) => e.preventDefault()}
                  onDrop={() => void reschedule(key)}
                >
                  <div className="calendar-week-daylabel">
                    {dates.weekdays[dates.weekday(day)]} {day.day}
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
                const key = dates.key(day)
                const timed = layoutTimed((byDate.get(key) ?? []).filter((e) => !e.allDay))
                return (
                  <div
                    key={key}
                    className={`calendar-day-track${dates.isToday(day) ? ' today' : ''}`}
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
            {dates.weekdays.map((name, index) => (
              <div key={index} className="calendar-weekday">
                {name}
              </div>
            ))}
          </div>
          <div className="calendar-month">
            {Array.from({ length: monthCells }, (_, i) => {
              const day = dates.addDays(monthGridStart, i)
              const key = dates.key(day)
              const dayEvents = byDate.get(key) ?? []
              const inMonth = day.month === anchor.month && day.year === anchor.year
              return (
                <div
                  key={key}
                  className={`calendar-cell${inMonth ? '' : ' outside'}${dates.isToday(day) ? ' today' : ''}`}
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
                  <div className="calendar-cell-date">{day.day}</div>
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
          {dates.months.map((_, index) => {
            const m = index + 1
            const monthEvents = eventsByMonth.get(m) ?? []
            return (
              <div key={m} className="calendar-year-month dashboard-card">
                <button
                  className="calendar-year-head"
                  onClick={() => jumpTo({ year: anchor.year, month: m, day: 1 }, 'month')}
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
