import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { rpc } from '../../rpc/client'
import type { WizardStepDef } from './wizards'

/**
 * Step-at-a-time wizard runner: one question per screen with help text,
 * back/skip/next navigation and a finish action returning the answer map.
 */
export function WizardDialog({
  title,
  steps,
  onFinish,
  onClose
}: {
  title: string
  steps: WizardStepDef[]
  onFinish: (answers: Record<string, string>) => void
  onClose: () => void
}): React.JSX.Element {
  const { t } = useTranslation()
  const [index, setIndex] = useState(0)
  const [answers, setAnswers] = useState<Record<string, string>>({})
  const [refChoices, setRefChoices] = useState<Record<string, { value: string; label: string }[]>>(
    {}
  )

  const step = steps[index]
  const value = answers[step?.id] ?? step?.defaultValue ?? ''
  const isLast = index === steps.length - 1
  const canAdvance = step?.skippable || value.trim().length > 0

  useEffect(() => {
    if (!step?.entityRefType || refChoices[step.id]) return
    void rpc
      .request<{ name: string }[]>('entities/list', [step.entityRefType])
      .then((entities) =>
        setRefChoices((prev) => ({
          ...prev,
          [step.id]: entities.map((e) => ({ value: e.name, label: e.name }))
        }))
      )
      .catch(() => setRefChoices((prev) => ({ ...prev, [step.id]: [] })))
  }, [step, refChoices])

  if (!step) return <></>

  const setValue = (v: string): void => setAnswers({ ...answers, [step.id]: v })
  const advance = (skip: boolean): void => {
    const next = { ...answers, [step.id]: skip ? '' : value }
    setAnswers(next)
    if (isLast) onFinish(next)
    else setIndex(index + 1)
  }
  const choices = step.entityRefType ? (refChoices[step.id] ?? []) : (step.choices ?? [])

  return (
    <div className="dialog-overlay">
      <div className="dialog-card type-manager-card" role="dialog" aria-label={title}>
        <div className="type-manager-title-row">
          <div className="dialog-title">{title}</div>
          <span className="codex-row-detail">
            {index + 1}/{steps.length}
          </span>
        </div>
        <label className="inspector-label">{step.title}</label>
        {step.help && <p className="wizard-help">{step.help}</p>}
        {step.kind === 'choice' ? (
          <div className="wizard-choices" role="radiogroup" aria-label={step.title}>
            {choices.map((choice) => (
              <label key={choice.value} className="type-manager-check">
                <input
                  type="radio"
                  name={`wizard-${step.id}`}
                  checked={value === choice.value}
                  onChange={() => setValue(choice.value)}
                />
                {choice.label}
              </label>
            ))}
            {choices.length === 0 && <p className="codex-empty">{t('codexHub.emptyHint')}</p>}
          </div>
        ) : step.multiline ? (
          <textarea
            className="inspector-textarea"
            rows={5}
            autoFocus
            value={value}
            onChange={(e) => setValue(e.target.value)}
          />
        ) : (
          <input
            className="dialog-input"
            type={step.kind === 'number' ? 'number' : step.kind === 'date' ? 'date' : 'text'}
            autoFocus
            value={value}
            onChange={(e) => setValue(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === 'Enter' && canAdvance) advance(false)
              if (e.key === 'Escape') onClose()
            }}
          />
        )}
        <div className="dialog-actions">
          <button className="dialog-button" onClick={onClose}>
            {t('wizard.cancel')}
          </button>
          <div className="toolbar-spacer" />
          {index > 0 && (
            <button className="dialog-button" onClick={() => setIndex(index - 1)}>
              {t('wizard.back')}
            </button>
          )}
          {step.skippable && !isLast && (
            <button className="dialog-button" onClick={() => advance(true)}>
              {t('wizard.skip')}
            </button>
          )}
          <button
            className="dialog-button primary"
            disabled={!canAdvance}
            onClick={() => advance(false)}
          >
            {isLast ? t('wizard.finish') : t('wizard.next')}
          </button>
        </div>
      </div>
    </div>
  )
}
