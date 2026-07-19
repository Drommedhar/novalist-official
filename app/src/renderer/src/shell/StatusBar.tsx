import { useMemo } from 'react'
import { useTranslation } from 'react-i18next'
import { useShellStore } from '../stores/shellStore'
import { useProjectStore } from '../stores/projectStore'
import { useSettingsStore } from '../stores/settingsStore'
import './statusbar.css'

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

const LEVEL_COLOR: Record<Level, string> = {
  veryEasy: '#16A34A',
  easy: '#22863A',
  moderate: '#B08800',
  difficult: '#C05621',
  veryDifficult: '#B91C1C'
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
  const { t } = useTranslation()
  const backendVersion = useShellStore((s) => s.backendVersion)
  const isLoaded = useProjectStore((s) => s.isLoaded)
  const chapters = useProjectStore((s) => s.chapters)
  const plainText = useProjectStore((s) => s.openScenePlainText)
  const language = useSettingsStore((s) => s.view?.effective.autoReplacementLanguage ?? 'en')
  const openScene = useProjectStore((s) =>
    s.chapters
      .find((c) => c.guid === s.openChapterGuid)
      ?.scenes.find((sc) => sc.id === s.openSceneId)
  )

  const totalWords = chapters.reduce(
    (sum, c) => sum + c.scenes.reduce((s2, sc) => s2 + sc.wordCount, 0),
    0
  )
  const sceneCount = chapters.reduce((sum, c) => sum + c.scenes.length, 0)

  const stats = useMemo(
    () => (plainText === null ? null : computeStats(plainText, language)),
    [plainText, language]
  )

  return (
    <footer className="status-bar">
      <span className="status-left">
        {openScene && (
          <span className="status-stats">
            <span>
              {openScene.wordCount.toLocaleString()} {t('shell.words')}
            </span>
            {stats && (
              <>
                <span>{t('statusBar.characters', { value: stats.characterCount })}</span>
                <span>
                  {t('statusBar.charactersNoSpaces', { value: stats.characterCountNoSpaces })}
                </span>
                {stats.readingTimeMinutes > 0 && (
                  <span>{t('statusBar.readingTime', { minutes: stats.readingTimeMinutes })}</span>
                )}
                {stats.readabilityScore > 0 && (
                  <span
                    className="status-readability"
                    style={{ color: LEVEL_COLOR[stats.readabilityLevel] }}
                    title={t(`statusBar.readabilityLevel.${stats.readabilityLevel}`)}
                  >
                    {t('statusBar.readability', { score: stats.readabilityScore })} -{' '}
                    {t(`statusBar.readabilityLevel.${stats.readabilityLevel}`)}
                  </span>
                )}
              </>
            )}
            <span className="status-scene-title">{openScene.title}</span>
          </span>
        )}
      </span>
      <span className="status-center">
        {isLoaded &&
          `${totalWords.toLocaleString()} ${t('shell.words')} - ${chapters.length} ${t('shell.chapters')} - ${sceneCount} ${t('shell.scenes')}`}
      </span>
      <span className="status-backend">
        {backendVersion
          ? t('shell.backendConnected', { version: backendVersion })
          : t('shell.backendConnecting')}
      </span>
    </footer>
  )
}
