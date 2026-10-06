import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { rpc } from '../../rpc/client'
import { useSelectionStore } from '../../stores/selectionStore'
import { CalendarDates, type CalendarConfig, type CalendarDate } from './calendarDates'
import { startMinutes, type CalendarEventDto, type CalendarMode } from './calendarEvents'

function useCalendarConfig(closeGoTo: () => void) {
  const { i18n } = useTranslation()
  const [config, setConfig] = useState<CalendarConfig | null>(null)
  const dates = useMemo(
    () => (config ? new CalendarDates(config, i18n.language) : null),
    [config, i18n.language]
  )
  const [anchor, setAnchor] = useState<CalendarDate | null>(null)
  const [storyStart, setStoryStart] = useState<CalendarDate | null>(null)
  const configRevision = useRef(0)
  const savedConfig = async (next: CalendarConfig): Promise<void> => {
    const revision = ++configRevision.current
    const calendar = new CalendarDates(next, i18n.language)
    const typeChanged = next.type !== config?.type
    setConfig(next)
    closeGoTo()
    setAnchor((current) => {
      if (!current || typeChanged) return calendar.today()
      const month = Math.min(current.month, calendar.months.length)
      return { ...current, month, day: Math.min(current.day, calendar.daysInMonth({ ...current, month })) }
    })
    const [start, saved] = await Promise.all([
      rpc.request<string | null>('calendar/getStoryStart'),
      typeChanged ? rpc.request<string | null>('calendar/getAnchor') : Promise.resolve(null)
    ])
    if (configRevision.current === revision) {
      setStoryStart(calendar.parse(start))
      if (typeChanged) setAnchor(calendar.parse(start) ?? calendar.parse(saved) ?? calendar.today())
    }
  }
  useEffect(() => {
    let disposed = false
    void Promise.all([
      rpc.request<CalendarConfig>('calendar/getConfig'),
      rpc.request<string | null>('calendar/getAnchor'),
      rpc.request<string | null>('calendar/getStoryStart')
    ]).then(([loaded, saved, start]) => {
      if (disposed) return
      const calendar = new CalendarDates(loaded, i18n.language)
      setConfig(loaded)
      setStoryStart(calendar.parse(start))
      setAnchor(calendar.parse(start) ?? calendar.parse(saved) ?? calendar.today())
    })
    return () => { disposed = true }
  }, [])
  return { dates, anchor, setAnchor, storyStart, setStoryStart, savedConfig }
}

function useCalendarEvents(dates: CalendarDates | null, anchor: CalendarDate | null, mode: CalendarMode) {
  const [events, setEvents] = useState<CalendarEventDto[]>([])
  const load = useCallback(async (): Promise<CalendarEventDto[]> => {
    if (!dates || !anchor || (dates.custom && !dates.config.monthNames.length)) return []
    let from: CalendarDate
    let to: CalendarDate
    if (mode === 'week') {
      from = dates.startOfWeek(anchor)
      to = dates.addDays(from, dates.weekdays.length - 1)
    } else if (mode === 'month') {
      from = dates.startOfWeek({ ...anchor, day: 1 })
      const cells = Math.ceil(
        (dates.weekday({ ...anchor, day: 1 }) + dates.daysInMonth(anchor)) / dates.weekdays.length
      ) * dates.weekdays.length
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
  return { events, setEvents, load }
}

function groupByDate(events: CalendarEventDto[]): Map<string, CalendarEventDto[]> {
  const map = new Map<string, CalendarEventDto[]>()
  for (const event of events) {
    const list = map.get(event.date) ?? []
    list.push(event)
    map.set(event.date, list)
  }
  for (const list of map.values()) {
    list.sort((a, b) => a.allDay === b.allDay ? startMinutes(a) - startMinutes(b) : a.allDay ? -1 : 1)
  }
  return map
}

function groupByMonth(events: CalendarEventDto[], dates: CalendarDates | null): Map<number, CalendarEventDto[]> {
  const map = new Map<number, CalendarEventDto[]>()
  const seen = new Map<number, Set<string>>()
  for (const event of events) {
    const month = dates?.parse(event.date)?.month
    if (month === undefined) continue
    let list = map.get(month)
    if (!list) { list = []; map.set(month, list) }
    let ids = seen.get(month)
    if (!ids) { ids = new Set(); seen.set(month, ids) }
    if (!ids.has(event.sceneId)) { ids.add(event.sceneId); list.push(event) }
  }
  return map
}

export function useCalendarModel() {
  const [mode, setMode] = useState<CalendarMode>(window.novalist.isMobile ? 'week' : 'month')
  const [configOpen, setConfigOpen] = useState(false)
  const [goToOpen, setGoToOpen] = useState(false)
  const calendar = useCalendarConfig(() => setGoToOpen(false))
  const { dates, anchor, setAnchor, setStoryStart } = calendar
  const { events, setEvents, load } = useCalendarEvents(dates, anchor, mode)
  const [dragging, setDragging] = useState<CalendarEventDto | null>(null)
  const reschedule = async (target: string): Promise<void> => {
    if (!dragging || !dates) return
    const selection = useSelectionStore.getState().sceneIds
    // Preserve the date gaps when dragging a selection of scenes together.
    if (selection.length > 1 && selection.includes(dragging.sceneId)) {
      const days = dates.ordinal(dates.parse(target)!) - dates.ordinal(dates.parse(dragging.date)!)
      if (days !== 0) await rpc.request('sceneBulk/shiftDates', [selection, days])
    } else {
      await rpc.request('calendar/reschedule', [dragging.chapterGuid, dragging.sceneId, target])
    }
    setDragging(null)
    setEvents(await load())
    setStoryStart(dates.parse(await rpc.request<string | null>('calendar/getStoryStart')))
  }
  const jumpTo = (date: CalendarDate, newMode?: CalendarMode): void => {
    setAnchor(date)
    if (newMode) setMode(newMode)
    void rpc.request('calendar/setAnchor', [dates!.key(date)])
  }
  const pan = (direction: -1 | 1): void => {
    if (dates && anchor) jumpTo(dates.shift(anchor, mode, direction))
  }
  const byDate = useMemo(() => groupByDate(events), [events])
  const eventsByMonth = useMemo(() => groupByMonth(events, dates), [events, dates])
  return {
    ...calendar, mode, setMode, configOpen, setConfigOpen, goToOpen, setGoToOpen,
    setDragging, reschedule, jumpTo, pan, byDate, eventsByMonth
  }
}

export type CalendarModel = ReturnType<typeof useCalendarModel>
