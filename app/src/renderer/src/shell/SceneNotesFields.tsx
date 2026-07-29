import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useProjectStore } from '../stores/projectStore'
import { useManuscriptPropsStore } from '../stores/manuscriptPropsStore'
import { ManuscriptPropertyField } from './ManuscriptPropertyField'
import { rpc } from '../rpc/client'

interface SceneMeta {
  notes?: string | null
}

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
        .then((meta) => setNotes(meta.notes ?? ''))
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
