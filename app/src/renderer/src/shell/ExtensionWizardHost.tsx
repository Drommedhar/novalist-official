import { useCallback, useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { rpc } from '../rpc/client'
import {
  useHostBridgeStore,
  type WizardAnswer,
  type WizardChoice,
  type WizardResultDto,
  type WizardStepDef
} from '../stores/hostBridgeStore'
import './hostBridge.css'

/** Evaluates a step's VisibleWhen condition against the current answers. */
function isVisible(step: WizardStepDef, answers: Record<string, WizardAnswer>): boolean {
  const cond = step.visibleWhen
  if (!cond) return true
  const answer = answers[cond.stepId]
  const text = answer?.text ?? ''
  switch (cond.operator) {
    case 'equals':
      return text === (cond.value ?? '')
    case 'notEquals':
      return text !== (cond.value ?? '')
    case 'contains':
      return text.includes(cond.value ?? '')
    case 'present':
      return text.trim().length > 0
    default:
      return true
  }
}

/**
 * Runs an extension-contributed wizard (IHostServices.RunWizardAsync) — the AI
 * Assistant's first-run setup, for instance. One question per screen, with
 * conditional visibility, host-provided dynamic choices, and host-side step
 * validation round-tripped over RPC. The collected result is sent back to the
 * host via ui/wizard/complete (null on cancel).
 */
export function ExtensionWizardHost(): React.JSX.Element | null {
  const { t } = useTranslation()
  const wizard = useHostBridgeStore((s) => s.wizard)
  const closeWizard = useHostBridgeStore((s) => s.closeWizard)

  const steps = wizard?.definition.steps ?? []
  const [answers, setAnswers] = useState<Record<string, WizardAnswer>>({})
  const [index, setIndex] = useState(0)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [dynChoices, setDynChoices] = useState<Record<string, WizardChoice[]>>({})
  const [loadingChoices, setLoadingChoices] = useState(false)

  // Seed answers + land on the first visible step whenever a wizard opens.
  useEffect(() => {
    if (!wizard) return
    const seeded = wizard.seed?.answers ?? {}
    setAnswers(seeded)
    setDynChoices({})
    setError(null)
    const firstVisible = wizard.definition.steps.findIndex((s) => isVisible(s, seeded))
    setIndex(firstVisible < 0 ? 0 : firstVisible)
  }, [wizard])

  const nextVisible = useCallback(
    (from: number): number => {
      for (let j = from + 1; j < steps.length; j++) if (isVisible(steps[j], answers)) return j
      return -1
    },
    [steps, answers]
  )

  const step = steps[index]

  const buildResult = useCallback(
    (completed: boolean): WizardResultDto => ({
      definitionId: wizard?.definition.id ?? '',
      answers,
      currentStepIndex: index,
      completed
    }),
    [wizard, answers, index]
  )

  // Fetch host-provided dynamic choices when entering a choice step that needs
  // them; auto-skip the step if the host returns none and it opts in.
  useEffect(() => {
    if (!wizard || !step || step.kind !== 'choice' || !step.hasDynamicChoices) return
    if (dynChoices[step.id]) return
    let cancelled = false
    setLoadingChoices(true)
    void rpc
      .request<WizardChoice[]>('ui/wizard/choices', [wizard.token, step.id, buildResult(false)])
      .then((choices) => {
        if (cancelled) return
        setDynChoices((prev) => ({ ...prev, [step.id]: choices }))
        if (choices.length === 0 && step.autoSkipIfChoicesEmpty) {
          const next = nextVisible(index)
          if (next >= 0) setIndex(next)
        }
      })
      .catch(() => {
        if (!cancelled) setDynChoices((prev) => ({ ...prev, [step.id]: [] }))
      })
      .finally(() => {
        if (!cancelled) setLoadingChoices(false)
      })
    return () => {
      cancelled = true
    }
  }, [wizard, step, index, dynChoices, buildResult, nextVisible])

  if (!wizard || !step) return null

  const setText = (value: string): void =>
    setAnswers((prev) => ({ ...prev, [step.id]: { text: value } }))
  const setNumber = (value: number): void =>
    setAnswers((prev) => ({ ...prev, [step.id]: { number: value } }))
  const toggleMulti = (value: string): void =>
    setAnswers((prev) => {
      const current = prev[step.id]?.multi ?? []
      const multi = current.includes(value)
        ? current.filter((v) => v !== value)
        : [...current, value]
      return { ...prev, [step.id]: { multi } }
    })

  const textValue = answers[step.id]?.text ?? ''
  const numberValue = answers[step.id]?.number ?? step.defaultNumber
  const multiValue = answers[step.id]?.multi ?? []
  const choices = step.hasDynamicChoices ? (dynChoices[step.id] ?? []) : (step.choices ?? [])
  const isLast = nextVisible(index) === -1
  const hasValue =
    step.kind === 'number'
      ? true
      : step.kind === 'choice'
        ? step.multiSelect
          ? multiValue.length > 0
          : textValue.length > 0
        : textValue.trim().length > 0
  const canAdvance = !busy && (step.skippable || hasValue)

  const finish = (result: WizardResultDto): void => {
    rpc.notify('ui/wizard/complete', [wizard.token, result])
    closeWizard()
  }

  const cancel = (): void => {
    rpc.notify('ui/wizard/complete', [wizard.token, null])
    closeWizard()
  }

  const advance = async (skip: boolean): Promise<void> => {
    if (!skip && step.hasValidator) {
      setBusy(true)
      try {
        const message = await rpc.request<string | null>('ui/wizard/validate', [
          wizard.token,
          step.id,
          buildResult(false)
        ])
        if (message) {
          setError(message)
          return
        }
      } finally {
        setBusy(false)
      }
    }
    setError(null)
    if (skip) setAnswers((prev) => ({ ...prev, [step.id]: {} }))
    const next = nextVisible(index)
    if (next < 0) finish({ ...buildResult(true) })
    else setIndex(next)
  }

  const visibleSteps = steps.filter((s) => isVisible(s, answers))
  const position = visibleSteps.indexOf(step) + 1

  return (
    <div className="dialog-overlay" role="presentation">
      <div className="dialog-card wizard-host-card" role="dialog" aria-label={wizard.definition.displayName}>
        <div className="wizard-host-title-row">
          <div className="dialog-title">{wizard.definition.displayName}</div>
          <span className="wizard-host-progress">
            {position}/{visibleSteps.length}
          </span>
        </div>
        <label className="wizard-host-label">{step.title}</label>
        {step.help && <p className="wizard-host-help">{step.help}</p>}

        {step.kind === 'choice' ? (
          <div className="wizard-host-choices" role="group" aria-label={step.title}>
            {loadingChoices && <p className="wizard-host-hint">{t('hostBridge.wizardLoading')}</p>}
            {!loadingChoices &&
              choices.map((choice) => (
                <label key={choice.value} className="wizard-host-choice">
                  <input
                    type={step.multiSelect ? 'checkbox' : 'radio'}
                    name={`wizard-${step.id}`}
                    checked={step.multiSelect ? multiValue.includes(choice.value) : textValue === choice.value}
                    onChange={() => (step.multiSelect ? toggleMulti(choice.value) : setText(choice.value))}
                  />
                  <span>{choice.label}</span>
                  {choice.description && <span className="wizard-host-choice-desc">{choice.description}</span>}
                </label>
              ))}
            {!loadingChoices && choices.length === 0 && (
              <p className="wizard-host-hint">{t('hostBridge.wizardNoChoices')}</p>
            )}
          </div>
        ) : step.kind === 'number' ? (
          <input
            className="dialog-input"
            type="number"
            min={step.min ?? undefined}
            max={step.max ?? undefined}
            autoFocus
            value={numberValue}
            onChange={(e) => setNumber(Number(e.target.value))}
          />
        ) : step.multiline ? (
          <textarea
            className="dialog-input wizard-host-textarea"
            rows={5}
            autoFocus
            maxLength={step.maxLength ?? undefined}
            placeholder={step.placeholder ?? undefined}
            value={textValue}
            onChange={(e) => setText(e.target.value)}
          />
        ) : (
          <input
            className="dialog-input"
            type={step.kind === 'date' ? 'date' : 'text'}
            autoFocus
            maxLength={step.maxLength ?? undefined}
            placeholder={step.placeholder ?? undefined}
            value={textValue}
            onChange={(e) => setText(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === 'Enter' && canAdvance) void advance(false)
              if (e.key === 'Escape') cancel()
            }}
          />
        )}

        {error && <p className="wizard-host-error">{error}</p>}

        <div className="dialog-actions">
          <button className="dialog-button" onClick={cancel}>
            {t('wizard.cancel')}
          </button>
          <div className="wizard-host-spacer" />
          {step.skippable && !isLast && (
            <button className="dialog-button" disabled={busy} onClick={() => void advance(true)}>
              {t('wizard.skip')}
            </button>
          )}
          <button className="dialog-button primary" disabled={!canAdvance} onClick={() => void advance(false)}>
            {isLast ? t('wizard.finish') : t('wizard.next')}
          </button>
        </div>
      </div>
    </div>
  )
}
