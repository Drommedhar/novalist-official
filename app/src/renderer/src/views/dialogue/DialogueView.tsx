import { useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { ExternalLink, Search } from 'lucide-react'
import {
  UNASSIGNED_SPEAKER_ID,
  useDialogueStore,
  type DialogueConfidence,
  type DialogueLine,
  type DialogueScene
} from '../../stores/dialogueStore'
import { useProjectStore } from '../../stores/projectStore'
import { useShellStore } from '../../stores/shellStore'
import './dialogue.css'

/**
 * Every line one character speaks, gathered from the whole book and laid out in
 * story order, so a drift in how they talk is readable end to end. Lines can be
 * rewritten here and the edit lands in the scene file; the speaker behind a
 * line can be corrected when the automatic attribution guessed wrong.
 */
export function DialogueView(): React.JSX.Element {
  const { t } = useTranslation()
  const speakers = useDialogueStore((s) => s.speakers)
  const unassignedCount = useDialogueStore((s) => s.unassignedCount)
  const selectedId = useDialogueStore((s) => s.selectedId)
  const groups = useDialogueStore((s) => s.groups)
  const loading = useDialogueStore((s) => s.loading)
  const staleError = useDialogueStore((s) => s.staleError)
  const load = useDialogueStore((s) => s.load)
  const select = useDialogueStore((s) => s.select)
  const clearStaleError = useDialogueStore((s) => s.clearStaleError)
  const [filter, setFilter] = useState('')

  useEffect(() => {
    void load()
  }, [load])

  const shown = useMemo(() => {
    const needle = filter.trim().toLowerCase()
    if (needle.length === 0) return speakers
    return speakers.filter((s) => s.name.toLowerCase().includes(needle))
  }, [speakers, filter])

  const lineCount = groups.reduce(
    (total, group) => total + group.scenes.reduce((n, scene) => n + scene.lines.length, 0),
    0
  )
  const isEmpty = !loading && speakers.length === 0 && unassignedCount === 0

  return (
    <div className="dialogue-view">
      <aside className="dialogue-roster" aria-label={t('dialogue.roster')}>
        <div className="dialogue-roster-title">{t('shell.view.dialogue')}</div>
        {speakers.length > 0 && (
          <div className="dialogue-roster-search">
            <Search size={13} strokeWidth={1.75} aria-hidden="true" />
            <input
              type="search"
              value={filter}
              placeholder={t('dialogue.filterPlaceholder')}
              aria-label={t('dialogue.filterPlaceholder')}
              onChange={(e) => setFilter(e.target.value)}
            />
          </div>
        )}
        {loading && speakers.length === 0 && (
          <div className="dialogue-status">{t('dialogue.loading')}</div>
        )}
        {isEmpty && <div className="dialogue-status">{t('dialogue.empty')}</div>}
        <ul className="dialogue-roster-list">
          {shown.map((speaker) => (
            <li key={speaker.characterId}>
              <button
                type="button"
                className={`dialogue-roster-item${
                  selectedId === speaker.characterId ? ' active' : ''
                }`}
                aria-current={selectedId === speaker.characterId ? 'true' : undefined}
                onClick={() => void select(speaker.characterId)}
              >
                <span className="dialogue-roster-name">{speaker.name}</span>
                <span className="dialogue-roster-count">{speaker.lineCount}</span>
              </button>
            </li>
          ))}
          {unassignedCount > 0 && (
            <li>
              <button
                type="button"
                className={`dialogue-roster-item dialogue-roster-unassigned${
                  selectedId === UNASSIGNED_SPEAKER_ID ? ' active' : ''
                }`}
                aria-current={selectedId === UNASSIGNED_SPEAKER_ID ? 'true' : undefined}
                onClick={() => void select(UNASSIGNED_SPEAKER_ID)}
              >
                <span className="dialogue-roster-name">{t('dialogue.unassigned')}</span>
                <span className="dialogue-roster-count">{unassignedCount}</span>
              </button>
            </li>
          )}
        </ul>
      </aside>

      <div className="dialogue-main">
        {staleError && (
          <div className="dialogue-banner" role="alert">
            <span>{t('dialogue.staleWarning')}</span>
            <button
              type="button"
              onClick={() => {
                clearStaleError()
                void load()
              }}
            >
              {t('dialogue.reload')}
            </button>
          </div>
        )}

        {selectedId && (
          <div className="dialogue-main-header">
            <h1>{selectedName(selectedId, speakers, t)}</h1>
            <span className="dialogue-main-sub">
              {t('dialogue.lineSummary', { lines: lineCount, groups: groups.length })}
            </span>
          </div>
        )}

        {!loading && selectedId && groups.length === 0 && (
          <div className="dialogue-status">{t('dialogue.noLines')}</div>
        )}

        {groups.map((group, index) => (
          <section className="dialogue-group" key={`${group.storyDate}-${index}`}>
            <h2 className="dialogue-group-date">
              {group.storyDate || t('dialogue.undated')}
            </h2>
            {group.scenes.map((scene) => (
              <DialogueSceneBlock
                key={`${scene.chapterGuid}-${scene.sceneId}`}
                scene={scene}
                speakerId={selectedId}
              />
            ))}
          </section>
        ))}
      </div>
    </div>
  )
}

function selectedName(
  selectedId: string,
  speakers: { characterId: string; name: string }[],
  t: (key: string) => string
): string {
  if (selectedId === UNASSIGNED_SPEAKER_ID) return t('dialogue.unassigned')
  return speakers.find((s) => s.characterId === selectedId)?.name ?? ''
}

function DialogueSceneBlock({
  scene,
  speakerId
}: {
  scene: DialogueScene
  speakerId: string | null
}): React.JSX.Element {
  const { t } = useTranslation()
  const openScene = useProjectStore((s) => s.openScene)
  const setMainView = useShellStore((s) => s.setMainView)

  const goToScene = async (): Promise<void> => {
    await openScene(scene.chapterGuid, scene.sceneId)
    setMainView('write')
  }

  return (
    <div className="dialogue-scene">
      <div className="dialogue-scene-header">
        <span className="dialogue-scene-title">
          {scene.chapterTitle} &middot; {scene.sceneTitle}
        </span>
        <button
          type="button"
          className="dialogue-scene-open"
          title={t('dialogue.openScene')}
          aria-label={t('dialogue.openScene')}
          onClick={() => void goToScene()}
        >
          <ExternalLink size={13} strokeWidth={1.75} />
        </button>
      </div>
      <ul className="dialogue-lines">
        {scene.lines.map((line) => (
          <DialogueLineRow
            key={line.lineKey}
            line={line}
            chapterGuid={scene.chapterGuid}
            sceneId={scene.sceneId}
            speakerId={speakerId}
          />
        ))}
      </ul>
    </div>
  )
}

/** Localized label for how the speaker was worked out. */
const CONFIDENCE_KEYS: Record<DialogueConfidence, string> = {
  Manual: 'dialogue.confidence.manual',
  High: 'dialogue.confidence.high',
  Inferred: 'dialogue.confidence.inferred',
  Medium: 'dialogue.confidence.medium',
  Low: 'dialogue.confidence.low',
  None: 'dialogue.confidence.none'
}

function DialogueLineRow({
  line,
  chapterGuid,
  sceneId,
  speakerId
}: {
  line: DialogueLine
  chapterGuid: string
  sceneId: string
  /** Whose list this row is being shown in — that character is filtered out of
   *  the suggestions, since the chips exist to change the attribution. */
  speakerId: string | null
}): React.JSX.Element {
  const { t } = useTranslation()
  const characters = useDialogueStore((s) => s.characters)
  const updateLine = useDialogueStore((s) => s.updateLine)
  const setSpeaker = useDialogueStore((s) => s.setSpeaker)
  const [editing, setEditing] = useState(false)
  const [draft, setDraft] = useState(line.text)
  const [saving, setSaving] = useState(false)

  const nameOf = (id: string): string =>
    characters.find((c) => c.id === id)?.name ?? id
  const suggestions = line.candidates.filter((c) => c.characterId !== speakerId)

  const beginEdit = (): void => {
    if (!line.editable) return
    setDraft(line.text)
    setEditing(true)
  }

  const commit = async (): Promise<void> => {
    const next = draft.trim()
    if (next.length === 0 || next === line.text) {
      setEditing(false)
      return
    }
    setSaving(true)
    await updateLine(chapterGuid, sceneId, line.lineKey, line.text, next)
    setSaving(false)
    setEditing(false)
  }

  return (
    <li className={`dialogue-line${line.editable ? '' : ' locked'}`}>
      <div className="dialogue-line-body">
        {editing ? (
          <textarea
            className="dialogue-line-input"
            value={draft}
            autoFocus
            rows={Math.min(6, Math.ceil(draft.length / 70) + 1)}
            disabled={saving}
            aria-label={t('dialogue.editLine')}
            onChange={(e) => setDraft(e.target.value)}
            onBlur={() => void commit()}
            onKeyDown={(e) => {
              if (e.key === 'Escape') {
                e.preventDefault()
                setDraft(line.text)
                setEditing(false)
              } else if (e.key === 'Enter' && !e.shiftKey) {
                e.preventDefault()
                void commit()
              }
            }}
          />
        ) : (
          <button
            type="button"
            className="dialogue-line-text"
            title={line.editable ? t('dialogue.editLine') : t('dialogue.lockedHint')}
            disabled={!line.editable}
            onClick={beginEdit}
          >
            {line.text}
          </button>
        )}
        {line.contextAfter && (
          <span className="dialogue-line-tag" title={line.contextAfter}>
            {line.contextAfter}
          </span>
        )}
        {suggestions.length > 0 && (
          <div className="dialogue-suggestions">
            <span className="dialogue-suggestions-label">{t('dialogue.mightBe')}</span>
            {suggestions.map((candidate) => (
              <button
                key={candidate.characterId}
                type="button"
                className="dialogue-suggestion"
                title={t('dialogue.assignTo', { name: nameOf(candidate.characterId) })}
                onClick={() =>
                  void setSpeaker(chapterGuid, sceneId, line.lineKey, candidate.characterId)
                }
              >
                <span className="dialogue-suggestion-name">{nameOf(candidate.characterId)}</span>
                <span className="dialogue-suggestion-percent">{candidate.percent}%</span>
              </button>
            ))}
          </div>
        )}
      </div>

      <div className="dialogue-line-meta">
        <span
          className={`dialogue-confidence dialogue-confidence-${line.confidence.toLowerCase()}`}
        >
          {t(CONFIDENCE_KEYS[line.confidence])}
        </span>
        <select
          className="dialogue-line-speaker"
          aria-label={t('dialogue.reassign')}
          title={t('dialogue.reassign')}
          value=""
          onChange={(e) => {
            const value = e.target.value
            if (value.length === 0) return
            void setSpeaker(
              chapterGuid,
              sceneId,
              line.lineKey,
              value === UNASSIGNED_SPEAKER_ID ? null : value
            )
          }}
        >
          <option value="">{t('dialogue.reassign')}</option>
          {characters.map((character) => (
            <option key={character.id} value={character.id}>
              {character.name}
            </option>
          ))}
          <option value={UNASSIGNED_SPEAKER_ID}>{t('dialogue.unassigned')}</option>
        </select>
      </div>
    </li>
  )
}
