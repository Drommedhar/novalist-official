import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { rpc } from '../../rpc/client'
import { useProjectStore } from '../../stores/projectStore'

export interface DashboardDto {
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
  week: Horizon
  month: Horizon
}

/** Progress over a horizon longer than a day. */
interface Horizon {
  current: number
  goal: number
  percent: number
  /** Writing days left in the horizon, today included. */
  daysLeft: number
}

interface StageTally {
  key: string
  label: string
  color: string
  countsAsWritten: boolean
  sceneCount: number
  wordCount: number
}

export function useDashboard() {
  const { t } = useTranslation()
  const projectName = useProjectStore((s) => s.projectName) ?? ''
  const chapters = useProjectStore((s) => s.chapters)
  const openChapterGuid = useProjectStore((s) => s.openChapterGuid)
  const openSceneId = useProjectStore((s) => s.openSceneId)
  const {cover, banner, changeBanner, removeBanner, changeCover, removeCover} = useDashboardArtwork(t)
  const [stageBreakdown, setStageBreakdown] = useState<StageTally[]>([])
  const [data, setData] = useState<DashboardDto | null>(null)
  const [range, setRange] = useState(30)
  const [editingGoal, setEditingGoal] = useState<'daily' | 'project' | null>(null)
  /** Why it failed, for the details nobody should have to open DevTools for. */
  const [failure, setFailure] = useState<string | null>(null)
  /** Bumped by Try again, which is the whole of the retry. */
  const [attempt, setAttempt] = useState(0)

  /**
   * The dashboard's own figures.
   *
   * Two things went wrong here and read as one. The request had no failure
   * path, so anything that stopped it resolving left this screen on its
   * placeholder for ever, with nothing said and nothing to press. And the
   * placeholder claimed the app was still connecting to the core process,
   * which by this point it certainly is not - so a slow or failed read looked
   * like a broken backend.
   */
  useEffect(() => {
    let current = true
    setFailure(null)
    setData(null)
    void rpc
      .request<DashboardDto>('dashboard/get', [range])
      .then((next) => current && setData(next))
      .catch((error: unknown) => {
        // Said out loud rather than swallowed. "It could not be worked out" is
        // no use to the person it happens to and no use to whoever has to fix
        // it either; what the backend actually said is the only thing that is.
        console.error('[dashboard] dashboard/get failed', error)
        if (current) {
          setFailure(String((error as { message?: string })?.message ?? error))
        }
      })
    return () => {
      current = false
    }
  }, [range, attempt])

  useEffect(() => {
    void rpc.request<StageTally[]>('stages/breakdown').then(setStageBreakdown)
  }, [])

  const stageTotal = Math.max(
    1,
    stageBreakdown.reduce((sum, row) => sum + row.sceneCount, 0)
  )

  const setPacing = async (adaptive: boolean, writingDays: number[]): Promise<void> => {
    await rpc.request('dashboard/setPacing', [adaptive, writingDays])
    setData(await rpc.request<DashboardDto>('dashboard/get', [range]))
  }

  return {failure, t, setAttempt, data, chapters, openChapterGuid, openSceneId, banner, projectName, changeBanner, removeBanner, cover, changeCover, removeCover, setEditingGoal, setPacing, range, setRange, stageBreakdown, stageTotal, editingGoal, setData}
}

function useDashboardArtwork(t: ReturnType<typeof useTranslation>['t']) {
  const [cover, setCover] = useState<string | null>(null)
  const [banner, setBanner] = useState<string | null>(null)
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

  useEffect(() => {
    void rpc.request<string | null>('dashboard/getCover').then(setCover)
    void rpc.request<string | null>('dashboard/getBanner').then(setBanner)
  }, [])
  return {cover, banner, changeBanner, removeBanner, changeCover, removeCover}
}

export function deriveDashboard(data: DashboardDto, chapters: ReturnType<typeof useProjectStore.getState>['chapters'], openChapterGuid: string | null, openSceneId: string | null) {
  const maxBar = Math.max(1, ...data.wordHistory.map((b) => b.words))
  const statusTotal = Math.max(
    1,
    data.statusBreakdown.reduce((s, b) => s + b.count, 0)
  )

  const metrics: { key: string; value: string }[] = [
    { key: 'dashboard.words', value: data.totalWords.toLocaleString() },
    { key: 'shell.chapters', value: String(data.chapterCount) },
    { key: 'shell.scenes', value: String(data.sceneCount) },
    { key: 'dashboard.readingTime', value: `${data.readingTimeMinutes} min` },
    { key: 'codexHub.characters', value: String(data.characterCount) },
    { key: 'codexHub.locations', value: String(data.locationCount) }
  ]

  const chapter = chapters.find((c) => c.guid === openChapterGuid)
  const scene = chapter?.scenes.find((sc) => sc.id === openSceneId)
  const first = chapters.find((c) => c.scenes.length > 0)
  const resume =
    scene && chapter
      ? {
          chapterGuid: chapter.guid,
          sceneId: scene.id,
          sceneTitle: scene.title,
          chapterTitle: chapter.title
        }
      : (data.recentActivity[0] ??
        (first
          ? {
              chapterGuid: first.guid,
              sceneId: first.scenes[0].id,
              sceneTitle: first.scenes[0].title,
              chapterTitle: first.title
            }
          : null))

  return {maxBar, statusTotal, metrics, resume}
}

export type BookDashboardState = ReturnType<typeof useDashboard> & ReturnType<typeof deriveDashboard>
