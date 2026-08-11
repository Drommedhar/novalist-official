import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { rpc } from '../rpc/client'
import { useProjectStore } from '../stores/projectStore'
import { useSettingsStore } from '../stores/settingsStore'

interface CleanupReportDto {
  scenesConsidered: number
  scenesChanged: number
  changedTitles: string[]
}

/**
 * The rules, in the order they are offered.
 *
 * Every one starts on. A writer opening a cleanup pass wants the manuscript
 * cleaned up, and a dialog that does nothing until six boxes are ticked is a
 * dialog that gets closed again.
 */
const RULES = [
  'SmartenQuotes',
  'Typography',
  'CollapseSpaces',
  'TrimParagraphs',
  'DropEmptyParagraphs',
  'NormaliseSceneBreaks'
] as const

/**
 * The two rules that are auto-replacement applied to prose already written.
 *
 * When the writer has switched auto-replacement off, running these would put
 * back over the whole book exactly what the switch is there to prevent - so
 * they are offered greyed out, not silently dropped, and the backend refuses
 * them too.
 */
const SUBSTITUTION_RULES: readonly string[] = ['SmartenQuotes', 'Typography']

/**
 * A cleanup pass over prose that is already written.
 *
 * Auto-replacements fire while typing and skip pasted text on purpose, so a
 * chapter written elsewhere and pasted in keeps its straight quotes, its hyphen
 * pairs and its double spaces for good. Find and Replace can be pointed at each
 * of them one pattern at a time, if the writer knows what to look for.
 */
export function CleanupDialog({ onClose }: { onClose(): void }): React.JSX.Element {
  const { t } = useTranslation()
  const chapters = useProjectStore((s) => s.chapters)
  const openChapterGuid = useProjectStore((s) => s.openChapterGuid)
  const autoReplacementEnabled = useSettingsStore(
    (s) => s.view?.effective.autoReplacementEnabled ?? true
  )
  const offered = RULES.filter((r) => autoReplacementEnabled || !SUBSTITUTION_RULES.includes(r))
  const [rules, setRules] = useState<Set<string>>(new Set(offered))
  const [wholeBook, setWholeBook] = useState(true)
  const [report, setReport] = useState<CleanupReportDto | null>(null)
  const [ran, setRan] = useState(false)
  const [busy, setBusy] = useState(false)

  const scope = (): string[] | null =>
    wholeBook || !openChapterGuid ? null : [openChapterGuid]

  const call = async (method: string): Promise<void> => {
    if (rules.size === 0) return
    setBusy(true)
    try {
      setReport(await rpc.request<CleanupReportDto>(method, [[...rules], scope()]))
      setRan(method.endsWith('run'))
    } finally {
      setBusy(false)
    }
  }

  const toggle = (rule: string, on: boolean): void =>
    setRules((prev) => {
      const next = new Set(prev)
      if (on) next.add(rule)
      else next.delete(rule)
      return next
    })

  const openChapter = chapters.find((c) => c.guid === openChapterGuid)

  return (
    <div className="dialog-overlay" onPointerDown={(e) => e.target === e.currentTarget && onClose()}>
      <div className="dialog-card cleanup-card" role="dialog" aria-label={t('cleanup.title')}>
        <div className="dialog-title">{t('cleanup.title')}</div>
        <p className="inspector-meta">{t('cleanup.intro')}</p>

        <div className="cleanup-rules">
          {RULES.map((rule) => {
            const suppressed = !offered.includes(rule)
            return (
              <label
                key={rule}
                className={`relationships-toggle${suppressed ? ' is-unavailable' : ''}`}
              >
                <input
                  type="checkbox"
                  disabled={suppressed}
                  checked={rules.has(rule)}
                  onChange={(e) => {
                    toggle(rule, e.target.checked)
                    // The old count described a different pass, and a stale
                    // number beside a changed choice is worse than none.
                    setReport(null)
                  }}
                />
                {t(`cleanup.rule${rule}`)}
              </label>
            )
          })}
        </div>
        {!autoReplacementEnabled && (
          <p className="inspector-meta">{t('cleanup.autoReplacementOff')}</p>
        )}

        {openChapter && (
          <label className="relationships-toggle">
            <input
              type="checkbox"
              checked={wholeBook}
              onChange={(e) => {
                setWholeBook(e.target.checked)
                setReport(null)
              }}
            />
            {t('cleanup.wholeBook', { chapter: openChapter.title })}
          </label>
        )}

        <div className="dialog-actions">
          {/* Preview first, and it is the plain button: a pass that rewrites
              every scene in a book is not something to find out about after. */}
          <button
            className="dialog-button"
            disabled={busy || rules.size === 0}
            onClick={() => void call('cleanup/preview')}
          >
            {t('cleanup.preview')}
          </button>
          <button
            className="dialog-button primary"
            disabled={busy || rules.size === 0}
            onClick={() => void call('cleanup/run')}
          >
            {t('cleanup.run')}
          </button>
        </div>

        {report && (
          <div className="cleanup-report">
            <p className="inspector-meta">
              {ran
                ? t('cleanup.ranCount', {
                    changed: report.scenesChanged,
                    total: report.scenesConsidered
                  })
                : t('cleanup.previewCount', {
                    changed: report.scenesChanged,
                    total: report.scenesConsidered
                  })}
            </p>
            {ran && report.scenesChanged > 0 && (
              <p className="inspector-meta">{t('cleanup.snapshotNote')}</p>
            )}
            {report.changedTitles.length > 0 && (
              <ul className="cleanup-titles">
                {report.changedTitles.map((title, index) => (
                  <li key={`${title}-${index}`}>{title}</li>
                ))}
              </ul>
            )}
          </div>
        )}
      </div>
    </div>
  )
}
