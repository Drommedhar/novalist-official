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
  relativeAmount?: number
  relativeUnit?: string
}

/** Units a scene can be offset by. Hours is what "later" usually means. */
const TIME_UNITS = ['Minutes', 'Hours', 'Days', 'Weeks']

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
function useSceneNotes() {
  const { t } = useTranslation()
  const chapters = useProjectStore((s) => s.chapters)
  const openChapterGuid = useProjectStore((s) => s.openChapterGuid)
  const openSceneId = useProjectStore((s) => s.openSceneId)
  const chapter = chapters.find((c) => c.guid === openChapterGuid)
  const scene = chapter?.scenes.find((sc) => sc.id === openSceneId)

  const sceneKey = `${openChapterGuid}/${openSceneId}`
  const [loadedSceneKey, setLoadedSceneKey] = useState<string | null>(null)
  const [loadError, setLoadError] = useState(false)
  const [attempt, setAttempt] = useState(0)
  const [synopsis, setSynopsis] = useState('')
  const [notes, setNotes] = useState('')
  const [cast, setCast] = useState<string[]>([])
  const [focus, setFocus] = useState<string | null>(null)
  const [mode, setMode] = useState('')
  const [strand, setStrand] = useState('')
  const [goal, setGoal] = useState('')
  const [outcome, setOutcome] = useState('')
  const [relAmount, setRelAmount] = useState(0)
  const [relUnit, setRelUnit] = useState('Hours')
  const definitions = useManuscriptPropsStore((s) => s.definitions)
  const sceneValues = useManuscriptPropsStore((s) => s.sceneValues)
  const sceneProps = definitions.filter((d) => d.scope === 'Scene')

  useEffect(() => {
    void useManuscriptPropsStore.getState().load()
  }, [])

  useEffect(() => { setSynopsis(scene?.synopsis ?? '') }, [openChapterGuid, openSceneId, scene?.synopsis])

  useEffect(() => {
    let cancelled = false
    setLoadedSceneKey(null)
    setLoadError(false)
    if (openChapterGuid && openSceneId) {
      void rpc
        .request<SceneMeta>('scenes/getMeta', [openChapterGuid, openSceneId])
        .then((meta) => {
          if (cancelled) return
          setNotes(meta.notes ?? '')
          setCast(meta.cast ?? [])
          setFocus(meta.focusEntityId ?? null)
          setMode(meta.narrativeMode ?? '')
          setStrand(meta.strand ?? '')
          setGoal(meta.goal ?? '')
          setOutcome(meta.outcome ?? '')
          setRelAmount(meta.relativeAmount ?? 0)
          setRelUnit(meta.relativeUnit || 'Hours')
          setLoadedSceneKey(sceneKey)
        })
        .catch(() => { if (!cancelled) setLoadError(true) })
    }
    return () => { cancelled = true }
  }, [openChapterGuid, openSceneId, sceneKey, attempt])

  return { t, openChapterGuid, openSceneId, scene, sceneKey, loadedSceneKey, loadError, setAttempt, synopsis, setSynopsis, notes, setNotes, cast, setCast, focus, setFocus, mode, setMode, strand, setStrand, goal, setGoal, outcome, setOutcome, relAmount, setRelAmount, relUnit, setRelUnit, sceneValues, sceneProps }
}

export function SceneNotesFields({ idPrefix = 'dock' }: { idPrefix?: string }): React.JSX.Element {
  const state = useSceneNotes()
  const { t, openChapterGuid, openSceneId, scene, sceneKey, loadedSceneKey, loadError, setAttempt, synopsis, setSynopsis, notes, setNotes, cast, setCast, focus, setFocus, goal, setGoal, outcome, setOutcome, sceneValues, sceneProps } = state
  if (!(openChapterGuid && openSceneId && scene)) {
    return <div className="notes-dock-empty">{t('sceneNotes.empty')}</div>
  }

  if (loadedSceneKey !== sceneKey) return <div className="notes-dock-empty" role="status">
    {loadError ? <button className="dialog-button" onClick={() => setAttempt((value) => value + 1)}>{t('shell.retry')}</button> : t('context.loading')}
  </div>

  return (
    <div className="notes-dock-body">
      <div className="notes-dock-col">
        <label className="notes-dock-label" htmlFor={`${idPrefix}-synopsis`}>
          {t('sceneNotes.synopsisTitle')}
        </label>
        <textarea
          id={`${idPrefix}-synopsis`}
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
        <label className="notes-dock-label" htmlFor={`${idPrefix}-notes`}>
          {t('sceneNotes.title')}
        </label>
        <textarea
          id={`${idPrefix}-notes`}
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
        <label className="notes-dock-label" htmlFor={`${idPrefix}-goal`}>
          {t('sceneNotes.goal')}
        </label>
        <textarea
          id={`${idPrefix}-goal`}
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
        <label className="notes-dock-label" htmlFor={`${idPrefix}-outcome`}>
          {t('sceneNotes.outcome')}
        </label>
        <textarea
          id={`${idPrefix}-outcome`}
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

      <SceneNarrativeFields state={state} idPrefix={idPrefix} />
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

function SceneNarrativeFields({ state, idPrefix }: { state: ReturnType<typeof useSceneNotes>; idPrefix: string }): React.JSX.Element {
  const { t, openChapterGuid, openSceneId, mode, setMode, strand, setStrand, relAmount, setRelAmount, relUnit, setRelUnit } = state
  return <>
      {/* "The next morning", said so the app can count with it. A writer who
          knows a scene is two hours after the last one and not which day had
          to invent a date or leave it blank - and blank dropped the scene out
          of the Calendar and the Timeline entirely. */}
      <div className="notes-dock-col notes-dock-props">
        <label className="notes-dock-label" htmlFor={`${idPrefix}-rel-amount`}>
          {t('narrative.relativeTime')}
        </label>
        <div className="notes-dock-relative">
          <input
            id={`${idPrefix}-rel-amount`}
            className="inspector-input"
            type="number"
            value={relAmount}
            onChange={(e) => setRelAmount(Number(e.target.value) || 0)}
            onBlur={() =>
              void rpc.request('scenes/setRelativeTime', [
                openChapterGuid,
                openSceneId,
                relAmount,
                relUnit
              ])
            }
          />
          <select
            className="inspector-input"
            aria-label={t('narrative.relativeUnit')}
            value={relUnit}
            onChange={(e) => {
              setRelUnit(e.target.value)
              void rpc.request('scenes/setRelativeTime', [
                openChapterGuid,
                openSceneId,
                relAmount,
                e.target.value
              ])
            }}
          >
            {TIME_UNITS.map((unit) => (
              <option key={unit} value={unit}>
                {t(`narrative.unit${unit}`)}
              </option>
            ))}
          </select>
        </div>
        <div className="settings-hint">{t('narrative.relativeHint')}</div>
      </div>

      {/* A flashback sorts by its date like everything else unless the scene
          says what it is. */}
      <div className="notes-dock-col notes-dock-props">
        <label className="notes-dock-label" htmlFor={`${idPrefix}-mode`}>
          {t('narrative.title')}
        </label>
        <select
          id={`${idPrefix}-mode`}
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

  </>
}
