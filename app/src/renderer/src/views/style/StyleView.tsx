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
  findings: StyleFinding[]
}

type Scope = 'book' | 'chapter' | 'scene'

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
  const [report, setReport] = useState<StyleReport | null>(null)
  const [busy, setBusy] = useState(false)
  const [expanded, setExpanded] = useState<string | null>(null)

  const run = useCallback(async () => {
    setBusy(true)
    try {
      if (scope === 'scene' && openChapterGuid && openSceneId) {
        setReport(await rpc.request<StyleReport>('style/scene', [openChapterGuid, openSceneId]))
      } else if (scope === 'chapter' && openChapterGuid) {
        setReport(await rpc.request<StyleReport>('style/book', [openChapterGuid]))
      } else {
        setReport(await rpc.request<StyleReport>('style/book', [null]))
      }
    } finally {
      setBusy(false)
    }
  }, [scope, openChapterGuid, openSceneId])

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
