import { MotionPresence } from '../../shell/MotionPresence'
import { rpc } from '../../rpc/client'
import { TargetsCard } from './TargetsCard'
import { PremiseCard } from './PremiseCard'
import { ArcsCard } from './ArcsCard'
import { SubmissionsCard } from './SubmissionsCard'
import { TensionCard } from './TensionCard'
import { CastAbsenceCard } from './CastAbsenceCard'
import { SceneAxisCard } from './SceneAxisCard'
import { InputDialog } from '../../shell/InputDialog'
import { useBookScope, useProjectStore } from '../../stores/projectStore'
import './dashboard.css'
import { type DashboardDto, useDashboard, deriveDashboard, type BookDashboardState } from './dashboardModel'
import { DailyWritingGoal } from './DailyWritingGoal'
import { DashboardWordStatistics, DashboardStoryProgress } from './DashboardStatistics'

/**
 * A dashboard belongs to one book, including the cards that fetch their own
 * reports when they mount. Giving that whole surface the shared book-scope key
 * refreshes all one-shot reads when the active book changes.
 */
export function DashboardView(): React.JSX.Element {
  const bookScope = useBookScope()
  return <BookDashboard key={bookScope} />
}

function BookDashboard(): React.JSX.Element {
  const { failure, t, setAttempt, data, chapters, openChapterGuid, openSceneId, banner, projectName, changeBanner, removeBanner, cover, changeCover, removeCover, setEditingGoal, setPacing, range, setRange, stageBreakdown, stageTotal, editingGoal, setData } = useDashboard()

  if (failure !== null) {
    return (
      <div className="main-placeholder">
        <p>{t('shell.viewLoadFailed')}</p>
        <button className="btn-secondary" onClick={() => setAttempt((n) => n + 1)}>
          {t('shell.retry')}
        </button>
        <details className="view-error-details">
          <summary>{t('shell.errorDetails')}</summary>
          <code>{failure}</code>
        </details>
      </div>
    )
  }

  if (!data) return <div className="main-placeholder">{t('shell.viewLoading')}</div>

  const {maxBar, statusTotal, metrics, resume} = deriveDashboard(data, chapters, openChapterGuid, openSceneId)

  return (
    <div className="dashboard">
      <DashboardArtwork banner={banner} projectName={projectName} t={t} changeBanner={changeBanner} removeBanner={removeBanner} cover={cover} data={data} changeCover={changeCover} removeCover={removeCover} />

      {resume && (
        <div className="dashboard-card dashboard-resume">
          <div className="dashboard-resume-copy">
            <div className="dashboard-card-title">{t('desktopRefresh.continueWriting')}</div>
            <h2>{resume.sceneTitle}</h2>
            <div className="dashboard-author">{resume.chapterTitle}</div>
          </div>
          <button
            className="dialog-button primary"
            onClick={() =>
              void useProjectStore.getState().openScene(resume.chapterGuid, resume.sceneId)
            }
          >
            {t('desktopRefresh.continueWriting')}
          </button>
        </div>
      )}
      {data.recentActivity.length > 0 && (
        <div className="dashboard-card dashboard-recent">
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

      <div className="dashboard-metrics">
        {metrics.map((m) => (
          <div key={m.key} className="dashboard-card dashboard-metric">
            <div className="dashboard-metric-value">{m.value}</div>
            <div className="dashboard-metric-label">{t(m.key)}</div>
          </div>
        ))}
      </div>

      <div className="dashboard-columns">
        <DailyWritingGoal setEditingGoal={setEditingGoal} t={t} data={data} setPacing={setPacing} range={range} setRange={setRange} maxBar={maxBar} />

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

          <DashboardWordStatistics data={data} t={t} statusTotal={statusTotal} />

      {/* What the book is, before what is left to write of it. */}
      <PremiseCard />

      {/* What the book's tension does across its length - the one shape the
          per-scene intensity figure has anything to say in. */}
      <TensionCard />
      <CastAbsenceCard />
      <SceneAxisCard />

      {/* Every character's arc against the book: the Codex holds one at a
          time, which cannot say whether the turns are spread or bunched. */}
      <ArcsCard />

      {/* Word targets, which had no home outside the binder's context menu. */}
      <TargetsCard />
      {/* Where the book went and what came back. */}
      <SubmissionsCard />

          <DashboardStoryProgress stageBreakdown={stageBreakdown} t={t} stageTotal={stageTotal} data={data} />

      <MotionPresence>{editingGoal && (
        <InputDialog
          title={
            editingGoal === 'daily' ? t('settings.dailyWordGoal') : t('settings.projectWordGoal')
          }
          placeholder={String(
            editingGoal === 'daily' ? data.dailyGoalTarget : data.projectGoalTarget
          )}
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
      )}</MotionPresence>
    </div>
  )
}

function DashboardArtwork({ banner, projectName, t, changeBanner, removeBanner, cover, data, changeCover, removeCover }: { banner: BookDashboardState['banner']; projectName: BookDashboardState['projectName']; t: BookDashboardState['t']; changeBanner: BookDashboardState['changeBanner']; removeBanner: BookDashboardState['removeBanner']; cover: BookDashboardState['cover']; data: NonNullable<BookDashboardState['data']>; changeCover: BookDashboardState['changeCover']; removeCover: BookDashboardState['removeCover'] }): React.JSX.Element {
  return (
    <div className="dashboard-book-hero">
      <div className="dashboard-cover">
        {banner ? (
          <img
            className="dashboard-cover-img"
            src={`novalist-project://nl/${encodeURI(banner)}`}
            alt={projectName}
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
              alt={projectName}
            />
          ) : (
            <div className="dashboard-bookcover-empty">{t('dashboard.noCover')}</div>
          )}
        </div>
        <div className="dashboard-bookcover-body">
          <div className="dashboard-header">
            <h1 className="dashboard-title">{projectName}</h1>
            {data.author && <div className="dashboard-author">{data.author}</div>}
            <div className="dashboard-subtitle">{t('dashboard.subtitle')}</div>
          </div>

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
    </div>
  )
}
