import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { rpc } from '../rpc/client'
import { useProjectStore, type ProjectStateDto } from '../stores/projectStore'

interface PremiseAct {
  act: string
  summary: string
}

/**
 * The Snowflake ladder, asked once at the start of a project.
 *
 * A wizard of exactly this shape has sat in Novalist's code for a long time
 * with nothing calling it: the steps, the help text and its translations all
 * shipped, and no way to reach any of it. This is the reachable version, and
 * the answers land in the book's premise rather than evaporating.
 */
export function SnowflakeSetup({ onClose }: { onClose: () => void }): React.JSX.Element {
  const { t } = useTranslation()
  const [logline, setLogline] = useState('')
  const [paragraph, setParagraph] = useState('')
  const [acts, setActs] = useState<PremiseAct[]>([
    { act: t('premise.actOne'), summary: '' },
    { act: t('premise.actTwo'), summary: '' },
    { act: t('premise.actThree'), summary: '' }
  ])
  const [chaptersPerAct, setChaptersPerAct] = useState(7)
  const [scaffold, setScaffold] = useState(true)
  const [busy, setBusy] = useState(false)

  const finish = async (): Promise<void> => {
    setBusy(true)
    try {
      await rpc.request('premise/save', [logline, paragraph, acts])
      // Placeholder chapters are opt-in: a writer who came here for the
      // premise alone should not have to delete twenty-one empty chapters.
      if (scaffold) {
        await rpc.request('premise/scaffold', [
          acts.filter((a) => a.act.trim().length > 0),
          chaptersPerAct
        ])
        useProjectStore.getState().applyState(
          await rpc.request<ProjectStateDto>('project/getState')
        )
      }
      onClose()
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="dialog-overlay" onPointerDown={(e) => e.target === e.currentTarget && onClose()}>
      <div className="dialog-card snowflake-card" role="dialog" aria-label={t('premise.setupTitle')}>
        <div className="dialog-title">{t('premise.setupTitle')}</div>
        <div className="settings-hint">{t('premise.setupIntro')}</div>

        <label className="inspector-label" htmlFor="sf-logline">
          {t('premise.logline')}
        </label>
        <input
          id="sf-logline"
          className="dialog-input"
          autoFocus
          placeholder={t('premise.loglinePlaceholder')}
          value={logline}
          onChange={(e) => setLogline(e.target.value)}
        />

        <label className="inspector-label" htmlFor="sf-paragraph">
          {t('premise.paragraph')}
        </label>
        <textarea
          id="sf-paragraph"
          className="dialog-input premise-paragraph"
          placeholder={t('premise.paragraphPlaceholder')}
          value={paragraph}
          onChange={(e) => setParagraph(e.target.value)}
        />

        {acts.map((act, index) => (
          <div key={index}>
            <div className="snowflake-act-row">
              <input
                className="dialog-input"
                aria-label={t('premise.actName')}
                value={act.act}
                onChange={(e) =>
                  setActs(acts.map((a, i) => (i === index ? { ...a, act: e.target.value } : a)))
                }
              />
            </div>
            <textarea
              className="dialog-input premise-paragraph"
              aria-label={`${act.act} ${t('premise.summary')}`}
              placeholder={t(`premise.actHelp${index === 0 ? 'One' : index === 1 ? 'Two' : 'Three'}`)}
              value={act.summary}
              onChange={(e) =>
                setActs(acts.map((a, i) => (i === index ? { ...a, summary: e.target.value } : a)))
              }
            />
          </div>
        ))}

        <label className="relationships-toggle">
          <input
            type="checkbox"
            checked={scaffold}
            onChange={(e) => setScaffold(e.target.checked)}
          />
          {t('premise.scaffold')}
        </label>
        {scaffold && (
          <>
            <label className="inspector-label" htmlFor="sf-chapters">
              {t('premise.chaptersPerAct')}
            </label>
            <input
              id="sf-chapters"
              className="dialog-input"
              type="number"
              min={1}
              max={30}
              value={chaptersPerAct}
              onChange={(e) => setChaptersPerAct(Number(e.target.value) || 1)}
            />
          </>
        )}

        <div className="dialog-actions">
          <button className="dialog-button" disabled={busy} onClick={onClose}>
            {t('premise.skip')}
          </button>
          <button className="dialog-button primary" disabled={busy} onClick={() => void finish()}>
            {t('dialog.ok')}
          </button>
        </div>
      </div>
    </div>
  )
}
