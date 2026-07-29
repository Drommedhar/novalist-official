import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { rpc } from '../../rpc/client'
import { BookAnalyticsCard } from './BookAnalyticsCard'
import { TargetsCard } from './TargetsCard'
import { PremiseCard } from './PremiseCard'
import { ArcsCard } from './ArcsCard'
import { TensionCard } from './TensionCard'
import { SceneAxisCard } from './SceneAxisCard'
import { InputDialog } from '../../shell/InputDialog'
import { useShellStore } from '../../stores/shellStore'
import { useProjectStore } from '../../stores/projectStore'
import './dashboard.css'

interface DashboardDto {
  projectName: string
  author: string
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
  daysRemaining: number
  wordsPerDayNeeded: number
  todayWords: number
  currentStreak: number
  history: {
    longestStreak: number
    daysWritten: number
    daysHitGoal: number
    writingDaysConsidered: number
    bestDayWords: number
    bestDayDate: string
    averagePerWritingDay: number
    adaptive: boolean
    writingDays: number[]
  }
  longestChapterWords: number
  shortestChapterWords: number
  averageSceneWords: number
  outlineCount: number
  firstDraftCount: number
  revisedCount: number
  editedCount: number
  finalCount: number
  statusBreakdown: { status: string; count: number; wordCount: number }[]
  chapterPacing: { title: string; words: number }[]
  maxChapterWords: number
  echoPhrases: { phrase: string; count: number }[]
  wordHistory: { date: string; words: number; metGoal: boolean }[]
  recentActivity: {
    sceneTitle: string
    chapterTitle: string
    chapterGuid: string
    sceneId: string
    timestamp: string
  }[]
}

const RANGES = [30, 90, 365]

const STATUS_SUMMARY: { key: keyof DashboardDto; label: string }[] = [
  { key: 'outlineCount', label: 'dashboard.statusOutline' },
  { key: 'firstDraftCount', label: 'dashboard.statusFirstDraft' },
  { key: 'revisedCount', label: 'dashboard.statusRevised' },
  { key: 'editedCount', label: 'dashboard.statusEdited' },
  { key: 'finalCount', label: 'dashboard.statusFinal' }
]

interface StageTally {
  key: string
  label: string
  color: string
  countsAsWritten: boolean
  sceneCount: number
  wordCount: number
}

export function DashboardView(): React.JSX.Element {
  const { t } = useTranslation()
  const [stageBreakdown, setStageBreakdown] = useState<StageTally[]>([])
  const mainView = useShellStore((s) => s.mainView)
  const [data, setData] = useState<DashboardDto | null>(null)
  const [range, setRange] = useState(30)
  const [editingGoal, setEditingGoal] = useState<'daily' | 'project' | null>(null)
  const [cover, setCover] = useState<string | null>(null)
  const [banner, setBanner] = useState<string | null>(null)

  useEffect(() => {
    if (mainView !== 'dashboard') return
    void rpc.request<DashboardDto>('dashboard/get', [range]).then(setData)
  }, [mainView, range])

  useEffect(() => {
    if (mainView !== 'dashboard') return
    void rpc.request<string | null>('dashboard/getCover').then(setCover)
    void rpc.request<string | null>('dashboard/getBanner').then(setBanner)
    void rpc.request<StageTally[]>('stages/breakdown').then(setStageBreakdown)
  }, [mainView])

  const stageTotal = Math.max(
    1,
    stageBreakdown.reduce((sum, row) => sum + row.sceneCount, 0)
  )

  const setPacing = async (adaptive: boolean, writingDays: number[]): Promise<void> => {
    await rpc.request('dashboard/setPacing', [adaptive, writingDays])
    setData(await rpc.request<DashboardDto>('dashboard/get', [range]))
  }

  const changeBanner = async (): Promise<void> => {
    const path = await window.novalist.pickFile(t('dashboard.pickBannerTitle'), 'images')
    if (!path) return
    await rpc.request('dashboard/setBanner', [path])
    setBanner(await rpc.request<string | null>('dashboard/getBanner'))
  }

  const removeBanner = async (): Promise<void> => {
    await rpc.request('dashboard/setBanner', [null])
    setBanner(await rpc.request<string | null>('dashboard/getBanner'))
  }

  const changeCover = async (): Promise<void> => {
    const path = await window.novalist.pickFile(t('dashboard.pickCoverTitle'), 'images')
    if (!path) return
    await rpc.request('dashboard/setCover', [path])
    setCover(await rpc.request<string | null>('dashboard/getCover'))
    setBanner(await rpc.request<string | null>('dashboard/getBanner'))
  }

  const removeCover = async (): Promise<void> => {
    await rpc.request('dashboard/setCover', [null])
    setCover(null)
    setBanner(await rpc.request<string | null>('dashboard/getBanner'))
  }

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
      <div className="dashboard-cover">
        {banner ? (
          <img
            className="dashboard-cover-img"
            src={`novalist-project://nl/${encodeURI(banner)}`}
            alt={data.projectName}
          />
        ) : (
          <div className="dashboard-cover-empty">{t('dashboard.noBanner')}</div>
        )}
        <div className="dashboard-cover-actions">
          <span className="dashboard-cover-tag">{t('dashboard.bannerLabel')}</span>
          <button className="dashboard-cover-btn" onClick={() => void changeBanner()}>
            {banner ? t('dashboard.changeBanner') : t('dashboard.addBanner')}
          </button>
          {banner && (
            <button className="dashboard-cover-btn" onClick={() => void removeBanner()}>
              {t('dashboard.removeBanner')}
            </button>
          )}
        </div>
      </div>

      <div className="dashboard-bookcover">
        <div className="dashboard-bookcover-preview">
          {cover ? (
            <img
              className="dashboard-bookcover-img"
              src={`novalist-project://nl/${encodeURI(cover)}`}
              alt={data.projectName}
            />
          ) : (
            <div className="dashboard-bookcover-empty">{t('dashboard.noCover')}</div>
          )}
        </div>
        <div className="dashboard-bookcover-body">
          <div className="dashboard-bookcover-label">{t('dashboard.bookCoverLabel')}</div>
          <div className="dashboard-bookcover-hint">{t('dashboard.bookCoverHint')}</div>
          <div className="dashboard-cover-actions dashboard-bookcover-actions">
            <button className="dashboard-cover-btn" onClick={() => void changeCover()}>
              {cover ? t('dashboard.changeCover') : t('dashboard.addCover')}
            </button>
            {cover && (
              <button className="dashboard-cover-btn" onClick={() => void removeCover()}>
                {t('dashboard.removeCover')}
              </button>
            )}
          </div>
        </div>
      </div>

      <div className="dashboard-header">
        <h1 className="dashboard-title">{data.projectName}</h1>
        {data.author && <div className="dashboard-author">{data.author}</div>}
        <div className="dashboard-subtitle">{t('dashboard.subtitle')}</div>
      </div>

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
                    const next = on
                      ? current.filter((d) => d !== day)
                      : [...current, day].sort()
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
            <div className="dashboard-deadline-detail">
              <div>
                <div className="dashboard-detail-label">{t('dashboard.deadline')}</div>
                <div className="dashboard-detail-value">{data.deadline}</div>
              </div>
              <div>
                <div className="dashboard-detail-label">{t('dashboard.daysLeft')}</div>
                <div className="dashboard-detail-value">{data.daysRemaining}</div>
              </div>
              <div>
                <div className="dashboard-detail-label">{t('dashboard.neededPerDay')}</div>
                <div className="dashboard-detail-value">
                  {data.wordsPerDayNeeded.toLocaleString()}
                </div>
              </div>
            </div>
          )}
        </div>
      </div>

      <div className="dashboard-card">
        <div className="dashboard-avg-grid">
          <div>
            <div className="dashboard-avg-value">{data.averageChapterWords.toLocaleString()}</div>
            <div className="dashboard-avg-label">{t('dashboard.avgPerChapter')}</div>
          </div>
          <div>
            <div className="dashboard-avg-value">{data.readingTimeMinutes} min</div>
            <div className="dashboard-avg-label">{t('dashboard.estReadingTime')}</div>
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
          <div className="dashboard-status-summary">
            {STATUS_SUMMARY.map((s) => (
              <div key={s.key}>
                <div className="dashboard-summary-count">{data[s.key] as number}</div>
                <div className="dashboard-summary-count-label">{t(s.label)}</div>
              </div>
            ))}
          </div>
        </div>
      )}

      {/* What the book is, before what is left to write of it. */}
      <PremiseCard />

      {/* What the book's tension does across its length - the one shape the
          per-scene intensity figure has anything to say in. */}
      <TensionCard />
      <SceneAxisCard />

      {/* Every character's arc against the book: the Codex holds one at a
          time, which cannot say whether the turns are spread or bunched. */}
      <ArcsCard />

      {/* Word targets, which had no home outside the binder's context menu. */}
      <TargetsCard />

      {/* Scene stages, which the chapter breakdown above cannot express: a
          chapter mid-revision holds scenes at four different stages. */}
      {stageBreakdown.length > 0 && (
        <div className="dashboard-card">
          <div className="dashboard-card-title">{t('stages.title')}</div>
          {stageBreakdown.map((row) => (
            <div key={row.key || 'unset'} className="dashboard-status-row">
              <span className="dashboard-status-dot" style={{ background: row.color }} />
              <span className="dashboard-status-name">
                {row.label || t('stages.untriaged')}
              </span>
              <div className="dashboard-bar-track dashboard-status-track">
                <div
                  className="dashboard-bar-fill"
                  style={{ width: `${Math.round((row.sceneCount / stageTotal) * 100)}%` }}
                />
              </div>
              <span className="dashboard-status-count">
                {row.sceneCount} - {row.wordCount.toLocaleString()} {t('shell.words')}
              </span>
            </div>
          ))}
        </div>
      )}

      {/* Where things sit across the whole book rather than one scene at a time. */}
      <BookAnalyticsCard />

      {data.chapterPacing.length > 0 && (
        <div className="dashboard-card">
          <div className="dashboard-card-title">{t('dashboard.pacingAnalysis')}</div>
          <div className="dashboard-pacing-summary">
            <div>
              <div className="dashboard-summary-value">
                {data.longestChapterWords.toLocaleString()}
              </div>
              <div className="dashboard-summary-label">{t('dashboard.longestChapter')}</div>
            </div>
            <div>
              <div className="dashboard-summary-value">
                {data.shortestChapterWords.toLocaleString()}
              </div>
              <div className="dashboard-summary-label">{t('dashboard.shortestChapter')}</div>
            </div>
            <div>
              <div className="dashboard-summary-value">
                {Math.round(data.averageSceneWords).toLocaleString()}
              </div>
              <div className="dashboard-summary-label">{t('dashboard.avgSceneWords')}</div>
            </div>
          </div>
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
          <div className="dashboard-echo-desc">{t('dashboard.echoDescription')}</div>
          <div className="dashboard-echoes">
            {data.echoPhrases.map((e) => (
              <span key={e.phrase} className="dashboard-echo">
                {e.phrase} <b>{e.count}</b>
              </span>
            ))}
          </div>
        </div>
      )}

      {data.recentActivity.length > 0 && (
        <div className="dashboard-card">
          <div className="dashboard-card-title">{t('dashboard.recentActivity')}</div>
          {data.recentActivity.map((a, i) => (
            <button
              key={i}
              type="button"
              className="dashboard-activity-row dashboard-activity-link"
              onClick={() => void useProjectStore.getState().openScene(a.chapterGuid, a.sceneId)}
            >
              <div className="dashboard-activity-main">
                <div className="dashboard-activity-scene">{a.sceneTitle}</div>
                <div className="dashboard-activity-chapter">{a.chapterTitle}</div>
              </div>
              <div className="dashboard-activity-time">{a.timestamp}</div>
            </button>
          ))}
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
