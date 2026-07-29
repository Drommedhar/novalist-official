import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useManuscriptStore } from '../../stores/manuscriptStore'
import { useProjectStore } from '../../stores/projectStore'
import { useStageStore } from '../../stores/stageStore'
import { useManuscriptPropsStore } from '../../stores/manuscriptPropsStore'
import { handleSceneClick, useSelectionStore } from '../../stores/selectionStore'
import './board.css'

/** A column, and the scenes that belong in it. */
interface Column {
  key: string
  label: string
  sceneIds: string[]
}

/**
 * Scene cards in columns, grouped by whatever the writer is thinking about.
 *
 * The manuscript had three modes - continuous text, a chapter-grouped
 * corkboard, and a table - and every one of them groups by chapter. A board
 * that groups by stage answers "what is left to revise"; by POV, "how much of
 * the book is his"; by a field of the writer's own, whatever they made it for.
 *
 * Dropping a card in another column writes that field, which is the only thing
 * that makes a board different from a filter.
 */
export function Board(): React.JSX.Element {
  const { t } = useTranslation()
  const sections = useManuscriptStore((s) => s.sections)
  const groupBy = useManuscriptStore((s) => s.groupBy)
  const chapters = useProjectStore((s) => s.chapters)
  const stages = useStageStore((s) => s.stages)
  const definitions = useManuscriptPropsStore((s) => s.definitions)
  const sceneValues = useManuscriptPropsStore((s) => s.sceneValues)
  const selectedIds = useSelectionStore((s) => s.sceneIds)
  const [dragging, setDragging] = useState<string | null>(null)
  const [over, setOver] = useState<string | null>(null)

  useEffect(() => {
    void useStageStore.getState().load()
    void useManuscriptPropsStore.getState().load()
  }, [])

  /** Every scene in the book, with the chapter it currently sits in. */
  const scenes = sections.flatMap((section) =>
    section.scenes.map((scene) => ({ ...scene, section }))
  )

  const stageOf = (sceneId: string): string =>
    chapters.flatMap((c) => c.scenes).find((s) => s.id === sceneId)?.stage ?? ''

  const property = groupBy.startsWith('prop:')
    ? definitions.find((d) => d.key === groupBy.slice(5))
    : undefined

  /** What column a scene belongs in under the current grouping. */
  const valueOf = (scene: (typeof scenes)[number]): string => {
    if (groupBy === 'chapter') return scene.section.chapterGuid
    if (groupBy === 'stage') return stageOf(scene.sceneId)
    if (groupBy === 'pov') return scene.pov ?? ''
    return sceneValues[scene.sceneId]?.[groupBy.slice(5)] ?? ''
  }

  /** The columns to draw, before any scene is placed in them. */
  const baseColumns = (): Column[] => {
    if (groupBy === 'chapter')
      return chapters.map((c) => ({ key: c.guid, label: c.title, sceneIds: [] }))
    if (groupBy === 'stage')
      return stages.map((s) => ({ key: s.key, label: s.label, sceneIds: [] }))
    if (property?.type === 'Enum')
      return property.enumOptions.map((o) => ({ key: o, label: o, sceneIds: [] }))
    if (property?.type === 'Bool')
      return [{ key: 'true', label: t('board.yes'), sceneIds: [] }]
    // POV and free-text fields have no list to draw from, so the columns are
    // whatever the book actually contains.
    return [
      ...new Set(scenes.map(valueOf).filter((v) => v.length > 0))
    ].sort().map((v) => ({ key: v, label: v, sceneIds: [] }))
  }

  const columns = baseColumns()
  const unset: Column = { key: '', label: t('board.unset'), sceneIds: [] }
  for (const scene of scenes) {
    const value = valueOf(scene)
    const column = columns.find((c) => c.key === value) ?? unset
    column.sceneIds.push(scene.sceneId)
  }
  // The untriaged pile is always shown: a board that hides the scenes nobody
  // has classified yet is a board that says the work is finished.
  const shown = [...columns, unset]

  const drop = async (columnKey: string): Promise<void> => {
    const sceneId = dragging
    setDragging(null)
    setOver(null)
    if (!sceneId) return
    const scene = scenes.find((s) => s.sceneId === sceneId)
    if (!scene || valueOf(scene) === columnKey) return

    if (groupBy === 'chapter') {
      const target = chapters.find((c) => c.guid === columnKey)
      if (!target) return
      await useProjectStore.getState().moveScenes([sceneId], columnKey, target.scenes.length)
    } else if (groupBy === 'stage') {
      await useStageStore
        .getState()
        .setSceneStage(scene.section.chapterGuid, sceneId, columnKey || null)
    } else if (groupBy === 'pov') {
      await useManuscriptStore.getState().setPov(scene.section.chapterGuid, sceneId, columnKey)
    } else {
      await useManuscriptPropsStore
        .getState()
        .setSceneValue(sceneId, groupBy.slice(5), columnKey || null)
    }
    await useManuscriptStore.getState().load()
  }

  return (
    <div className="board">
      {shown.map((column) => (
        <div
          key={column.key || 'unset'}
          className={`board-column${over === column.key ? ' over' : ''}`}
          onDragOver={(e) => {
            e.preventDefault()
            setOver(column.key)
          }}
          onDragLeave={() => setOver((k) => (k === column.key ? null : k))}
          onDrop={() => void drop(column.key)}
        >
          <div className="board-column-head">
            <span className="board-column-title">{column.label}</span>
            <span className="board-column-count">{column.sceneIds.length}</span>
          </div>
          {column.sceneIds.map((sceneId) => {
            const scene = scenes.find((s) => s.sceneId === sceneId)!
            return (
              <div
                key={sceneId}
                className={`corkboard-card board-card${
                  selectedIds.includes(sceneId) ? ' selected' : ''
                }`}
                draggable
                onDragStart={() => setDragging(sceneId)}
                onDragEnd={() => setDragging(null)}
              >
                <button
                  className="corkboard-card-title"
                  onClick={(e) => {
                    if (handleSceneClick(sceneId, e)) return
                    void useProjectStore
                      .getState()
                      .openScene(scene.section.chapterGuid, sceneId)
                  }}
                >
                  {scene.title}
                </button>
                {/* Which chapter a card is from stops mattering only when the
                    board is grouped by chapter. */}
                {groupBy !== 'chapter' && (
                  <div className="board-card-chapter">{scene.section.chapterTitle}</div>
                )}
                {scene.synopsis && <div className="board-card-synopsis">{scene.synopsis}</div>}
                <div className="corkboard-card-words">
                  {scene.wordCount.toLocaleString()} {t('shell.words')}
                </div>
              </div>
            )
          })}
        </div>
      ))}
    </div>
  )
}
