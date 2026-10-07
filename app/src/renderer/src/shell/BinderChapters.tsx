import { ChevronRight, MoreHorizontal, Plus } from 'lucide-react'

import { BinderSceneRow } from './BinderSceneRow'

import type { BinderState } from './binderState'
import { MotionPresence } from './MotionPresence'

export function BinderChapters({ state }: { state: BinderState }): React.JSX.Element {
  const { t, binderTab, isMobile, setAddSceneChapter, collapsed, setCollapsed, setDrag, setMenu, chapters, onChapterDrop, scenesOf, cycleStatus } = state
  return <>
        {binderTab === 'chapters' &&
          chapters.map((chapter, index) => (
          <div key={chapter.guid} className="binder-chapter">
            {chapter.act && chapters[index - 1]?.act !== chapter.act && (
              <div className="binder-act">{chapter.act}</div>
            )}
            <div
              className="binder-chapter-row"
              draggable
              onDragStart={() => setDrag({ kind: 'chapter', chapterGuid: chapter.guid })}
              onDragOver={(e) => e.preventDefault()}
              onDrop={() => onChapterDrop({ guid: chapter.guid, order: chapter.order })}
              onContextMenu={(e) => {
                e.preventDefault()
                setMenu({ x: e.clientX, y: e.clientY, chapterGuid: chapter.guid, sceneId: null })
              }}
            >
              <button
                className="binder-expand"
                aria-label={chapter.title}
                onClick={() =>
                  setCollapsed((c) => ({ ...c, [chapter.guid]: !c[chapter.guid] }))
                }
              >
                <ChevronRight
                  size={13}
                  strokeWidth={2}
                  className={`binder-chevron${collapsed[chapter.guid] ? '' : ' open'}`}
                />
              </button>
              <button
                className="binder-status-dot"
                data-status={chapter.status}
                title={t('explorer.cycleStatusTooltip')}
                onClick={() => cycleStatus(chapter.guid, chapter.status)}
              />
              <span className="binder-chapter-title" title={chapter.title}>{chapter.title}</span>
              {isMobile && (
                <>
                  <button
                    className="binder-row-action"
                    aria-label={t('shell.newScene')}
                    onClick={(e) => {
                      e.stopPropagation()
                      setAddSceneChapter(chapter.guid)
                    }}
                  >
                    <Plus size={16} strokeWidth={2} />
                  </button>
                  <button
                    className="binder-row-action"
                    aria-label={t('shell.chapters')}
                    onClick={(e) => {
                      e.stopPropagation()
                      const r = e.currentTarget.getBoundingClientRect()
                      setMenu({ x: r.left, y: r.bottom, chapterGuid: chapter.guid, sceneId: null })
                    }}
                  >
                    <MoreHorizontal size={16} strokeWidth={2} />
                  </button>
                </>
              )}
            </div>
            <MotionPresence collapse>
            {!collapsed[chapter.guid] && <div>
              {scenesOf(chapter).map((scene, sceneIndex) => (
                <BinderSceneRow key={scene.id} state={state} chapter={chapter} scene={scene} sceneIndex={sceneIndex} />
              ))}
            </div>}
            </MotionPresence>
          </div>
        ))}
  </>
}
