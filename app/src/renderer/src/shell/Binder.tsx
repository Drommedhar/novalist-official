import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { ChevronRight, MoreHorizontal, Plus } from 'lucide-react'
import { useProjectStore } from '../stores/projectStore'
import { rpc } from '../rpc/client'
import { ContextMenu, type ContextMenuItem } from './ContextMenu'
import { MobileBookDraftBar } from './MobileBookDraftBar'
import { InputDialog } from './InputDialog'
import { ConfirmDialog } from './ConfirmDialog'
import { ChapterDialog } from './ChapterDialog'
import { SceneDialog } from './SceneDialog'
import { StoryDateRangeDialog } from './StoryDateRangeDialog'
import { SmartListsPanel } from './SmartListsPanel'
import { savePanelSize, useShellStore } from '../stores/shellStore'
import { PanelResizer } from './PanelResizer'

const STATUS_CYCLE = ['Outline', 'FirstDraft', 'Revised', 'Edited', 'Final']

interface MenuState {
  x: number
  y: number
  chapterGuid: string
  sceneId: string | null
}

type PendingAction =
  | { kind: 'editChapter'; chapterGuid: string }
  | { kind: 'editScene'; chapterGuid: string; sceneId: string; current: string }
  | { kind: 'deleteChapter'; chapterGuid: string; title: string }
  | { kind: 'deleteScene'; chapterGuid: string; sceneId: string; title: string }
  | { kind: 'setDate'; chapterGuid: string; sceneId: string }
  | { kind: 'setAct'; chapterGuid: string; current: string }

interface ArchivedScene {
  id: string
  title: string
  wordCount: number
}

export function Binder(): React.JSX.Element {
  const { t } = useTranslation()
  const binderTab = useShellStore((s) => s.binderTab)
  const setBinderTab = useShellStore((s) => s.setBinderTab)
  const binderWidth = useShellStore((s) => s.binderWidth)
  const setBinderWidth = useShellStore((s) => s.setBinderWidth)
  const projectPath = useProjectStore((s) => s.projectPath)
  const [changedIds, setChangedIds] = useState<Set<string>>(new Set())

  // Poll which scenes have uncommitted Git changes so their rows can be marked
  // in the explorer (matches the desktop change markers). Quiet no-op outside a repo.
  useEffect(() => {
    let active = true
    const load = (): void => {
      void rpc
        .request<string[]>('git/changedScenes')
        .then((ids) => {
          if (active) setChangedIds(new Set(ids))
        })
        .catch(() => {})
    }
    load()
    const id = window.setInterval(load, 12000)
    return () => {
      active = false
      window.clearInterval(id)
    }
  }, [projectPath])
  const chapters = useProjectStore((s) => s.chapters)
  const openSceneId = useProjectStore((s) => s.openSceneId)
  const openScene = useProjectStore((s) => s.openScene)
  const store = useProjectStore
  // Touch has no right-click/hover, so mobile surfaces add + row-menu buttons
  // (which reuse the same dialogs and context menu as the desktop).
  const isMobile = window.novalist.isMobile === true
  const [addChapterOpen, setAddChapterOpen] = useState(false)
  const [addSceneChapter, setAddSceneChapter] = useState<string | null>(null)
  const [collapsed, setCollapsed] = useState<Record<string, boolean>>({})
  const [drag, setDrag] = useState<
    | { kind: 'chapter'; chapterGuid: string }
    | { kind: 'scene'; chapterGuid: string; sceneId: string }
    | null
  >(null)

  const onChapterDrop = (target: { guid: string; order: number }): void => {
    if (!drag) return
    if (drag.kind === 'chapter' && drag.chapterGuid !== target.guid) {
      void store.getState().reorderChapter(drag.chapterGuid, target.order)
    } else if (drag.kind === 'scene' && drag.chapterGuid !== target.guid) {
      void store.getState().moveScenes([drag.sceneId], target.guid, 0)
    }
    setDrag(null)
  }

  const onSceneDrop = (chapterGuid: string, target: { id: string; order: number }, index: number): void => {
    if (!drag || drag.kind !== 'scene') return
    if (drag.sceneId === target.id) { setDrag(null); return }
    if (drag.chapterGuid === chapterGuid) {
      void store.getState().reorderScene(chapterGuid, drag.sceneId, target.order)
    } else {
      void store.getState().moveScenes([drag.sceneId], chapterGuid, index)
    }
    setDrag(null)
  }
  const [menu, setMenu] = useState<MenuState | null>(null)
  const [archived, setArchived] = useState<ArchivedScene[] | null>(null)

  const loadArchived = async (): Promise<void> => {
    setArchived(await rpc.request<ArchivedScene[]>('scenes/archived'))
  }
  const [pending, setPending] = useState<PendingAction | null>(null)

  const menuItems = (): ContextMenuItem[] => {
    if (!menu) return []
    const chapter = chapters.find((c) => c.guid === menu.chapterGuid)
    if (!chapter) return []
    if (menu.sceneId) {
      const scene = chapter.scenes.find((s) => s.id === menu.sceneId)
      if (!scene) return []
      // Touch has no drag-reorder, so mobile gets explicit Move up/down entries
      // (using the neighbour's order, matching the desktop drag semantics).
      const sceneIndex = chapter.scenes.findIndex((s) => s.id === scene.id)
      const sceneMoves: ContextMenuItem[] = isMobile
        ? [
            ...(sceneIndex > 0
              ? [
                  {
                    label: t('explorer.moveUp'),
                    onClick: () =>
                      void store
                        .getState()
                        .reorderScene(chapter.guid, scene.id, chapter.scenes[sceneIndex - 1].order)
                  }
                ]
              : []),
            ...(sceneIndex < chapter.scenes.length - 1
              ? [
                  {
                    label: t('explorer.moveDown'),
                    onClick: () =>
                      void store
                        .getState()
                        .reorderScene(chapter.guid, scene.id, chapter.scenes[sceneIndex + 1].order)
                  }
                ]
              : [])
          ]
        : []
      return [
        ...sceneMoves,
        {
          label: t('explorer.contextArchive'),
          onClick: () => {
            void rpc
              .request('scenes/archive', [chapter.guid, scene.id])
              .then(async () => {
                const state = await rpc.request<import('../stores/projectStore').ProjectStateDto>(
                  'project/getState'
                )
                store.getState().applyState(state)
                if (archived !== null) void loadArchived()
              })
          }
        },
        {
          label: t('menu.toggleSplitEditor'),
          onClick: () => void store.getState().openSceneInSplit(chapter.guid, scene.id)
        },
        {
          label: t('explorer.renameScene'),
          onClick: () =>
            setPending({
              kind: 'editScene',
              chapterGuid: chapter.guid,
              sceneId: scene.id,
              current: scene.title
            })
        },
        {
          label: t('explorer.contextSetDate'),
          onClick: () =>
            setPending({ kind: 'setDate', chapterGuid: chapter.guid, sceneId: scene.id })
        },
        {
          label: t('explorer.contextDelete'),
          danger: true,
          onClick: () =>
            setPending({
              kind: 'deleteScene',
              chapterGuid: chapter.guid,
              sceneId: scene.id,
              title: scene.title
            })
        }
      ]
    }
    const chapterIndex = chapters.findIndex((c) => c.guid === chapter.guid)
    const chapterMoves: ContextMenuItem[] = isMobile
      ? [
          ...(chapterIndex > 0
            ? [
                {
                  label: t('explorer.moveUp'),
                  onClick: () =>
                    void store
                      .getState()
                      .reorderChapter(chapter.guid, chapters[chapterIndex - 1].order)
                }
              ]
            : []),
          ...(chapterIndex < chapters.length - 1
            ? [
                {
                  label: t('explorer.moveDown'),
                  onClick: () =>
                    void store
                      .getState()
                      .reorderChapter(chapter.guid, chapters[chapterIndex + 1].order)
                }
              ]
            : [])
        ]
      : []
    return [
      ...chapterMoves,
      {
        label: t('explorer.renameAct'),
        onClick: () =>
          setPending({ kind: 'setAct', chapterGuid: chapter.guid, current: chapter.act })
      },
      {
        label: t('explorer.renameChapter'),
        onClick: () => setPending({ kind: 'editChapter', chapterGuid: chapter.guid })
      },
      {
        label: t('explorer.contextDelete'),
        danger: true,
        onClick: () =>
          setPending({ kind: 'deleteChapter', chapterGuid: chapter.guid, title: chapter.title })
      }
    ]
  }

  const cycleStatus = (chapterGuid: string, current: string): void => {
    const next = STATUS_CYCLE[(STATUS_CYCLE.indexOf(current) + 1) % STATUS_CYCLE.length]
    void store.getState().setChapterStatus(chapterGuid, next)
  }

  return (
    <nav className="binder" style={{ width: binderWidth }}>
      <PanelResizer
        edge="right"
        width={binderWidth}
        onResize={setBinderWidth}
        onResizeEnd={(px) => savePanelSize({ binderWidth: px })}
      />
      <div className="binder-tabs">
        <button
          className={`binder-tab${binderTab === 'chapters' ? ' active' : ''}`}
          onClick={() => setBinderTab('chapters')}
        >
          {t('shell.chapters')}
        </button>
        <button
          className={`binder-tab${binderTab === 'smartLists' ? ' active' : ''}`}
          onClick={() => setBinderTab('smartLists')}
        >
          {t('smartList.section')}
        </button>
      </div>
      {isMobile && binderTab === 'chapters' && <MobileBookDraftBar />}
      {isMobile && binderTab === 'chapters' && (
        <div className="binder-mobile-actions">
          <button className="binder-mobile-add" onClick={() => setAddChapterOpen(true)}>
            <Plus size={15} strokeWidth={2} />
            {t('shell.newChapter')}
          </button>
        </div>
      )}
      <div className="binder-tree">
        {binderTab === 'smartLists' && <SmartListsPanel />}
        {binderTab === 'chapters' && chapters.length === 0 && (
          <div className="binder-placeholder">{t('shell.binderEmpty')}</div>
        )}
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
              <span className="binder-chapter-title">{chapter.title}</span>
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
            {!collapsed[chapter.guid] &&
              chapter.scenes.map((scene, sceneIndex) => (
                <div key={scene.id} className="binder-scene-wrap">
                <button
                  className={`binder-scene-row${openSceneId === scene.id ? ' active' : ''}${
                    changedIds.has(scene.id) ? ' changed' : ''
                  }`}
                  draggable
                  onDragStart={() =>
                    setDrag({ kind: 'scene', chapterGuid: chapter.guid, sceneId: scene.id })
                  }
                  onDragOver={(e) => e.preventDefault()}
                  onDrop={() => onSceneDrop(chapter.guid, { id: scene.id, order: scene.order }, sceneIndex)}
                  onClick={() => void openScene(chapter.guid, scene.id)}
                  onContextMenu={(e) => {
                    e.preventDefault()
                    setMenu({
                      x: e.clientX,
                      y: e.clientY,
                      chapterGuid: chapter.guid,
                      sceneId: scene.id
                    })
                  }}
                  title={changedIds.has(scene.id) ? t('explorer.changed') : undefined}
                >
                  <span className="binder-scene-title">{scene.title}</span>
                  <span className="binder-scene-words">
                    {scene.wordCount > 0 ? scene.wordCount.toLocaleString() : ''}
                  </span>
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
              ))}
          </div>
        ))}
        {binderTab === 'chapters' && (
          <div className="binder-archived">
            <button
              className="binder-group-label binder-archived-toggle"
              onClick={() => (archived === null ? void loadArchived() : setArchived(null))}
            >
              {t('explorer.archive')}
            </button>
            {archived?.map((scene) => (
              <div key={scene.id} className="binder-scene-row">
                <span className="binder-scene-title">{scene.title}</span>
                <button
                  className="snapshot-restore"
                  onClick={() => {
                    const target = chapters[0]?.guid
                    if (!target) return
                    void rpc
                      .request('scenes/restoreArchived', [scene.id, target])
                      .then(async () => {
                        const state = await rpc.request<
                          import('../stores/projectStore').ProjectStateDto
                        >('project/getState')
                        store.getState().applyState(state)
                        void loadArchived()
                      })
                  }}
                >
                  {t('snapshots.restore')}
                </button>
              </div>
            ))}
            {archived !== null && archived.length === 0 && (
              <div className="binder-placeholder">{t('explorer.archiveEmpty')}</div>
            )}
          </div>
        )}
      </div>
      {menu && <ContextMenu x={menu.x} y={menu.y} items={menuItems()} onClose={() => setMenu(null)} />}
      {addChapterOpen && <ChapterDialog onClose={() => setAddChapterOpen(false)} />}
      {addSceneChapter && (
        <SceneDialog
          defaultChapterGuid={addSceneChapter}
          onClose={() => setAddSceneChapter(null)}
        />
      )}
      {pending?.kind === 'editChapter' &&
        chapters.some((c) => c.guid === pending.chapterGuid) && (
          <ChapterDialog
            chapter={chapters.find((c) => c.guid === pending.chapterGuid)}
            onClose={() => setPending(null)}
          />
        )}
      {pending?.kind === 'editScene' && (
        <SceneDialog
          edit={{
            chapterGuid: pending.chapterGuid,
            sceneId: pending.sceneId,
            title: pending.current
          }}
          onClose={() => setPending(null)}
        />
      )}
      {pending?.kind === 'setDate' && (
        <StoryDateRangeDialog
          chapterGuid={pending.chapterGuid}
          sceneId={pending.sceneId}
          onClose={() => setPending(null)}
        />
      )}
      {pending?.kind === 'setAct' && (
        <InputDialog
          title={t('explorer.renameAct')}
          placeholder={pending.current}
          onCancel={() => setPending(null)}
          onSubmit={(act) => {
            const chapterGuid = pending.chapterGuid
            setPending(null)
            void rpc
              .request<import('../stores/projectStore').ProjectStateDto>('project/setChapterAct', [
                chapterGuid,
                act
              ])
              .then((state) => store.getState().applyState(state))
          }}
        />
      )}
      {pending?.kind === 'deleteChapter' && (
        <ConfirmDialog
          title={t('explorer.deleteTitle')}
          message={t('explorer.confirmDeleteChapter', { name: pending.title })}
          onCancel={() => setPending(null)}
          onConfirm={() => {
            setPending(null)
            void store.getState().deleteChapter(pending.chapterGuid)
          }}
        />
      )}
      {pending?.kind === 'deleteScene' && (
        <ConfirmDialog
          title={t('explorer.deleteTitle')}
          message={t('explorer.confirmDeleteScene', { name: pending.title })}
          onCancel={() => setPending(null)}
          onConfirm={() => {
            setPending(null)
            void store.getState().deleteScene(pending.chapterGuid, pending.sceneId)
          }}
        />
      )}
    </nav>
  )
}
