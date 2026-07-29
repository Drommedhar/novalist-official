import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Plus, Trash2 } from 'lucide-react'
import { rpc } from '../../rpc/client'

interface BeatDef {
  key: string
  title: string
  description: string
  targetPercent: number
  categoryId: string
}

interface StructureDefinition {
  id: string
  displayName: string
  description: string
  beats: BeatDef[]
}

/**
 * Authoring a story structure.
 *
 * Novalist shipped four structures in a hardcoded array with no save, import or
 * delete path, so a writer following a method it had not heard of could not use
 * the structure feature at all. A structure is a list of beats and where each
 * one belongs; there is no reason it has to be one of ours.
 */
export function StructureEditor(props: {
  /** The structure to edit, or empty to start a new one. */
  templateId: string
  onDone: () => void
}): React.JSX.Element {
  const { t } = useTranslation()
  const [draft, setDraft] = useState<StructureDefinition>({
    id: '',
    displayName: '',
    description: '',
    beats: []
  })

  useEffect(() => {
    if (!props.templateId) return
    void rpc
      .request<StructureDefinition | null>('structure/template', [props.templateId])
      .then((found) => {
        // Editing a built-in saves a copy under the same id, which is how a
        // shipped method gets adjusted rather than being untouchable.
        if (found) setDraft(found)
      })
  }, [props.templateId])

  const edit = (index: number, patch: Partial<BeatDef>): void =>
    setDraft({
      ...draft,
      beats: draft.beats.map((b, i) => (i === index ? { ...b, ...patch } : b))
    })

  return (
    <div className="dialog-overlay" onPointerDown={(e) => e.target === e.currentTarget && props.onDone()}>
      <div className="dialog-card snowflake-card" role="dialog" aria-label={t('structure.editTitle')}>
        <div className="dialog-title">{t('structure.editTitle')}</div>

        <label className="inspector-label">{t('structure.name')}</label>
        <input
          className="dialog-input"
          autoFocus
          value={draft.displayName}
          onChange={(e) => setDraft({ ...draft, displayName: e.target.value })}
        />

        <label className="inspector-label">{t('structure.description')}</label>
        <input
          className="dialog-input"
          value={draft.description}
          onChange={(e) => setDraft({ ...draft, description: e.target.value })}
        />

        <label className="inspector-label">{t('structure.beats')}</label>
        {draft.beats.map((beat, index) => (
          <div key={index} className="structure-beat-row">
            <input
              className="dialog-input"
              aria-label={t('structure.beatTitle')}
              placeholder={t('structure.beatTitlePlaceholder')}
              value={beat.title}
              onChange={(e) => edit(index, { title: e.target.value })}
            />
            {/* Where the beat belongs, as a percentage through the book: this
                is what makes a structure more than a checklist. */}
            <input
              className="dialog-input"
              type="number"
              min={0}
              max={100}
              aria-label={t('structure.beatPercent')}
              value={beat.targetPercent}
              onChange={(e) => edit(index, { targetPercent: Number(e.target.value) || 0 })}
            />
            <button
              className="dialog-button danger"
              title={t('structure.removeBeat')}
              onClick={() => setDraft({ ...draft, beats: draft.beats.filter((_, i) => i !== index) })}
            >
              <Trash2 size={14} />
            </button>
          </div>
        ))}

        <div className="settings-button-row">
          <button
            className="dialog-button"
            onClick={() =>
              setDraft({
                ...draft,
                beats: [
                  ...draft.beats,
                  { key: '', title: '', description: '', targetPercent: 50, categoryId: 'plot' }
                ]
              })
            }
          >
            <Plus size={14} /> {t('structure.addBeat')}
          </button>
        </div>

        <div className="dialog-actions">
          <button className="dialog-button" onClick={props.onDone}>
            {t('dialog.cancel')}
          </button>
          <button
            className="dialog-button primary"
            disabled={draft.displayName.trim().length === 0}
            onClick={() => void rpc.request('structure/saveTemplate', [draft]).then(props.onDone)}
          >
            {t('dialog.save')}
          </button>
        </div>
      </div>
    </div>
  )
}
