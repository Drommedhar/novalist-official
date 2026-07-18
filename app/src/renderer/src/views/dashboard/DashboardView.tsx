import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { rpc } from '../../rpc/client'
import { InputDialog } from '../../shell/InputDialog'
import { useShellStore } from '../../stores/shellStore'

interface DashboardDto {
  projectName: string
  totalWords: number
  chapterCount: number
  sceneCount: number
  characterCount: number
  locationCount: number
  readingTimeMinutes: number
  averageChapterWords: number
  dailyGoalCurrent: number
  dailyGoalTarget: number
  dailyGoalPercent: number
  projectGoalTarget: number
  projectGoalPercent: number
  deadline: string | null
  todayWords: number
  currentStreak: number
  statusBreakdown: { status: string; count: number; wordCount: number }[]
  chapterPacing: { title: string; words: number }[]
  maxChapterWords: number
  echoPhrases: { phrase: string; count: number }[]
  wordHistory: { date: string; words: number; metGoal: boolean }[]
}

const RANGES = [30, 90, 365]

export function DashboardView(): React.JSX.Element {
  const { t } = useTranslation()
  const mainView = useShellStore((s) => s.mainView)
  const [data, setData] = useState<DashboardDto | null>(null)
  const [range, setRange] = useState(30)
  const [editingGoal, setEditingGoal] = useState<'daily' | 'project' | null>(null)

  useEffect(() => {
    if (mainView !== 'dashboard') return
    void rpc.request<DashboardDto>('dashboard/get', [range]).then(setData)
  }, [mainView, range])

  if (!data) return <div className="main-placeholder">{t('shell.backendConnecting')}</div>

  const maxBar = Math.max(1, ...data.wordHistory.map((b) => b.words))
  const statusTotal = Math.max(1, data.statusBreakdown.reduce((s, b) => s + b.count, 0))

  const metrics: { key: string; value: string }[] = [
    { key: 'dashboard.words', value: data.totalWords.toLocaleString() },
    { key: 'shell.chapters', value: String(data.chapterCount) },
    { key: 'shell.scenes', value: String(data.sceneCount) },
    { key: 'dashboard.readingTime', value: `${data.readingTimeMinutes} min` },
    { key: 'codexHub.characters', value: String(data.characterCount) },
    { key: 'codexHub.locations', value: String(data.locationCount) }
  ]

  return (
    <div className="dashboard">
      <h1 className="dashboard-title">{data.projectName}</h1>

      <div className="dashboard-metrics">
        {metrics.map((m) => (
          <div key={m.key} className="dashboard-card dashboard-metric">
            <div className="dashboard-metric-value">{m.value}</div>
            <div className="dashboard-metric-label">{t(m.key)}</div>
          </div>
        ))}
      </div>

      <div className="dashboard-columns">
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

        <div className="dashboard-card">
          <div className="dashboard-card-title">
            <button className="dashboard-card-title-btn" onClick={() => setEditingGoal('project')}>
              {t('dashboard.goalTracking')}
            </button>
          </div>
          <div className="dashboard-goal-row">
            <span>
              {data.totalWords.toLocaleString()} / {data.projectGoalTarget.toLocaleString()}
            </span>
            <span>{data.projectGoalPercent}%</span>
          </div>
          <div className="dashboard-bar-track">
            <div className="dashboard-bar-fill" style={{ width: `${data.projectGoalPercent}%` }} />
          </div>
          {data.deadline && (
            <div className="dashboard-deadline">
              {t('dashboard.deadline')}: {data.deadline}
            </div>
          )}
          <div className="dashboard-average">
            {t('dashboard.avgChapter')}: {data.averageChapterWords.toLocaleString()}
          </div>
        </div>
      </div>

      {data.statusBreakdown.length > 0 && (
        <div className="dashboard-card">
          <div className="dashboard-card-title">{t('dashboard.progressBreakdown')}</div>
          {data.statusBreakdown.map((s) => (
            <div key={s.status} className="dashboard-status-row">
              <span className="dashboard-status-dot" data-status={s.status} />
              <span className="dashboard-status-name">{t(`dashboard.status${s.status}`)}</span>
              <div className="dashboard-bar-track dashboard-status-track">
                <div
                  className="dashboard-bar-fill"
                  style={{ width: `${Math.round((s.count / statusTotal) * 100)}%` }}
                />
              </div>
              <span className="dashboard-status-count">
                {s.count} - {s.wordCount.toLocaleString()} {t('shell.words')}
              </span>
            </div>
          ))}
        </div>
      )}

      {data.chapterPacing.length > 0 && (
        <div className="dashboard-card">
          <div className="dashboard-card-title">{t('dashboard.pacingAnalysis')}</div>
          {data.chapterPacing.map((c) => (
            <div key={c.title} className="dashboard-pacing-row">
              <span className="dashboard-pacing-title">{c.title}</span>
              <div className="dashboard-bar-track dashboard-status-track">
                <div
                  className="dashboard-bar-fill"
                  style={{
                    width: `${Math.round((c.words / Math.max(1, data.maxChapterWords)) * 100)}%`
                  }}
                />
              </div>
              <span className="dashboard-status-count">{c.words.toLocaleString()}</span>
            </div>
          ))}
        </div>
      )}

      {data.echoPhrases.length > 0 && (
        <div className="dashboard-card">
          <div className="dashboard-card-title">{t('dashboard.echoFinder')}</div>
          <div className="dashboard-echoes">
            {data.echoPhrases.map((e) => (
              <span key={e.phrase} className="dashboard-echo">
                {e.phrase} <b>{e.count}</b>
              </span>
            ))}
          </div>
        </div>
      )}
      {editingGoal && (
        <InputDialog
          title={editingGoal === 'daily' ? t('settings.dailyWordGoal') : t('settings.projectWordGoal')}
          placeholder={String(editingGoal === 'daily' ? data.dailyGoalTarget : data.projectGoalTarget)}
          onCancel={() => setEditingGoal(null)}
          onSubmit={(value) => {
            const parsed = Number(value)
            const which = editingGoal
            setEditingGoal(null)
            if (!Number.isFinite(parsed) || parsed < 0) return
            void rpc
              .request('dashboard/setGoals', [
                which === 'daily' ? parsed : data.dailyGoalTarget,
                which === 'project' ? parsed : data.projectGoalTarget,
                data.deadline
              ])
              .then(() => rpc.request<DashboardDto>('dashboard/get', [range]).then(setData))
          }}
        />
      )}
    </div>
  )
}
