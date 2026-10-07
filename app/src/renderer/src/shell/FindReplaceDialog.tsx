import { useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { rpc } from '../rpc/client'
import { useProjectStore } from '../stores/projectStore'
import { useDialogAutoFocus, useDialogKeyboard } from './useDialogKeyboard'

interface FindMatchDto {
  bookId: string | null
  bookTitle: string | null
  chapterGuid: string
  chapterTitle: string
  sceneId: string
  sceneTitle: string
  before: string
  matchedText: string
  after: string
  /** prose | synopsis | notes | comment | codex - where the hit was found. */
  field: string
}

const SCOPES = ['CurrentScene', 'CurrentChapter', 'ActiveBook', 'Project']

async function navigateToMatch(match: FindMatchDto): Promise<void> {
  const state = useProjectStore.getState()
  if (match.bookId && match.bookId !== state.activeBookId) await state.switchBook(match.bookId)
  if (match.bookId && useProjectStore.getState().activeBookId !== match.bookId) return
  await useProjectStore.getState().openScene(match.chapterGuid, match.sceneId)
}

function useFindReplace(onClose: () => void) {
  const { t } = useTranslation()
  const inputRef = useRef<HTMLInputElement>(null)
  useDialogAutoFocus(inputRef)
  const keyboard = useDialogKeyboard(onClose)
  const openChapterGuid = useProjectStore((s) => s.openChapterGuid)
  const openSceneId = useProjectStore((s) => s.openSceneId)
  const [pattern, setPattern] = useState('')
  const [replacement, setReplacement] = useState('')
  const [matchCase, setMatchCase] = useState(false)
  const [wholeWord, setWholeWord] = useState(false)
  const [useRegex, setUseRegex] = useState(false)
  const [scope, setScope] = useState('ActiveBook')
  const [includeNotes, setIncludeNotes] = useState(false)
  const [includeCodex, setIncludeCodex] = useState(false)
  const [matches, setMatches] = useState<FindMatchDto[] | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const requestId = useRef(0)
  useEffect(() => { setMatches(null); setError(null); requestId.current++ }, [pattern, matchCase, wholeWord, useRegex, scope, includeNotes, includeCodex])
  const [replacedCount, setReplacedCount] = useState<number | null>(null)

  const args = (): unknown[] => [pattern, matchCase, wholeWord, useRegex, scope, openChapterGuid, openSceneId, includeNotes, includeCodex]

  const find = async (): Promise<void> => {
    if (!pattern || busy) return
    const request = ++requestId.current
    setError(null)
    setMatches(null)
    setBusy(true)
    setReplacedCount(null)
    try {
      const result = await rpc.request<FindMatchDto[]>('search/find', args())
      if (request === requestId.current) setMatches(result)
    } catch (reason) {
      if (request === requestId.current) setError(t('findReplace.failed', { message: String(reason) }))
    } finally {
      setBusy(false)
    }
  }

  const replaceAll = async (): Promise<void> => {
    if (!pattern || busy) return
    const request = ++requestId.current
    setError(null)
    setMatches(null)
    setBusy(true)
    try {
      const count = await rpc.request<number>('search/replaceAll', [
        pattern,
        replacement,
        matchCase,
        wholeWord,
        useRegex,
        scope,
        openChapterGuid,
        openSceneId,
        includeNotes
      ])
      if (request === requestId.current) setReplacedCount(count)
      setMatches(null)
      const state = useProjectStore.getState()
      if (state.openChapterGuid && state.openSceneId) {
        await state.openScene(state.openChapterGuid, state.openSceneId)
      }
    } catch (reason) {
      if (request === requestId.current) setError(t('findReplace.failed', { message: String(reason) }))
    } finally {
      setBusy(false)
    }
  }

  const openMatch = async (match: FindMatchDto): Promise<void> => {
    try {
      await navigateToMatch(match)
      onClose()
    } catch (reason) {
      setError(t('findReplace.failed', { message: String(reason) }))
    }
  }

  return { t, inputRef, keyboard, pattern, setPattern, replacement, setReplacement, matchCase, setMatchCase, wholeWord, setWholeWord, useRegex, setUseRegex, scope, setScope, includeNotes, setIncludeNotes, includeCodex, setIncludeCodex, matches, busy, error, replacedCount, find, replaceAll, openMatch }
}

export function FindReplaceDialog({ onClose }: { onClose(): void }): React.JSX.Element {
  const { t, inputRef, keyboard, pattern, setPattern, replacement, setReplacement, matchCase, setMatchCase, wholeWord, setWholeWord, useRegex, setUseRegex, scope, setScope, includeNotes, setIncludeNotes, includeCodex, setIncludeCodex, matches, busy, error, replacedCount, find, replaceAll, openMatch } = useFindReplace(onClose)

  return (
    <div className="dialog-overlay" onPointerDown={(e) => e.target === e.currentTarget && onClose()}>
      <div {...keyboard} className="dialog-card findreplace-card" role="dialog" aria-modal="true" aria-label={t('findReplace.title')}>
        <div className="dialog-title">{t('findReplace.title')}</div>
        <input
          className="dialog-input"
          placeholder={t('findReplace.find')}
          value={pattern}
          ref={inputRef}
          onChange={(e) => setPattern(e.target.value)}
          onKeyDown={(e) => {
            if (e.key === 'Enter') void find()
          }}
        />
        <input
          className="dialog-input"
          placeholder={t('findReplace.replace')}
          value={replacement}
          onChange={(e) => setReplacement(e.target.value)}
        />
        <div className="findreplace-options">
          <label className="relationships-toggle">
            <input type="checkbox" checked={matchCase} onChange={(e) => setMatchCase(e.target.checked)} />
            {t('findReplace.matchCase')}
          </label>
          <label className="relationships-toggle">
            <input type="checkbox" checked={wholeWord} onChange={(e) => setWholeWord(e.target.checked)} />
            {t('findReplace.wholeWord')}
          </label>
          <label className="relationships-toggle">
            <input type="checkbox" checked={useRegex} onChange={(e) => setUseRegex(e.target.checked)} />
            {t('findReplace.regex')}
          </label>
          <label className="relationships-toggle">
            <input
              type="checkbox"
              checked={includeNotes}
              onChange={(e) => setIncludeNotes(e.target.checked)}
            />
            {t('findReplace.includeNotes')}
          </label>
          <label className="relationships-toggle">
            <input
              type="checkbox"
              checked={includeCodex}
              onChange={(e) => setIncludeCodex(e.target.checked)}
            />
            {t('findReplace.includeCodex')}
          </label>
          <select className="dialog-input findreplace-scope" value={scope} onChange={(e) => setScope(e.target.value)}>
            {SCOPES.map((s) => (
              <option key={s} value={s}>
                {t(`findReplace.scope${s}`)}
              </option>
            ))}
          </select>
        </div>
        <div className="dialog-actions">
          <button className="dialog-button" disabled={busy} onClick={() => void find()}>
            {t('findReplace.find')}
          </button>
          <button className="dialog-button primary" disabled={busy} onClick={() => void replaceAll()}>
            {t('findReplace.replaceAll')}
          </button>
        </div>
        {error && <p role="alert">{error}</p>}
        {replacedCount !== null && (
          <p className="inspector-meta">{t('findReplace.replacedCount', { count: replacedCount })}</p>
        )}
        {matches && (
          <div className="findreplace-results">
            {matches.length === 0 && <p className="codex-empty">{t('findReplace.noMatches')}</p>}
            {matches.map((match, index) => (
              <button
                key={`${match.sceneId}-${match.field}-${index}`}
                className="findreplace-result"
                // A Codex hit has no scene behind it, so it reports and stays put.
                disabled={!match.chapterGuid}
                onClick={() => void openMatch(match)}
              >
                <span className="codex-row-detail">
                  {match.bookTitle ? `${match.bookTitle} / ` : ''}{match.chapterTitle} - {match.sceneTitle}
                  {match.field !== 'prose' && (
                    <span className="findreplace-field">{t(`findReplace.field_${match.field}`)}</span>
                  )}
                </span>
                <span className="findreplace-snippet">
                  {match.before}
                  <mark>{match.matchedText}</mark>
                  {match.after}
                </span>
              </button>
            ))}
          </div>
        )}
      </div>
    </div>
  )
}
