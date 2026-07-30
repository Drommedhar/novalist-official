import { useCallback, useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { rpc } from '../../rpc/client'
import { useProjectStore } from '../../stores/projectStore'
import './style.css'

interface StyleHit {
  text: string
  offset: number
  context: string
}

interface StyleFinding {
  key: string
  count: number
  per1000Words: number
  supported: boolean
  examples: StyleHit[]
}

interface StyleReport {
  language: string
  wordCount: number
  sentenceCount: number
  meanSentenceWords: number
  sentenceLengthStdDev: number
  longestSentenceWords: number
  scope: string
  paragraphCount: number
  meanParagraphWords: number
  paragraphLengthStdDev: number
  findings: StyleFinding[]
  /** One row per sense, always all five, always in the same order. */
  senses: StyleFinding[]
}

type Scope = 'book' | 'chapter' | 'scene'

/**
 * Which part of the prose is measured.
 *
 * A character written to speak in cliches is not a writing problem, and a report
 * that counts their dialogue alongside the narration says otherwise - which is
 * the most common complaint about tools of this kind. Novalist has segmented
 * dialogue with high fidelity all along and never used it for this.
 */
type TextScope = 'Everything' | 'ProseOnly' | 'DialogueOnly'
const TEXT_SCOPES: TextScope[] = ['Everything', 'ProseOnly', 'DialogueOnly']

/**
 * Deterministic craft reports. Every number here is computed offline from the
 * text and the writing language's word lists, so the same manuscript always
 * produces the same report. Nothing is sent anywhere and no model is involved.
 */
export function StyleView(): React.JSX.Element {
  const { t } = useTranslation()
  const chapters = useProjectStore((s) => s.chapters)
  const openChapterGuid = useProjectStore((s) => s.openChapterGuid)
  const openSceneId = useProjectStore((s) => s.openSceneId)

  const [scope, setScope] = useState<Scope>('book')
  const [textScope, setTextScope] = useState<TextScope>('Everything')
  const [report, setReport] = useState<StyleReport | null>(null)
  const [busy, setBusy] = useState(false)
  const [expanded, setExpanded] = useState<string | null>(null)

  const run = useCallback(async () => {
    setBusy(true)
    try {
      if (scope === 'scene' && openChapterGuid && openSceneId) {
        setReport(
          await rpc.request<StyleReport>('style/scene', [
            openChapterGuid,
            openSceneId,
            textScope
          ])
        )
      } else if (scope === 'chapter' && openChapterGuid) {
        setReport(await rpc.request<StyleReport>('style/book', [openChapterGuid, textScope]))
      } else {
        setReport(await rpc.request<StyleReport>('style/book', [null, textScope]))
      }
    } finally {
      setBusy(false)
    }
  }, [scope, textScope, openChapterGuid, openSceneId])

  useEffect(() => {
    void run()
  }, [run])

  const scopeDisabled = (s: Scope): boolean =>
    (s === 'scene' && !openSceneId) || (s === 'chapter' && !openChapterGuid)

  return (
    <div className="style-view">
      <div className="style-header">
        <h2>{t('style.title')}</h2>
        <div className="style-scopes">
          {(['book', 'chapter', 'scene'] as Scope[]).map((s) => (
            <button
              key={s}
              className={`dialog-button${scope === s ? ' active' : ''}`}
              disabled={scopeDisabled(s)}
              onClick={() => setScope(s)}
            >
              {t(`style.scope.${s}`)}
            </button>
          ))}
          {/* Narration and dialogue are different writing and read differently.
              Measuring them together is what makes a report tell somebody their
              character speaks badly. */}
          <select
            className="dialog-input style-text-scope"
            aria-label={t('style.textScope')}
            value={textScope}
            onChange={(e) => setTextScope(e.target.value as TextScope)}
          >
            {TEXT_SCOPES.map((ts) => (
              <option key={ts} value={ts}>
                {t(`style.textScope${ts}`)}
              </option>
            ))}
          </select>
          <button className="dialog-button" disabled={busy} onClick={() => void run()}>
            {t('style.refresh')}
          </button>
        </div>
      </div>

      <p className="settings-hint">{t('style.description')}</p>

      {report && (
        <>
          <div className="style-stats">
            <Stat label={t('style.words')} value={report.wordCount.toLocaleString()} />
            <Stat label={t('style.sentences')} value={report.sentenceCount.toLocaleString()} />
            <Stat label={t('style.meanSentence')} value={String(report.meanSentenceWords)} />
            <Stat
              label={t('style.variation')}
              value={String(report.sentenceLengthStdDev)}
              hint={t('style.variationHint')}
            />
            <Stat label={t('style.longest')} value={String(report.longestSentenceWords)} />
            <Stat
              label={t('style.paragraphs')}
              value={report.paragraphCount.toLocaleString()}
            />
            <Stat
              label={t('style.meanParagraph')}
              value={String(report.meanParagraphWords)}
            />
            {/* The well-known one is sentence variation; a chapter of
                identically-sized paragraphs reads as flat for the same reason
                and is just as invisible while writing it. */}
            <Stat
              label={t('style.paragraphVariation')}
              value={String(report.paragraphLengthStdDev)}
              hint={t('style.paragraphVariationHint')}
            />
          </div>

          {/* Not problems, so kept out of the findings list. A count of sight
              words is not something to reduce - the reading is which senses
              the prose forgot, and nearly every writer forgets the same
              three. */}
          <div className="style-senses">
            <div className="inspector-label">{t('style.senses')}</div>
            <div className="settings-hint">{t('style.sensesHint')}</div>
            <div className="style-sense-row">
              {report.senses.map((sense) => (
                <div
                  key={sense.key}
                  className={`style-sense${sense.supported && sense.count === 0 ? ' absent' : ''}`}
                  title={
                    sense.supported
                      ? t('style.per1000', { value: sense.per1000Words })
                      : t('style.unsupported', { language: report.language })
                  }
                >
                  <span className="style-sense-name">{t(`style.sense.${sense.key}`)}</span>
                  <span className="style-sense-count">{sense.supported ? sense.count : '—'}</span>
                </div>
              ))}
            </div>
          </div>

          <div className="style-findings">
            {report.findings.map((f) => (
              <div key={f.key} className="style-finding">
                <button
                  className="style-finding-head"
                  onClick={() => setExpanded(expanded === f.key ? null : f.key)}
                  disabled={!f.supported || f.examples.length === 0}
                >
                  <span className="style-finding-name">{t(`style.report.${f.key}`)}</span>
                  {f.supported ? (
                    <span className="style-finding-count">
                      {f.count}
                      <span className="style-finding-density">
                        {t('style.per1000', { value: f.per1000Words })}
                      </span>
                    </span>
                  ) : (
                    <span className="style-finding-unsupported">
                      {t('style.unsupported', { language: report.language })}
                    </span>
                  )}
                </button>

                {f.supported && <div className="settings-hint">{t(`style.reportDesc.${f.key}`)}</div>}

                {expanded === f.key && (
                  <ul className="style-examples">
                    {f.examples.map((e, i) => (
                      <li key={`${e.offset}-${i}`}>
                        <span className="style-example-hit">{e.text}</span>
                        <span className="style-example-context">{e.context}</span>
                      </li>
                    ))}
                  </ul>
                )}
              </div>
            ))}
          </div>
        </>
      )}

      {!report && !busy && <p className="settings-hint">{t('style.empty')}</p>}
      {chapters.length === 0 && <p className="settings-hint">{t('style.noChapters')}</p>}
    </div>
  )
}

function Stat(props: { label: string; value: string; hint?: string }): React.JSX.Element {
  return (
    <div className="style-stat" title={props.hint}>
      <div className="style-stat-value">{props.value}</div>
      <div className="style-stat-label">{props.label}</div>
    </div>
  )
}
