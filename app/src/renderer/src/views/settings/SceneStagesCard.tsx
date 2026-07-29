import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Plus, Trash2 } from 'lucide-react'
import { useStageStore, type SceneStage } from '../../stores/stageStore'

/**
 * The revision stages this book's scenes can be at.
 *
 * Chapter statuses are a fixed five compiled into the app. Stages are the
 * writer's own, because no two people agree on what the stages are — "needs a
 * beta read" and "cut but keeping" are real stages for the people who use them.
 */
export function SceneStagesCard(): React.JSX.Element {
  const { t } = useTranslation()
  const stages = useStageStore((s) => s.stages)
  const [draft, setDraft] = useState<SceneStage[]>([])
  const [dirty, setDirty] = useState(false)

  useEffect(() => {
    void useStageStore.getState().load()
  }, [])

  useEffect(() => {
    if (!dirty) setDraft(stages)
  }, [stages, dirty])

  const edit = (index: number, patch: Partial<SceneStage>): void => {
    setDirty(true)
    setDraft(draft.map((s, i) => (i === index ? { ...s, ...patch } : s)))
  }

  const add = (): void => {
    setDirty(true)
    // A key the writer never sees, derived once so renaming the label later
    // cannot orphan the scenes already at this stage.
    setDraft([
      ...draft,
      { key: `stage-${draft.length + 1}-${Date.now()}`, label: '', color: '#8b8b8b', countsAsWritten: true }
    ])
  }

  const save = async (): Promise<void> => {
    await useStageStore.getState().save(draft)
    setDirty(false)
  }

  return (
    <div className="settings-subgroup">
      <div className="settings-hint">{t('stages.intro')}</div>

      {draft.map((stage, index) => (
        <div key={stage.key} className="stage-row">
          <input
            type="color"
            className="stage-color"
            value={stage.color}
            title={t('stages.color')}
            onChange={(e) => edit(index, { color: e.target.value })}
          />
          <input
            className="inspector-input"
            value={stage.label}
            placeholder={t('stages.labelPlaceholder')}
            onChange={(e) => edit(index, { label: e.target.value })}
          />
          <label className="match-toggle" title={t('stages.countsAsWrittenHint')}>
            <input
              type="checkbox"
              checked={stage.countsAsWritten}
              onChange={(e) => edit(index, { countsAsWritten: e.target.checked })}
            />
            {t('stages.countsAsWritten')}
          </label>
          <button
            className="dialog-button"
            title={t('stages.remove')}
            onClick={() => {
              setDirty(true)
              setDraft(draft.filter((_, i) => i !== index))
            }}
          >
            <Trash2 size={14} />
          </button>
        </div>
      ))}

      <div className="settings-button-row">
        <button className="dialog-button" onClick={add}>
          <Plus size={14} /> {t('stages.add')}
        </button>
        <button className="dialog-button" disabled={!dirty} onClick={() => void save()}>
          {t('stages.save')}
        </button>
      </div>
      {dirty && <div className="settings-hint">{t('stages.unsaved')}</div>}
    </div>
  )
}
