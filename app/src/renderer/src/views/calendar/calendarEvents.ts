import { useProjectStore } from '../../stores/projectStore'

export interface CalendarEventDto {
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

export type CalendarMode = 'week' | 'month' | 'year'

/** Pixel height of one hour row in the week timed grid. */
const HOUR_PX = 48

export const pad2 = (n: number): string => n.toString().padStart(2, '0')

export const startMinutes = (e: CalendarEventDto): number => e.startHour * 60 + e.startMinute

export const eventTime = (e: CalendarEventDto): string =>
  e.allDay ? '' : `${pad2(e.startHour)}:${pad2(e.startMinute)}`

export const eventTooltip = (e: CalendarEventDto): string => {
  const parts = [e.title]
  if (e.note) parts.push(e.note)
  if (e.synopsis) parts.push(e.synopsis)
  return parts.join('\n')
}

export const openScene = (e: CalendarEventDto): void => {
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
export const layoutTimed = (events: CalendarEventDto[]): TimedLayout[] => {
  const items = events
    .map((event) => {
      const start = startMinutes(event)
      let end = event.endHour * 60 + event.endMinute
      if (end <= start) end = start + 60
      const topPx = (start / 60) * HOUR_PX
      const heightPx = Math.max(20, ((end - start) / 60) * HOUR_PX)
      return {
        event,
        topPx,
        heightPx,
        start: topPx,
        end: topPx + heightPx,
        leftPct: 0,
        widthPct: 100
      }
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
