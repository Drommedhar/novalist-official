import { expect, test } from '@playwright/test'
import { CalendarDates, type CalendarConfig } from '../src/renderer/src/views/calendar/calendarDates'

const config: CalendarConfig = {
  type: 'Custom', yearLabel: 'Cycle', monthNames: ['Rimefall', 'Thaw', 'Highsun'],
  daysPerMonth: [30, 30, 40], weekdayNames: ['Firstday', 'Starday', 'Moonday', 'Fireday', 'Lastday'],
  yearLength: 100, eras: []
}

test('custom arithmetic crosses month, year and negative-year boundaries without Gregorian limits', () => {
  const dates = new CalendarDates(config, 'en')
  expect(dates.key(dates.addDays({ year: -1, month: 3, day: 40 }, 1))).toBe('0.1.1')
  expect(dates.key(dates.addDays({ year: 0, month: 1, day: 1 }, -1))).toBe('-1.3.40')
  expect(dates.key(dates.shift({ year: 812, month: 3, day: 40 }, 'month', 1))).toBe('813.1.30')
  expect(dates.key(dates.shift({ year: 0, month: 1, day: 1 }, 'month', -1))).toBe('-1.3.1')
  expect(dates.key(dates.shift({ year: 0, month: 1, day: 1 }, 'week', -1))).toBe('-1.3.36')
  expect(dates.parse('812.3.40')).toEqual({ year: 812, month: 3, day: 40 })
  expect(dates.parse('812.1.40')).toBeNull()
  expect(dates.parse('812.4.1')).toBeNull()
  expect(dates.weekday({ year: -1, month: 3, day: 40 })).toBe(4)
})

test('era labels match the book reckoning on either side of year zero', () => {
  const dates = new CalendarDates({ ...config, eras: [
    { name: 'Before the Fall', startYear: -100, countsDown: true },
    { name: 'After the Fall', startYear: 0, countsDown: false }
  ] }, 'en')
  expect(dates.formatYear(-12)).toBe('12 Before the Fall')
  expect(dates.formatYear(0)).toBe('1 After the Fall')
  expect(dates.formatYear(-101)).toBe('-101 Cycle')
})

test('Gregorian navigation preserves local dates, leap years, early years and month-end clamping', () => {
  const dates = new CalendarDates({ ...config, type: 'Gregorian' }, 'en')
  expect(dates.months).toHaveLength(12)
  expect(dates.weekdays).toHaveLength(7)
  expect(dates.key(dates.shift({ year: 2024, month: 1, day: 31 }, 'month', 1))).toBe('2024-02-29')
  expect(dates.key(dates.shift({ year: 2024, month: 2, day: 29 }, 'year', 1))).toBe('2025-02-28')
  expect(dates.key(dates.addDays({ year: 2026, month: 3, day: 29 }, 1))).toBe('2026-03-30')
  expect(dates.key(dates.addDays({ year: 12, month: 1, day: 1 }, 1))).toBe('0012-01-02')
  expect(dates.parse('0.1.1')).toBeNull()
  expect(dates.parse('2025-02-29')).toBeNull()
  expect(dates.formatYear(2026)).toBe('2026')
})
