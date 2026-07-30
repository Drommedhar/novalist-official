import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { rpc } from '../../rpc/client'

interface PremiseAct {
  act: string
  summary: string
}

interface Pitch {
  genre: string
  audience: string
  comparables: string
  setting: string
  blurb: string
  synopsis: string
}

interface Voice {
  narrativePerson: string
  tense: string
}

interface Premise {
  logline: string
  paragraph: string
  acts: PremiseAct[]
  pitch: Pitch
  voice: Voice
}

/** What a book can declare itself to be written in. */
const PERSONS = ['first', 'third-limited', 'third-omniscient']
const TENSES = ['past', 'present']

/** The pitch fields, in the order a query letter asks for them. */
const PITCH_FIELDS: { key: keyof Pitch; long: boolean }[] = [
  { key: 'genre', long: false },
  { key: 'audience', long: false },
  { key: 'comparables', long: false },
  { key: 'setting', long: false },
  { key: 'blurb', long: true },
  { key: 'synopsis', long: true }
]

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

  const savePitch = (pitch: Pitch): void => {
    setPremise({ ...premise, pitch })
    void rpc.request<Premise>('premise/savePitch', [pitch]).then(setPremise)
  }

  const saveVoice = (narrativePerson: string, tense: string): void => {
    setPremise({ ...premise, voice: { narrativePerson, tense } })
    void rpc.request<Premise>('premise/saveVoice', [narrativePerson, tense]).then(setPremise)
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

      {/* What the book is written in. Declared rather than read off the
          majority of scenes, which would make the one that drifted look
          normal. A scene that disagrees is told about in the Inspector. */}
      <div className="dashboard-card-title">{t('premise.voiceTitle')}</div>
      <div className="dashboard-echo-desc">{t('premise.voiceIntro')}</div>
      <div className="premise-voice-row">
        <select
          className="inspector-input"
          aria-label={t('premise.narrativePerson')}
          value={premise.voice.narrativePerson}
          onChange={(e) => saveVoice(e.target.value, premise.voice.tense)}
        >
          <option value="">{t('premise.voiceUnset')}</option>
          {PERSONS.map((p) => (
            <option key={p} value={p}>
              {t(`premise.person_${p.replace('-', '_')}`)}
            </option>
          ))}
        </select>
        <select
          className="inspector-input"
          aria-label={t('premise.tense')}
          value={premise.voice.tense}
          onChange={(e) => saveVoice(premise.voice.narrativePerson, e.target.value)}
        >
          <option value="">{t('premise.voiceUnset')}</option>
          {TENSES.map((tn) => (
            <option key={tn} value={tn}>
              {t(`premise.tense_${tn}`)}
            </option>
          ))}
        </select>
      </div>

      {/* The pitch. Every one of these is asked for by name on a query letter
          or a submission form, and every one of them used to live in a
          document somewhere outside Novalist. */}
      <div className="dashboard-card-title">{t('premise.pitchTitle')}</div>
      <div className="dashboard-echo-desc">{t('premise.pitchIntro')}</div>
      {PITCH_FIELDS.map((field) => (
        <div key={field.key}>
          <label className="inspector-label" htmlFor={`pitch-${field.key}`}>
            {t(`premise.${field.key}`)}
          </label>
          {field.long ? (
            <textarea
              id={`pitch-${field.key}`}
              className="dialog-input premise-paragraph"
              placeholder={t(`premise.${field.key}Placeholder`)}
              defaultValue={premise.pitch[field.key]}
              onBlur={(e) => savePitch({ ...premise.pitch, [field.key]: e.target.value })}
            />
          ) : (
            <input
              id={`pitch-${field.key}`}
              className="dialog-input"
              placeholder={t(`premise.${field.key}Placeholder`)}
              defaultValue={premise.pitch[field.key]}
              onBlur={(e) => savePitch({ ...premise.pitch, [field.key]: e.target.value })}
            />
          )}
        </div>
      ))}
    </div>
  )
}
