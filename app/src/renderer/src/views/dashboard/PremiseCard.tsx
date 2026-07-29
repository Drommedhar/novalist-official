import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { rpc } from '../../rpc/client'

interface PremiseAct {
  act: string
  summary: string
}

interface Premise {
  logline: string
  paragraph: string
  acts: PremiseAct[]
}

/**
 * The book in one line, then one paragraph, then one summary per act.
 *
 * Novalist has carried a Snowflake-shaped setup wizard in its code for a long
 * time with nothing calling it and nowhere for its answers to go. This is where
 * they live: beside the goals, because a premise is the thing the daily word
 * count is in service of.
 */
export function PremiseCard(): React.JSX.Element {
  const { t } = useTranslation()
  const [premise, setPremise] = useState<Premise | null>(null)

  useEffect(() => {
    void rpc.request<Premise>('premise/get').then(setPremise).catch(() => setPremise(null))
  }, [])

  if (!premise) return <></>

  const save = (next: Premise): void => {
    setPremise(next)
    void rpc
      .request<Premise>('premise/save', [next.logline, next.paragraph, next.acts])
      .then(setPremise)
  }

  return (
    <div className="dashboard-card">
      <div className="dashboard-card-title">{t('premise.title')}</div>
      <div className="dashboard-echo-desc">{t('premise.intro')}</div>

      <label className="inspector-label" htmlFor="premise-logline">
        {t('premise.logline')}
      </label>
      <input
        id="premise-logline"
        className="dialog-input"
        placeholder={t('premise.loglinePlaceholder')}
        defaultValue={premise.logline}
        onBlur={(e) => save({ ...premise, logline: e.target.value })}
      />

      <label className="inspector-label" htmlFor="premise-paragraph">
        {t('premise.paragraph')}
      </label>
      <textarea
        id="premise-paragraph"
        className="dialog-input premise-paragraph"
        placeholder={t('premise.paragraphPlaceholder')}
        defaultValue={premise.paragraph}
        onBlur={(e) => save({ ...premise, paragraph: e.target.value })}
      />

      {/* One box per act the book actually has, so a two-act or five-act
          structure is not asked to pretend it has three. */}
      {premise.acts.map((act, index) => (
        <div key={act.act}>
          <label className="inspector-label">{act.act}</label>
          <textarea
            className="dialog-input premise-paragraph"
            aria-label={act.act}
            defaultValue={act.summary}
            onBlur={(e) =>
              save({
                ...premise,
                acts: premise.acts.map((a, i) =>
                  i === index ? { ...a, summary: e.target.value } : a
                )
              })
            }
          />
        </div>
      ))}
      {premise.acts.length === 0 && (
        <div className="settings-hint">{t('premise.noActs')}</div>
      )}
    </div>
  )
}
