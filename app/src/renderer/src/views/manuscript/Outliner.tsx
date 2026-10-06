import { useEffect } from 'react'
import { useTranslation } from 'react-i18next'
import { useManuscriptStore } from '../../stores/manuscriptStore'
import { useProjectStore } from '../../stores/projectStore'
import { handleSceneClick, useSelectionStore } from '../../stores/selectionStore'
import { useTargetStore } from '../../stores/targetStore'
import { useManuscriptPropsStore } from '../../stores/manuscriptPropsStore'
import { ManuscriptPropertyField } from '../../shell/ManuscriptPropertyField'
import { LazyBlock } from '../../shell/LazyBlock'
import { EAGER_SECTIONS } from './ManuscriptFrame'

const OUTLINER_ROW_HEIGHT = 34

export function Outliner(): React.JSX.Element {
  const { t } = useTranslation()
  const sections = useManuscriptStore((s) => s.sections)
  const setSynopsis = useManuscriptStore((s) => s.setSynopsis)
  const setPov = useManuscriptStore((s) => s.setPov)
  const setGoalOutcome = useManuscriptStore((s) => s.setGoalOutcome)
  const selectedIds = useSelectionStore((s) => s.sceneIds)
  const targets = useTargetStore((s) => s.targets)
  const definitions = useManuscriptPropsStore((s) => s.definitions)
  const sceneValues = useManuscriptPropsStore((s) => s.sceneValues)

  useEffect(() => {
    void useTargetStore.getState().load()
    void useManuscriptPropsStore.getState().load()
  }, [])

  // The writer picks which of their fields is worth a column; a dozen fields
  // is not a dozen columns anybody wants to read.
  const columns = definitions.filter((d) => d.scope === 'Scene' && d.showInOutliner)
  // Every row uses the same readable column floors. A narrow pane scrolls the
  // whole grid instead of collapsing synopsis, goal and outcome to zero width.
  const grid = {
    gridTemplateColumns: `repeat(5, minmax(var(--nl-outliner-text-width), 1fr)) repeat(${3 + columns.length}, var(--nl-outliner-short-width))`,
    minWidth: `calc(5 * var(--nl-outliner-text-width) + ${3 + columns.length} * var(--nl-outliner-short-width) + ${7 + columns.length} * var(--nl-space-md))`
  }

  return (
    <div className="outliner">
      <div className="outliner-row outliner-head" style={grid}>
        <span>{t('shell.chapters')}</span>
        <span>{t('shell.scenes')}</span>
        <span>{t('sceneNotes.synopsisTitle')}</span>
        {/* Read down the two columns and a scene where nothing happens says so:
            the outcome repeats the goal, or there is no outcome at all. */}
        <span>{t('sceneNotes.goal')}</span>
        <span>{t('sceneNotes.outcome')}</span>
        <span>{t('common.povWatermark')}</span>
        <span>{t('shell.wordsTitle')}</span>
        <span>{t('targets.column')}</span>
        {columns.map((property) => (
          <span key={property.key}>{property.label}</span>
        ))}
      </div>
      {sections.map((section, index) => (
        <LazyBlock
          key={section.chapterGuid}
          eager={index < EAGER_SECTIONS}
          estimatedHeight={OUTLINER_ROW_HEIGHT * Math.max(1, section.scenes.length)}
        >
          {section.scenes.map((scene) => (
            <div
              key={scene.sceneId}
              className={`outliner-row${selectedIds.includes(scene.sceneId) ? ' selected' : ''}${
                scene.inactive ? ' inactive' : ''
              }`}
              style={grid}
            >
              <span className="outliner-cell">{section.chapterTitle}</span>
              <button
                className="outliner-cell outliner-scene-title"
                onClick={(e) => {
                  if (handleSceneClick(scene.sceneId, e)) return
                  void useProjectStore.getState().openScene(section.chapterGuid, scene.sceneId)
                }}
              >
                {scene.title}
              </button>
              <input
                className="outliner-input"
                defaultValue={scene.synopsis ?? ''}
                onBlur={(e) => void setSynopsis(section.chapterGuid, scene.sceneId, e.target.value)}
              />
              <input
                className="outliner-input"
                defaultValue={scene.goal ?? ''}
                placeholder={t('sceneNotes.goal')}
                onBlur={(e) =>
                  void setGoalOutcome(
                    section.chapterGuid,
                    scene.sceneId,
                    e.target.value,
                    scene.outcome ?? ''
                  )
                }
              />
              <input
                className="outliner-input"
                defaultValue={scene.outcome ?? ''}
                placeholder={t('sceneNotes.outcome')}
                onBlur={(e) =>
                  void setGoalOutcome(
                    section.chapterGuid,
                    scene.sceneId,
                    scene.goal ?? '',
                    e.target.value
                  )
                }
              />
              <input
                className="outliner-input"
                defaultValue={scene.pov ?? ''}
                placeholder={t('common.povWatermark')}
                onBlur={(e) => void setPov(section.chapterGuid, scene.sceneId, e.target.value)}
              />
              <span className="outliner-cell outliner-words">
                {scene.wordCount.toLocaleString()}
              </span>
              {/* Editable in place: the Outliner is where a writer sets targets
                  across a run of scenes, and a dialog per scene would be worse. */}
              <input
                className="outliner-input outliner-target"
                type="number"
                min={0}
                defaultValue={
                  targets.find((tg) => tg.kind === 'scene' && tg.id === scene.sceneId)?.target ?? ''
                }
                placeholder={t('targets.none')}
                onBlur={(e) =>
                  void useTargetStore
                    .getState()
                    .setScene(section.chapterGuid, scene.sceneId, Number(e.target.value) || null)
                }
              />
              {columns.map((property) => (
                <ManuscriptPropertyField
                  key={property.key}
                  className="outliner-input"
                  property={property}
                  value={sceneValues[scene.sceneId]?.[property.key] ?? ''}
                  onCommit={(value) =>
                    void useManuscriptPropsStore
                      .getState()
                      .setSceneValue(scene.sceneId, property.key, value)
                  }
                />
              ))}
            </div>
          ))}
        </LazyBlock>
      ))}
    </div>
  )
}
