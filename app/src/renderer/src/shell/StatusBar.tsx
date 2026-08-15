import { useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { BarChart3, GitBranch, Timer } from 'lucide-react'
import { rpc } from '../rpc/client'
import { ExtensionStatusItems } from './ExtensionStatusItems'
import { useShellStore } from '../stores/shellStore'
import { useProjectStore } from '../stores/projectStore'
import { elapsedSeconds, formatDuration, sprintWords, useSprintStore } from '../stores/sprintStore'
import { SprintPanel } from './SprintPanel'
import { useSettingsStore } from '../stores/settingsStore'
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

// ── Text statistics (ported from Novalist.Core TextStatistics) ─────────────
// The editor reports plain text on every change and the store strips HTML on
// scene load, so the status bar can compute live counts client-side and match
// the desktop EditorViewModel.UpdateStats figures.

type Level = 'veryEasy' | 'easy' | 'moderate' | 'difficult' | 'veryDifficult'

interface EditorStats {
  characterCount: number
  characterCountNoSpaces: number
  wordCount: number
  readingTimeMinutes: number
  readabilityScore: number
  readabilityLevel: Level
}

const WORD_RE = /[\p{L}\p{N}]+(?:['’-][\p{L}\p{N}]+)*/gu
const STRIP_CHARS = new Set("#*_[]()|`".split(''))

function normalize(text: string): string {
  if (!text.trim()) return ''
  const withoutComments = text.replace(/%%[\s\S]*?%%|<!--[\s\S]*?-->/g, '')
  let out = ''
  for (const ch of withoutComments) {
    if (STRIP_CHARS.has(ch)) continue
    out += ch
  }
  return out
}

function countWords(text: string): number {
  return text.match(WORD_RE)?.length ?? 0
}

function countCharsNoSpaces(text: string): number {
  let n = 0
  for (const ch of text) if (!/\s/.test(ch)) n++
  return n
}

function countSentences(text: string): number {
  if (!text.trim()) return 0
  const parts = text.replace(/\.\.\./g, '.').split(/[.!?]+/)
  let count = 0
  for (const p of parts) if (p.trim() && /[\p{L}\p{N}]/u.test(p)) count++
  return Math.max(1, count)
}

function countVowelGroups(word: string, vowels: string, diphthongs: string[]): number {
  const set = new Set(vowels.split(''))
  let count = 0
  let inGroup = false
  for (const ch of word) {
    const isVowel = set.has(ch.toLowerCase())
    if (isVowel && !inGroup) count++
    inGroup = isVowel
  }
  for (const d of diphthongs) {
    const escaped = d.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
    count -= word.match(new RegExp(escaped, 'gi'))?.length ?? 0
  }
  return Math.max(1, count)
}

function countSyllables(word: string, language: string): number {
  switch (language) {
    case 'en': {
      const trimmed = word.endsWith('e') ? word.slice(0, -1) : word
      return countVowelGroups(trimmed, 'aeiouy', [])
    }
    case 'de-low':
    case 'de-guillemet':
      return countVowelGroups(word, 'aeiouäöü', ['ei', 'ai', 'au', 'eu', 'äu', 'ie'])
    case 'fr':
      return countVowelGroups(word, 'aeiouyàâäéèêëïîôùûü', ['oi', 'ai', 'ei', 'eu', 'au', 'ou', 'ie'])
    case 'es':
    case 'it':
    case 'pt':
      return countVowelGroups(word, 'aeiouàáâãäèéêëìíîïòóôõöùúûü', [
        'ai', 'ei', 'oi', 'ui', 'au', 'eu', 'ou', 'ia', 'ie', 'io', 'iu', 'ua', 'ue', 'uo'
      ])
    case 'ru':
      return countVowelGroups(word, 'аеёиоуыэюя', [])
    case 'pl':
    case 'cs':
    case 'sk':
      return countVowelGroups(word, 'aeiouyáéíóúýàèìòùäëïöü', [])
    default:
      return countVowelGroups(word, 'aeiouyàáâãäåæèéêëìíîïòóôõöøùúûüýÿαεηιουωаеёиоуыэюя', [])
  }
}

function estimateSyllables(text: string, language: string): number {
  const words = text.match(WORD_RE) ?? []
  let total = 0
  for (const word of words) total += countSyllables(word.toLowerCase(), language)
  return Math.max(total, 1)
}

function ari(wordCount: number, sentenceCount: number, charCount: number): number {
  return 4.71 * (charCount / wordCount) + 0.5 * (wordCount / sentenceCount) - 21.43
}

function readabilityLevel(score: number, language: string): Level {
  if (language === 'it') {
    if (score >= 80) return 'veryEasy'
    if (score >= 60) return 'easy'
    if (score >= 40) return 'moderate'
    if (score >= 20) return 'difficult'
    return 'veryDifficult'
  }
  if (score >= 90) return 'veryEasy'
  if (score >= 70) return 'easy'
  if (score >= 50) return 'moderate'
  if (score >= 30) return 'difficult'
  return 'veryDifficult'
}

/** Modifier class per level; the pigments themselves are theme tokens, so the
 * badge follows whichever theme is active instead of pinning its own hex. */
const LEVEL_CLASS: Record<Level, string> = {
  veryEasy: 'level-very-easy',
  easy: 'level-easy',
  moderate: 'level-moderate',
  difficult: 'level-difficult',
  veryDifficult: 'level-very-difficult'
}

function computeStats(plainText: string, language: string): EditorStats {
  const clean = normalize(plainText)
  const wordCount = countWords(clean)
  const characterCount = clean.length
  const characterCountNoSpaces = countCharsNoSpaces(clean)
  const sentenceCount = countSentences(clean)

  let score = 0
  if (wordCount > 0 && sentenceCount > 0) {
    const syllables = estimateSyllables(clean, language)
    const wps = wordCount / sentenceCount
    const spw = syllables / wordCount
    switch (language) {
      case 'en':
        score = 206.835 - 1.015 * wps - 84.6 * spw
        break
      case 'de-low':
      case 'de-guillemet':
        score = 180 - wps - 58.5 * spw
        break
      case 'fr':
        score = 207 - 1.015 * wps - 73.6 * spw
        break
      case 'es':
        score = 206.84 - 0.6 * wps - 102 * spw
        break
      case 'it':
        score = (300 * sentenceCount - 10 * characterCountNoSpaces) / wordCount
        break
      case 'pt':
        score = 206.84 - 0.6 * wps - 102 * spw
        break
      case 'ru':
        score = 206.835 - 1.3 * wps - 60.1 * spw
        break
      default:
        score = 100 - ari(wordCount, sentenceCount, characterCountNoSpaces) * 3
        break
    }
    score = Math.min(100, Math.max(0, score))
  }

  return {
    characterCount,
    characterCountNoSpaces,
    wordCount,
    readingTimeMinutes: wordCount <= 0 ? 0 : Math.ceil(wordCount / 200),
    readabilityScore: Math.round(score),
    readabilityLevel: readabilityLevel(score, language)
  }
}

export function StatusBar(): React.JSX.Element {
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
  const projectPath = useProjectStore((s) => s.projectPath)
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

  const [overview, setOverview] = useState<ProjectOverview | null>(null)
  const [git, setGit] = useState<GitIndicator | null | undefined>(undefined)
  const [overviewOpen, setOverviewOpen] = useState(false)
  const [breakdown, setBreakdown] = useState<ProjectBreakdown | null>(null)

  // Pull whole-project figures + git status when a project opens and on a slow
  // interval thereafter (dashboard/get is comparatively heavy). git/status
  // returns null outside a repository, which hides the git indicator.
  useEffect(() => {
    if (!isLoaded) {
      setOverview(null)
      setGit(undefined)
      setOverviewOpen(false)
      setBreakdown(null)
      return
    }
    let active = true
    const load = (): void => {
      void rpc
        .request<ProjectOverview>('dashboard/get', [1])
        .then((d) => {
          if (active) setOverview(d)
        })
        .catch(() => {})
      void rpc
        .request<GitIndicator | null>('git/status')
        .then((g) => {
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
  }, [isLoaded, projectPath])

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

  const toggleOverview = (): void => {
    const next = !overviewOpen
    setOverviewOpen(next)
    if (next) {
      void rpc
        .request<ProjectOverview>('dashboard/get', [1])
        .then(setOverview)
        .catch(() => {})
      void rpc
        .request<ProjectBreakdown>('dashboard/overview')
        .then(setBreakdown)
        .catch(() => {})
    }
  }

  const maxChapterWords =
    breakdown && breakdown.chapters.length > 0
      ? Math.max(1, ...breakdown.chapters.map((c) => c.words))
      : 1

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
                  {overview.dailyGoalTarget > 0 && (
                    <span className="status-goal" title={`${overview.dailyGoalPercent}%`}>
                      <span className="status-goal-label">
                        {t('statusBar.dailyGoalShort', {
                          current: overview.dailyGoalCurrent.toLocaleString(),
                          target: overview.dailyGoalTarget.toLocaleString()
                        })}
                      </span>
                      <span className="status-goal-track">
                        <span
                          className="status-goal-fill"
                          style={{ width: `${overview.dailyGoalPercent}%` }}
                        />
                      </span>
                    </span>
                  )}
                  {overview.projectGoalTarget > 0 && (
                    <span className="status-goal" title={`${overview.projectGoalPercent}%`}>
                      <span className="status-goal-label">
                        {t('statusBar.projectGoalShort', {
                          current: overview.totalWords.toLocaleString(),
                          target: overview.projectGoalTarget.toLocaleString()
                        })}
                      </span>
                      <span className="status-goal-track">
                        <span
                          className="status-goal-fill"
                          style={{ width: `${overview.projectGoalPercent}%` }}
                        />
                      </span>
                    </span>
                  )}
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
