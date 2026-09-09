/** Calendar arithmetic shared by the three views. Custom dates never pass through Date. */
export interface CalendarConfig {
  type: string
  yearLabel: string
  monthNames: string[]
  daysPerMonth: number[]
  weekdayNames: string[]
  yearLength: number
  eras: { name: string; startYear: number; countsDown: boolean }[]
}

export interface CalendarDate { year: number; month: number; day: number }
const mod = (n: number, divisor: number): number => ((n % divisor) + divisor) % divisor
const utc = (d: CalendarDate): Date => {
  const value = new Date(0)
  value.setUTCFullYear(d.year, d.month - 1, d.day)
  return value
}
const parts = (d: Date): CalendarDate => ({ year: d.getUTCFullYear(), month: d.getUTCMonth() + 1, day: d.getUTCDate() })

export class CalendarDates {
  readonly custom: boolean
  readonly weekdays: string[]
  readonly months: string[]
  readonly lengths: number[]
  readonly yearLength: number

  constructor(readonly config: CalendarConfig, readonly language: string) {
    this.custom = config.type === 'Custom'
    this.months = this.custom ? (config.monthNames.length ? config.monthNames : ['1'])
      : Array.from({ length: 12 }, (_, i) => new Date(2000, i, 1).toLocaleString(language, { month: 'long' }))
    this.weekdays = this.custom ? (config.weekdayNames.length ? config.weekdayNames : ['1'])
      : Array.from({ length: 7 }, (_, i) => new Date(2024, 0, i + 1).toLocaleString(language, { weekday: 'short' }))
    this.lengths = config.daysPerMonth.length ? config.daysPerMonth : [1]
    this.yearLength = this.lengths.reduce((a, b) => a + b, 0)
  }

  today(): CalendarDate {
    const now = new Date()
    return this.custom ? { year: 0, month: 1, day: 1 }
      : { year: now.getFullYear(), month: now.getMonth() + 1, day: now.getDate() }
  }

  parse(raw: string | null): CalendarDate | null {
    const match = raw?.trim().match(/^(-?\d+)[.\-/](\d+)[.\-/](\d+)$/)
    if (!match) return null
    const [year, month, day] = match.slice(1).map(Number)
    const date = { year, month, day }
    return Number.isSafeInteger(year) && (this.custom || (year >= 1 && year <= 9999)) && month >= 1 && month <= this.months.length
      && day >= 1 && day <= this.daysInMonth(date) ? date : null
  }

  key(d: CalendarDate): string {
    return this.custom ? `${d.year}.${d.month}.${d.day}`
      : `${String(d.year).padStart(4, '0')}-${String(d.month).padStart(2, '0')}-${String(d.day).padStart(2, '0')}`
  }

  ordinal(d: CalendarDate): number {
    return this.custom ? d.year * this.yearLength + this.lengths.slice(0, d.month - 1).reduce((a, b) => a + b, 0) + d.day - 1
      : utc(d).getTime() / 86_400_000
  }

  addDays(d: CalendarDate, days: number): CalendarDate {
    const ordinal = this.ordinal(d) + days
    if (!this.custom) return parts(new Date(ordinal * 86_400_000))
    const year = Math.floor(ordinal / this.yearLength)
    let remaining = mod(ordinal, this.yearLength)
    let month = 1
    while (remaining >= this.lengths[month - 1]) remaining -= this.lengths[month++ - 1]
    return { year, month, day: remaining + 1 }
  }

  daysInMonth(d: CalendarDate): number {
    return this.custom ? this.lengths[d.month - 1]
      : utc({ year: d.year, month: d.month + 1, day: 0 }).getUTCDate()
  }

  shift(d: CalendarDate, mode: 'week' | 'month' | 'year', direction: number): CalendarDate {
    if (mode === 'week') return this.addDays(d, direction * this.weekdays.length)
    const index = d.year * this.months.length + d.month - 1 + direction * (mode === 'year' ? this.months.length : 1)
    const next = { year: Math.floor(index / this.months.length), month: mod(index, this.months.length) + 1, day: 1 }
    return { ...next, day: Math.min(d.day, this.daysInMonth(next)) }
  }

  weekday(d: CalendarDate): number {
    // Custom year zero starts on the first named weekday, continuously across years.
    return this.custom ? mod(this.ordinal(d), this.weekdays.length) : mod(utc(d).getUTCDay() - 1, 7)
  }

  startOfWeek(d: CalendarDate): CalendarDate { return this.addDays(d, -this.weekday(d)) }

  formatYear(year: number): string {
    if (!this.custom) return String(year)
    const eras = [...this.config.eras].sort((a, b) => a.startYear - b.startYear)
    const era = eras.filter((e) => e.startYear <= year).at(-1)
    if (!era) return `${year} ${this.config.yearLabel}`.trim()
    const next = eras.find((e) => e.startYear > era.startYear)?.startYear ?? 0
    return `${era.countsDown ? next - year : year - era.startYear + 1} ${era.name}`.trim()
  }

  label(d: CalendarDate): string {
    return this.custom ? `${d.day} ${this.months[d.month - 1]} ${this.formatYear(d.year)}` : this.key(d)
  }

  isToday(d: CalendarDate): boolean { return !this.custom && this.key(d) === this.key(this.today()) }
}
