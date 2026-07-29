import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { rpc } from '../rpc/client'
import { useProjectStore } from '../stores/projectStore'

interface FindMatchDto {
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

export function FindReplaceDialog({ onClose }: { onClose(): void }): React.JSX.Element {
  const { t } = useTranslation()
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
  const [replacedCount, setReplacedCount] = useState<number | null>(null)

  const args = (): unknown[] => [
    pattern,
    matchCase,
    wholeWord,
    useRegex,
    scope,
    openChapterGuid,
    openSceneId,
    includeNotes,
    includeCodex
  ]

  const find = async (): Promise<void> => {
    if (!pattern) return
    setBusy(true)
    setReplacedCount(null)
    try {
      setMatches(await rpc.request<FindMatchDto[]>('search/find', args()))
    } finally {
      setBusy(false)
    }
  }

  const replaceAll = async (): Promise<void> => {
    if (!pattern) return
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
      setReplacedCount(count)
      setMatches(null)
      const state = useProjectStore.getState()
      if (state.openChapterGuid && state.openSceneId) {
        await state.openScene(state.openChapterGuid, state.openSceneId)
      }
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="dialog-overlay" onPointerDown={(e) => e.target === e.currentTarget && onClose()}>
      <div className="dialog-card findreplace-card" role="dialog" aria-label={t('findReplace.title')}>
        <div className="dialog-title">{t('findReplace.title')}</div>
        <input
          className="dialog-input"
          placeholder={t('findReplace.find')}
          value={pattern}
          autoFocus
          onChange={(e) => setPattern(e.target.value)}
          onKeyDown={(e) => {
            if (e.key === 'Enter') void find()
            if (e.key === 'Escape') onClose()
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
                onClick={() => {
                  onClose()
                  void useProjectStore.getState().openScene(match.chapterGuid, match.sceneId)
                }}
              >
                <span className="codex-row-detail">
                  {match.chapterTitle} - {match.sceneTitle}
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
