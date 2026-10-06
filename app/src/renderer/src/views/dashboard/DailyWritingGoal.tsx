import { type BookDashboardState } from './dashboardModel'

const RANGES = [30, 90, 365]

export function DailyWritingGoal({ setEditingGoal, t, data, setPacing, range, setRange, maxBar }: { setEditingGoal: BookDashboardState['setEditingGoal']; t: BookDashboardState['t']; data: NonNullable<BookDashboardState['data']>; setPacing: BookDashboardState['setPacing']; range: BookDashboardState['range']; setRange: BookDashboardState['setRange']; maxBar: BookDashboardState['maxBar'] }): React.JSX.Element {
  return (
    <div className="dashboard-card">
      <div className="dashboard-card-title">
        <button className="dashboard-card-title-btn" onClick={() => setEditingGoal('daily')}>
          {t('dashboard.dailyProgress')}
        </button>
      </div>
      <div className="dashboard-goal-row">
        <span>
          {data.dailyGoalCurrent.toLocaleString()} / {data.dailyGoalTarget.toLocaleString()}
        </span>
        <span>{data.dailyGoalPercent}%</span>
      </div>
      <div className="dashboard-bar-track">
        <div className="dashboard-bar-fill" style={{ width: `${data.dailyGoalPercent}%` }} />
      </div>
      <div className="dashboard-streak">
        {t('dashboard.streakDays', { count: data.currentStreak })}
      </div>

      {/* Longer horizons, shown only once the writer has set one. A daily
          goal asks the same of every day, so somebody who writes three
          heavy days a week misses four out of seven while being exactly on
          schedule - and has no way to make Tuesday up on Saturday. */}
      {[
        { horizon: data.week, label: 'dashboard.thisWeek' },
        { horizon: data.month, label: 'dashboard.thisMonth' }
      ]
        .filter((row) => row.horizon.goal > 0)
        .map((row) => (
          <div key={row.label} className="dashboard-horizon">
            <div className="dashboard-goal-row">
              <span>{t(row.label)}</span>
              <span>
                {row.horizon.current.toLocaleString()} / {row.horizon.goal.toLocaleString()}
              </span>
            </div>
            <div className="dashboard-bar-track">
              <div
                className="dashboard-bar-fill"
                style={{ width: `${row.horizon.percent}%` }}
              />
            </div>
            {/* What is left, over the days left to write it in - which is
                the number that says whether being behind matters. */}
            {row.horizon.current < row.horizon.goal && row.horizon.daysLeft > 0 && (
              <div className="dashboard-streak">
                {t('dashboard.horizonPace', {
                  words: Math.ceil(
                    (row.horizon.goal - row.horizon.current) / row.horizon.daysLeft
                  ).toLocaleString(),
                  days: row.horizon.daysLeft
                })}
              </div>
            )}
          </div>
        ))}

      {/* A journal has been kept per day all along and shown as a bar
          chart and nothing else. */}
      <div className="dashboard-status-summary">
        <div>
          <div className="dashboard-summary-count">{data.history.longestStreak}</div>
          <div className="dashboard-summary-count-label">{t('dashboard.longestStreak')}</div>
        </div>
        <div>
          <div className="dashboard-summary-count">
            {data.history.writingDaysConsidered > 0
              ? `${Math.round(
                  (data.history.daysHitGoal / data.history.writingDaysConsidered) * 100
                )}%`
              : '-'}
          </div>
          <div className="dashboard-summary-count-label">{t('dashboard.hitRate')}</div>
        </div>
        <div>
          <div className="dashboard-summary-count">
            {data.history.bestDayWords.toLocaleString()}
          </div>
          <div className="dashboard-summary-count-label">
            {data.history.bestDayDate || t('dashboard.bestDay')}
          </div>
        </div>
        <div>
          <div className="dashboard-summary-count">
            {data.history.averagePerWritingDay.toLocaleString()}
          </div>
          <div className="dashboard-summary-count-label">{t('dashboard.averagePerDay')}</div>
        </div>
      </div>

      {/* Which days count, and whether today's number follows what is
          left. A streak that breaks on a day off measures nothing. */}
      <label className="relationships-toggle">
        <input
          type="checkbox"
          checked={data.history.adaptive}
          onChange={(e) => void setPacing(e.target.checked, data.history.writingDays)}
        />
        {t('dashboard.adaptiveGoal')}
      </label>
      <div className="dashboard-writing-days">
        {[1, 2, 3, 4, 5, 6, 0].map((day) => {
          // An empty list means every day, so nothing is unticked then.
          const on =
            data.history.writingDays.length === 0 || data.history.writingDays.includes(day)
          return (
            <button
              key={day}
              className={`dashboard-range${on ? ' active' : ''}`}
              title={t('dashboard.writingDaysHint')}
              onClick={() => {
                const current =
                  data.history.writingDays.length === 0
                    ? [0, 1, 2, 3, 4, 5, 6]
                    : data.history.writingDays
                const next = on ? current.filter((d) => d !== day) : [...current, day].sort()
                void setPacing(data.history.adaptive, next)
              }}
            >
              {t(`dashboard.day${day}`)}
            </button>
          )
        })}
      </div>
      <div className="dashboard-range-buttons">
        {RANGES.map((r) => (
          <button
            key={r}
            className={`dashboard-range${range === r ? ' active' : ''}`}
            onClick={() => setRange(r)}
          >
            {r}d
          </button>
        ))}
      </div>
      <div className="dashboard-history" role="img" aria-label={t('dashboard.dailyProgress')}>
        {data.wordHistory.map((bar) => (
          <div
            key={bar.date}
            className={`dashboard-history-bar${bar.metGoal ? ' met' : ''}`}
            style={{ height: `${Math.max(2, (bar.words / maxBar) * 100)}%` }}
            title={`${bar.date}: ${bar.words.toLocaleString()}`}
          />
        ))}
      </div>
    </div>
  )
}
