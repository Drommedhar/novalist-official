import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useProjectStore } from '../stores/projectStore'
import { useManuscriptPropsStore } from '../stores/manuscriptPropsStore'
import { ManuscriptPropertyField } from './ManuscriptPropertyField'
import { SceneCastPicker } from './SceneCastPicker'
import { rpc } from '../rpc/client'

interface SceneMeta {
  notes?: string | null
  cast?: string[]
  focusEntityId?: string | null
  narrativeMode?: string | null
  strand?: string | null
  goal?: string | null
  outcome?: string | null
}

/** How a scene can sit in time relative to the story around it. */
const NARRATIVE_MODES = [
  'flashback',
  'flashforward',
  'parallel',
  'frame',
  'dream',
  'timeskip'
]

/**
 * Synopsis + Notes fields for the open scene, shared by the desktop bottom dock
 * (SceneNotesDock) and the mobile writing-hub sheet. Reads the open scene from the
 * store and commits on blur. Layout (side-by-side vs stacked) is left to the parent
 * via the .notes-dock-body container styling.
 */
export function SceneNotesFields(): React.JSX.Element {
  const { t } = useTranslation()
  const chapters = useProjectStore((s) => s.chapters)
  const openChapterGuid = useProjectStore((s) => s.openChapterGuid)
  const openSceneId = useProjectStore((s) => s.openSceneId)
  const chapter = chapters.find((c) => c.guid === openChapterGuid)
  const scene = chapter?.scenes.find((sc) => sc.id === openSceneId)

  const [synopsis, setSynopsis] = useState('')
  const [notes, setNotes] = useState('')
  const [cast, setCast] = useState<string[]>([])
  const [focus, setFocus] = useState<string | null>(null)
  const [mode, setMode] = useState('')
  const [strand, setStrand] = useState('')
  const [goal, setGoal] = useState('')
  const [outcome, setOutcome] = useState('')
  const definitions = useManuscriptPropsStore((s) => s.definitions)
  const sceneValues = useManuscriptPropsStore((s) => s.sceneValues)
  const sceneProps = definitions.filter((d) => d.scope === 'Scene')

  useEffect(() => {
    void useManuscriptPropsStore.getState().load()
  }, [])

  useEffect(() => {
    setSynopsis(scene?.synopsis ?? '')
    setNotes('')
    if (openChapterGuid && openSceneId) {
      void rpc
        .request<SceneMeta>('scenes/getMeta', [openChapterGuid, openSceneId])
        .then((meta) => {
          setNotes(meta.notes ?? '')
          setCast(meta.cast ?? [])
          setFocus(meta.focusEntityId ?? null)
          setMode(meta.narrativeMode ?? '')
          setStrand(meta.strand ?? '')
          setGoal(meta.goal ?? '')
          setOutcome(meta.outcome ?? '')
        })
        .catch(() => setNotes(''))
    }
  }, [openChapterGuid, openSceneId, scene?.synopsis])

  if (!(openChapterGuid && openSceneId && scene)) {
    return <div className="notes-dock-empty">{t('sceneNotes.empty')}</div>
  }

  return (
    <div className="notes-dock-body">
      <div className="notes-dock-col">
        <label className="notes-dock-label" htmlFor="dock-synopsis">
          {t('sceneNotes.synopsisTitle')}
        </label>
        <textarea
          id="dock-synopsis"
          className="notes-dock-textarea notes-dock-synopsis"
          placeholder={t('sceneNotes.synopsisPlaceholder')}
          value={synopsis}
          onChange={(e) => setSynopsis(e.target.value)}
          onBlur={() =>
            void rpc.request('scenes/setSynopsis', [openChapterGuid, openSceneId, synopsis])
          }
        />
      </div>
      <div className="notes-dock-col notes-dock-col-grow">
        <label className="notes-dock-label" htmlFor="dock-notes">
          {t('sceneNotes.title')}
        </label>
        <textarea
          id="dock-notes"
          className="notes-dock-textarea"
          placeholder={t('sceneNotes.placeholder')}
          value={notes}
          onChange={(e) => setNotes(e.target.value)}
          onBlur={() => void rpc.request('scenes/setNotes', [openChapterGuid, openSceneId, notes])}
        />
      </div>
      {/* What they wanted and what they got. Conflict sits between the two and
          is read out of the prose in the Inspector; these two never are,
          because a goal nobody stated and an outcome nobody wrote down are
          precisely what a draft is missing. */}
      <div className="notes-dock-col notes-dock-props">
        <label className="notes-dock-label" htmlFor="dock-goal">
          {t('sceneNotes.goal')}
        </label>
        <textarea
          id="dock-goal"
          className="notes-dock-textarea notes-dock-short"
          placeholder={t('sceneNotes.goalPlaceholder')}
          value={goal}
          onChange={(e) => setGoal(e.target.value)}
          onBlur={() =>
            void rpc.request('scenes/setGoalOutcome', [
              openChapterGuid,
              openSceneId,
              goal,
              outcome
            ])
          }
        />
        <label className="notes-dock-label" htmlFor="dock-outcome">
          {t('sceneNotes.outcome')}
        </label>
        <textarea
          id="dock-outcome"
          className="notes-dock-textarea notes-dock-short"
          placeholder={t('sceneNotes.outcomePlaceholder')}
          value={outcome}
          onChange={(e) => setOutcome(e.target.value)}
          onBlur={() =>
            void rpc.request('scenes/setGoalOutcome', [
              openChapterGuid,
              openSceneId,
              goal,
              outcome
            ])
          }
        />
      </div>

      {/* A flashback sorts by its date like everything else unless the scene
          says what it is. */}
      <div className="notes-dock-col notes-dock-props">
        <label className="notes-dock-label" htmlFor="dock-mode">
          {t('narrative.title')}
        </label>
        <select
          id="dock-mode"
          className="inspector-input"
          value={mode}
          onChange={(e) => {
            setMode(e.target.value)
            void rpc.request('scenes/setNarrativeMode', [
              openChapterGuid,
              openSceneId,
              e.target.value || null,
              strand || null
            ])
          }}
        >
          <option value="">{t('narrative.normal')}</option>
          {NARRATIVE_MODES.map((m) => (
            <option key={m} value={m}>
              {t(`timeline.mode_${m}`)}
            </option>
          ))}
        </select>
        {/* Only a scene running alongside another belongs to a strand. */}
        {mode === 'parallel' && (
          <input
            className="inspector-input"
            placeholder={t('narrative.strandPlaceholder')}
            aria-label={t('narrative.strand')}
            value={strand}
            onChange={(e) => setStrand(e.target.value)}
            onBlur={() =>
              void rpc.request('scenes/setNarrativeMode', [
                openChapterGuid,
                openSceneId,
                mode || null,
                strand || null
              ])
            }
          />
        )}
      </div>

      {/* Who and what is in the scene, said outright rather than inferred
          from which names the prose happens to use. */}
      <div className="notes-dock-col notes-dock-props">
        <label className="notes-dock-label">{t('cast.title')}</label>
        <SceneCastPicker
          chapterGuid={openChapterGuid}
          sceneId={openSceneId}
          cast={cast}
          focusEntityId={focus}
          onChange={(next, nextFocus) => {
            setCast(next)
            setFocus(nextFocus)
          }}
        />
      </div>

      {/* The book's own scene fields. Nothing shows when the writer has
          defined none, which is the state every project starts in. */}
      {sceneProps.length > 0 && (
        <div className="notes-dock-col notes-dock-props">
          <label className="notes-dock-label">{t('props.sceneTitle')}</label>
          {sceneProps.map((property) => (
            <div key={property.key} className="notes-dock-prop">
              <span className="notes-dock-prop-label">{property.label}</span>
              <ManuscriptPropertyField
                property={property}
                value={sceneValues[openSceneId]?.[property.key] ?? ''}
                onCommit={(value) =>
                  void useManuscriptPropsStore
                    .getState()
                    .setSceneValue(openSceneId, property.key, value)
                }
              />
            </div>
          ))}
        </div>
      )}
    </div>
  )
}
