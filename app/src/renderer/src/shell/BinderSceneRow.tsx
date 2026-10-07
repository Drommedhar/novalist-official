import { MoreHorizontal } from 'lucide-react'
import { handleSceneClick, useSelectionStore } from '../stores/selectionStore'
import type { ChapterDto, SceneDto } from '../stores/projectTypes'
import type { BinderState } from './binderState'

export function BinderSceneRow({ state, chapter, scene, sceneIndex }: {
  state: BinderState; chapter: ChapterDto; scene: SceneDto; sceneIndex: number
}): React.JSX.Element {
  const { t, sortMode, isMobile, setDrag, setMenu, changedIds, selectedIds, stages, targets, openSceneId, openScene, onSceneDrop } = state
  return (
                <div className="binder-scene-wrap">
                <button
                  className={`binder-scene-row${openSceneId === scene.id ? ' active' : ''}${
                    changedIds.has(scene.id) ? ' changed' : ''
                  }${selectedIds.includes(scene.id) ? ' selected' : ''}${
                    scene.inactive ? ' inactive' : ''
                  }`}
                  draggable={sortMode === 'order'}
                  onDragStart={() =>
                    setDrag({ kind: 'scene', chapterGuid: chapter.guid, sceneId: scene.id })
                  }
                  onDragOver={(e) => e.preventDefault()}
                  onDrop={() => onSceneDrop(chapter.guid, { id: scene.id, order: scene.order }, sceneIndex)}
                  onClick={(e) => {
                    // Ctrl/shift build a selection; a plain click still opens the
                    // scene and drops whatever was selected.
                    if (handleSceneClick(scene.id, e)) return
                    void openScene(chapter.guid, scene.id)
                  }}
                  onContextMenu={(e) => {
                    e.preventDefault()
                    // A right-click outside the selection replaces it, so the
                    // menu always acts on what the writer just pointed at.
                    if (!selectedIds.includes(scene.id)) useSelectionStore.getState().clear()
                    setMenu({
                      x: e.clientX,
                      y: e.clientY,
                      chapterGuid: chapter.guid,
                      sceneId: scene.id
                    })
                  }}
                  title={changedIds.has(scene.id) ? t('explorer.changed') : undefined}
                >
                  {/* A dot only where the writer set a stage - an untriaged
                      scene shows nothing rather than claiming to be at the
                      first one. The slot is always there, though: rendering it
                      only for staged scenes made their titles sit a few pixels
                      right of everything else. */}
                  {(() => {
                    const stage = stages.find((st) => st.key === scene.stage)
                    return (
                      <span
                        className="binder-scene-stage"
                        style={{ background: stage ? stage.color : 'transparent' }}
                        title={stage ? stage.label : undefined}
                      />
                    )
                  })()}
                  <span className="binder-scene-title" title={scene.title}>{scene.title}</span>
                  {/* A plotline has carried a colour since the Plot Grid
                      shipped and it never left that view, so the binder could
                      not show that this scene and that one are one thread. */}
                  {scene.plotlineColors.length > 0 && (
                    <span className="binder-scene-threads">
                      {scene.plotlineColors.slice(0, 4).map((color, i) => (
                        <span
                          key={`${color}-${i}`}
                          className="binder-thread-dot"
                          style={{ background: color }}
                        />
                      ))}
                    </span>
                  )}
                  {(() => {
                    const target = targets.find((tg) => tg.kind === 'scene' && tg.id === scene.id)
                    if (!target) {
                      return (
                        <span className="binder-scene-words">
                          {scene.wordCount > 0 ? scene.wordCount.toLocaleString() : ''}
                        </span>
                      )
                    }
                    // Past the target the bar stays full and the count says so,
                    // rather than the bar overflowing its track.
                    const pct = Math.min(100, Math.round((target.words / target.target) * 100))
                    return (
                      <span
                        className="binder-scene-words binder-scene-target"
                        title={t('targets.progress', {
                          words: target.words.toLocaleString(),
                          target: target.target.toLocaleString()
                        })}
                      >
                        <span className="binder-target-track">
                          <span
                            className={`binder-target-fill${target.overrun > 0 ? ' over' : ''}`}
                            style={{ width: `${pct}%` }}
                          />
                        </span>
                        {target.words.toLocaleString()}/{target.target.toLocaleString()}
                      </span>
                    )
                  })()}
                </button>
                {isMobile && (
                  <button
                    className="binder-row-action"
                    aria-label={t('explorer.renameScene')}
                    onClick={(e) => {
                      const r = e.currentTarget.getBoundingClientRect()
                      setMenu({ x: r.left, y: r.bottom, chapterGuid: chapter.guid, sceneId: scene.id })
                    }}
                  >
                    <MoreHorizontal size={16} strokeWidth={2} />
                  </button>
                )}
                </div>
  )
}
