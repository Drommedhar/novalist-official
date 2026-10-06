import { computeStats, LEVEL_CLASS, type Level } from './textStatistics'
import { useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { BarChart3, GitBranch, Headphones, Timer } from 'lucide-react'
import { rpc } from '../rpc/client'
import { ExtensionStatusItems } from './ExtensionStatusItems'
import { useShellStore } from '../stores/shellStore'
import { useBookScope, useProjectStore } from '../stores/projectStore'
import { loadBookScoped } from '../stores/bookScopedLoad'
import { elapsedSeconds, formatDuration, sprintWords, useSprintStore } from '../stores/sprintStore'
import { SprintPanel } from './SprintPanel'
import { useSettingsStore } from '../stores/settingsStore'
import { useAudiobookStore } from '../stores/audiobookStore'
import { onPluginContributionsChanged, pluginStatusItems } from './pluginHost'
import './statusbar.css'

// Whole-project figures the status bar surfaces (goal progress + the overview
// popover). A subset of dashboard/get's DashboardDto; the extra fields are
// simply ignored. Refreshed on project change and a slow interval so the bar
// never runs a fresh-array zustand selector to derive them.
interface ProjectOverview {
  totalWords: number
  chapterCount: number
  sceneCount: number
  characterCount: number
  locationCount: number
  readingTimeMinutes: number
  dailyGoalCurrent: number
  dailyGoalTarget: number
  dailyGoalPercent: number
  projectGoalTarget: number
  projectGoalPercent: number
}

interface GitIndicator {
  branchName: string
  changedFiles: unknown[]
}

// Per-chapter / per-scene breakdown for the overview popover (dashboard/overview).
interface SceneOverview {
  title: string
  words: number
}
interface ChapterOverview {
  title: string
  words: number
  readability: number
  readabilityLevel: string | null
  scenes: SceneOverview[]
  /** Estimated printed pages. An estimate, and the popover says so. */
  pages: number
}
interface ProjectBreakdown {
  projectName: string
  chapters: ChapterOverview[]
  pages: number
  wordsPerPage: number
}

// Maps the backend's localized readability-level label to the badge palette.
const LEVEL_BY_LABEL: Record<string, Level> = {
  'Very easy': 'veryEasy',
  Easy: 'easy',
  Moderate: 'moderate',
  Difficult: 'difficult',
  'Very difficult': 'veryDifficult'
}

const OVERVIEW_REFRESH_MS = 20000

function useStatusBar() {
  // Plugins add and remove these at any time, so the bar listens rather than
  // reading once.
  const [pluginItems, setPluginItems] = useState([...pluginStatusItems()])
  useEffect(
    () => onPluginContributionsChanged(() => setPluginItems([...pluginStatusItems()])),
    []
  )

  const { t } = useTranslation()
  const backendVersion = useShellStore((s) => s.backendVersion)
  const setMainView = useShellStore((s) => s.setMainView)
  const isLoaded = useProjectStore((s) => s.isLoaded)
  const chapters = useProjectStore((s) => s.chapters)
  const plainText = useProjectStore((s) => s.openScenePlainText)
  const language = useSettingsStore((s) => s.view?.effective.autoReplacementLanguage ?? 'en')
  const openScene = useProjectStore((s) =>
    s.chapters
      .find((c) => c.guid === s.openChapterGuid)
      ?.scenes.find((sc) => sc.id === s.openSceneId)
  )

  const [sprintOpen, setSprintOpen] = useState(false)
  const sprintRunning = useSprintStore((s) => s.running)
  const sprintBanked = useSprintStore((s) => s.bankedSeconds)
  const sprintTarget = useSprintStore((s) => s.targetMinutes)
  // Subscribed so the status bar clock re-renders every second.
  useSprintStore((s) => s.tick)

  const { overview, git, overviewOpen, setOverviewOpen, breakdown, toggleOverview } = useStatusOverview(isLoaded)

  const totalWords = chapters.reduce(
    (sum, c) => sum + c.scenes.reduce((s2, sc) => s2 + sc.wordCount, 0),
    0
  )
  const sceneCount = chapters.reduce((sum, c) => sum + c.scenes.length, 0)
  const avgChapterWords =
    chapters.length > 0 ? Math.round(totalWords / chapters.length) : 0

  const stats = useMemo(
    () => (plainText === null ? null : computeStats(plainText, language)),
    [plainText, language]
  )

  const maxChapterWords =
    breakdown && breakdown.chapters.length > 0
      ? Math.max(1, ...breakdown.chapters.map((c) => c.words))
      : 1

  return { pluginItems, t, backendVersion, setMainView, isLoaded, chapters, openScene, sprintOpen, setSprintOpen, sprintRunning, sprintBanked, sprintTarget, overview, git, overviewOpen, setOverviewOpen, breakdown, totalWords, sceneCount, avgChapterWords, stats, toggleOverview, maxChapterWords }
}

export function StatusBar(): React.JSX.Element {
  const state = useStatusBar()
  const { pluginItems, t, backendVersion, setMainView, isLoaded, openScene, sprintOpen, setSprintOpen, sprintRunning, sprintBanked, sprintTarget, git, totalWords, stats, toggleOverview } = state
  return (
    <footer className="status-bar">
      <span className="status-left">
        {openScene && (
          <span className="status-stats">
            <span
              title={
                stats
                  ? `${t('statusBar.characters', { value: stats.characterCount })}\n${t(
                      'statusBar.charactersNoSpaces',
                      { value: stats.characterCountNoSpaces }
                    )}`
                  : undefined
              }
            >
              {openScene.wordCount.toLocaleString()} {t('shell.words')}
            </span>
            {stats && stats.readingTimeMinutes > 0 && (
              <span className="status-dim">
                {t('statusBar.readingTime', { minutes: stats.readingTimeMinutes })}
              </span>
            )}
            {stats && stats.readabilityScore > 0 && (
              <span
                className={`status-readability-badge ${LEVEL_CLASS[stats.readabilityLevel]}`}
                title={`${t('statusBar.readability', { score: stats.readabilityScore })} - ${t(
                  `statusBar.readabilityLevel.${stats.readabilityLevel}`
                )}`}
              >
                {stats.readabilityScore}
              </span>
            )}
          </span>
        )}
        <ExtensionStatusItems />
        <AudiobookProgress />
      </span>


      <span className="status-center-wrap">
        {isLoaded ? (
          <button
            type="button"
            className="status-center status-overview-trigger"
            onClick={toggleOverview}
            title={t('statusBar.overviewTooltip')}
          >
            <BarChart3 size={13} strokeWidth={1.75} />
            <span className="status-metric">
              {`${totalWords.toLocaleString()} ${t('shell.words')}`}
            </span>
            <span className="status-project-label">{t('statusBar.projectStatus')}</span>
          </button>
        ) : (
          <span className="status-center" />
        )}
      <StatusOverview state={state} />
      </span>

      <span className="status-right">
        {isLoaded && (
          <button
            type="button"
            className={`status-sprint${sprintRunning ? ' running' : ''}`}
            onClick={() => setSprintOpen(true)}
            title={t('sprint.title')}
          >
            <Timer size={13} strokeWidth={2} />
            {sprintRunning || sprintBanked > 0 ? (
              <>
                {formatDuration(
                  sprintTarget > 0
                    ? Math.max(0, sprintTarget * 60 - elapsedSeconds())
                    : elapsedSeconds()
                )}
                <span className="status-dim">
                  {sprintWords().toLocaleString()} {t('shell.words')}
                </span>
              </>
            ) : (
              t('sprint.start')
            )}
          </button>
        )}
        {git && (
          <button
            type="button"
            className="status-git"
            onClick={() => setMainView('git')}
            title={t('statusBar.gitTooltip', { count: git.changedFiles.length })}
          >
            <GitBranch size={12} aria-hidden />
            <span className="status-git-branch">{git.branchName}</span>
            {git.changedFiles.length > 0 && (
              <span className="status-git-count">{git.changedFiles.length}</span>
            )}
          </button>
        )}
        <span
          className={`status-backend${backendVersion ? ' connected' : ''}`}
          title={
            backendVersion
              ? t('shell.backendConnected', { version: backendVersion })
              : t('shell.backendConnecting')
          }
          aria-label={
            backendVersion
              ? t('shell.backendConnected', { version: backendVersion })
              : t('shell.backendConnecting')
          }
        >
          <span className="status-backend-dot" aria-hidden />
        </span>
      </span>
      {/* Whatever plugins put here, each carrying the name of whoever added
          it: when one misbehaves the writer needs to know which to turn off. */}
      {pluginItems.map((item) => (
        <span
          key={`${item.extensionId}:${item.id}`}
          className="status-plugin-item"
          title={item.tooltip ?? item.extensionId}
          onClick={item.onClick}
        >
          {item.text}
        </span>
      ))}
      {sprintOpen && <SprintPanel onClose={() => setSprintOpen(false)} />}
    </footer>
  )
}

/**
 * The audiobook render, while one is running.
 *
 * In the status bar rather than only in the Export view because the render
 * takes hours and the writer is expected to go on writing through it. A job
 * that can only be seen on the screen it was started from is a job nobody can
 * tell is still going.
 */
function AudiobookProgress(): React.JSX.Element | null {
  const { t } = useTranslation()
  const status = useAudiobookStore((s) => s.status)
  const refresh = useAudiobookStore((s) => s.refresh)
  const workspaceBusy = useProjectStore((s) => s.workspaceBusy)
  const workspaceEpoch = useProjectStore((s) => s.workspaceEpoch)

  useEffect(() => {
    // A render begun before this window opened is still worth showing.
    if (!workspaceBusy) void refresh()
  }, [refresh, workspaceBusy, workspaceEpoch])

  if (status === null || (status.phase !== 'rendering' && status.phase !== 'packaging')) return null

  const percent =
    status.segmentsTotal > 0 ? Math.round((status.segmentsDone / status.segmentsTotal) * 100) : 0

  return (
    <span
      className="status-audiobook"
      title={t('audiobook.renderingChapter', {
        index: status.chapterIndex,
        total: status.chapterCount,
        title: status.chapterTitle
      })}
    >
      <Headphones size={13} aria-hidden="true" />
      {status.phase === 'packaging' ? t('audiobook.packaging') : `${percent}%`}
    </span>
  )
}

function GoalProgress({ label, target, percent }: {
  label: string
  target: number
  percent: number
}): React.JSX.Element | null {
  if (target <= 0) return null
  return (
    <span className="status-goal" title={`${percent}%`}>
      <span className="status-goal-label">{label}</span>
      <span className="status-goal-track">
        <span className="status-goal-fill" style={{ width: `${percent}%` }} />
      </span>
    </span>
  )
}

function StatusOverview({ state }: { state: ReturnType<typeof useStatusBar> }): React.JSX.Element {
  const { t, chapters, overview, overviewOpen, setOverviewOpen, breakdown, sceneCount, avgChapterWords, maxChapterWords } = state
  return <>
        {overviewOpen && (
          <>
            <div className="status-overview-backdrop" onClick={() => setOverviewOpen(false)} />
            <div className="status-overview-popover" role="dialog">
              <div className="status-overview-title">
                {breakdown?.projectName ?? t('dashboard.projectOverview')}
              </div>
              <div className="status-overview-summary">
                <span>{chapters.length.toLocaleString()} {t('statusBar.chapters')}</span>
                <span>{sceneCount.toLocaleString()} {t('statusBar.scenes')}</span>
                {overview && (
                  <>
                    <span>{overview.characterCount.toLocaleString()} {t('statusBar.charactersFull')}</span>
                    <span>{overview.locationCount.toLocaleString()} {t('statusBar.locations')}</span>
                    <span>{t('statusBar.readingTime', { minutes: overview.readingTimeMinutes })}</span>
                  </>
                )}
                <span>
                  {t('statusBar.averageChapter', { value: avgChapterWords.toLocaleString() })}
                </span>
              </div>
              {overview && (overview.dailyGoalTarget > 0 || overview.projectGoalTarget > 0) && (
                <div className="status-goals status-overview-goals">
                  <GoalProgress
                    label={t('statusBar.dailyGoalShort', {
                      current: overview.dailyGoalCurrent.toLocaleString(),
                      target: overview.dailyGoalTarget.toLocaleString()
                    })}
                    target={overview.dailyGoalTarget}
                    percent={overview.dailyGoalPercent}
                  />
                  <GoalProgress
                    label={t('statusBar.projectGoalShort', {
                      current: overview.totalWords.toLocaleString(),
                      target: overview.projectGoalTarget.toLocaleString()
                    })}
                    target={overview.projectGoalTarget}
                    percent={overview.projectGoalPercent}
                  />
                </div>
              )}
              <div className="status-overview-cols">
                <span>{t('overview.chapterColumn')}</span>
                <span>{t('overview.wordsColumn')}</span>
                <span>{t('overview.pagesColumn')}</span>
                <span>{t('overview.readabilityColumn')}</span>
              </div>
              <div className="status-overview-list">
                {!breakdown && <div className="status-dim">{t('shell.backendConnecting')}</div>}
                {breakdown && breakdown.chapters.length === 0 && (
                  <div className="status-dim">{t('overview.noChapters')}</div>
                )}
                {breakdown?.chapters.map((chapter, ci) => (
                  <div key={ci} className="status-overview-chapter">
                    <div className="status-overview-row">
                      <span className="status-overview-name">{chapter.title}</span>
                      <span className="status-overview-words">
                        {chapter.words.toLocaleString()}
                        <span className="status-overview-bar">
                          <span
                            className="status-overview-bar-fill"
                            style={{ width: `${Math.round((chapter.words / maxChapterWords) * 100)}%` }}
                          />
                        </span>
                      </span>
                      <span className="status-overview-pages">{chapter.pages || ''}</span>
                      <span className="status-overview-read">
                        {chapter.readabilityLevel ? (
                          <span
                            className={`status-readability-badge ${
                              LEVEL_CLASS[LEVEL_BY_LABEL[chapter.readabilityLevel] ?? 'moderate']
                            }`}
                            title={chapter.readabilityLevel}
                          >
                            {chapter.readability}
                          </span>
                        ) : (
                          <span className="status-dim">–</span>
                        )}
                      </span>
                    </div>
                    {chapter.scenes.map((scene, si) => (
                      <div key={si} className="status-overview-row status-overview-scene">
                        <span className="status-overview-name">{scene.title}</span>
                        <span className="status-overview-words">
                          {scene.words.toLocaleString()}
                          <span className="status-overview-bar">
                            <span
                              className="status-overview-bar-fill"
                              style={{
                                width: `${Math.round((scene.words / maxChapterWords) * 100)}%`
                              }}
                            />
                          </span>
                        </span>
                        <span className="status-overview-pages" />
                        <span className="status-overview-read" />
                      </div>
                    ))}
                  </div>
                ))}
              </div>
              {breakdown && breakdown.pages > 0 && (
                <div className="status-overview-pages-note">
                  {t('overview.pagesEstimate', {
                    pages: breakdown.pages.toLocaleString(),
                    wordsPerPage: breakdown.wordsPerPage
                  })}
                </div>
              )}
            </div>
          </>
        )}
  </>
}

function useStatusOverview(isLoaded: boolean) {
  const bookScope = useBookScope()
  const workspaceBusy = useProjectStore((s) => s.workspaceBusy)
  const workspaceEpoch = useProjectStore((s) => s.workspaceEpoch)
  const [overview, setOverview] = useState<ProjectOverview | null>(null)
  const [git, setGit] = useState<GitIndicator | null | undefined>(undefined)
  const [overviewOpen, setOverviewOpen] = useState(false)
  const [breakdown, setBreakdown] = useState<ProjectBreakdown | null>(null)

  // Pull whole-project figures + git status when a project opens and on a slow
  // interval thereafter (dashboard/get is comparatively heavy). git/status
  // returns null outside a repository, which hides the git indicator.
  useEffect(() => {
    if (workspaceBusy) return
    setOverview(null)
    setGit(undefined)
    setOverviewOpen(false)
    setBreakdown(null)
    if (!isLoaded) {
      return
    }
    let active = true
    const load = (): void => {
      void loadBookScoped(useProjectStore.getState,
        () => rpc.request<ProjectOverview>('dashboard/get', [1]),
        (d) => {
          if (active) setOverview(d)
        })
        .catch(() => {})
      void loadBookScoped(useProjectStore.getState,
        () => rpc.request<GitIndicator | null>('git/status'),
        (g) => {
          if (active) setGit(g)
        })
        .catch(() => {
          if (active) setGit(null)
        })
    }
    load()
    const id = window.setInterval(load, OVERVIEW_REFRESH_MS)
    return () => {
      active = false
      window.clearInterval(id)
    }
  }, [isLoaded, bookScope, workspaceEpoch, workspaceBusy])

  const toggleOverview = (): void => {
    const next = !overviewOpen
    setOverviewOpen(next)
    if (next) {
      void loadBookScoped(useProjectStore.getState,
        () => rpc.request<ProjectOverview>('dashboard/get', [1]), setOverview)
        .catch(() => {})
      void loadBookScoped(useProjectStore.getState,
        () => rpc.request<ProjectBreakdown>('dashboard/overview'), setBreakdown)
        .catch(() => {})
    }
  }

  return { overview, git, overviewOpen, setOverviewOpen, breakdown, toggleOverview }
}
