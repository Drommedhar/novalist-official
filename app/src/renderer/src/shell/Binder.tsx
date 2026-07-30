import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { ChevronRight, MoreHorizontal, Plus } from 'lucide-react'
import { useProjectStore, type ProjectStateDto } from '../stores/projectStore'
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
import { handleSceneClick, useSelectionStore } from '../stores/selectionStore'
import { useStageStore } from '../stores/stageStore'
import { useTargetStore } from '../stores/targetStore'
import { SceneBulkBar } from './SceneBulkBar'
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
  | { kind: 'sceneTemplate'; chapterGuid: string; sceneId: string; title: string }
  | { kind: 'editScene'; chapterGuid: string; sceneId: string; current: string }
  | { kind: 'deleteChapter'; chapterGuid: string; title: string }
  | { kind: 'deleteScene'; chapterGuid: string; sceneId: string; title: string }
  | { kind: 'setDate'; chapterGuid: string; sceneId: string }
  | { kind: 'setAct'; chapterGuid: string; current: string }
  | { kind: 'sceneTarget'; targets: { chapterGuid: string; sceneId: string }[]; current: string }
  | { kind: 'deleteScenes'; targets: { chapterGuid: string; sceneId: string }[] }
  | { kind: 'chapterTarget'; chapterGuid: string; current: string }
  | { kind: 'actTarget'; actName: string; current: string }

interface ArchivedScene {
  id: string
  title: string
  wordCount: number
}

interface TrashedChapter {
  guid: string
  title: string
  deletedAt: string
  sceneCount: number
}

export function Binder(): React.JSX.Element {
  const { t } = useTranslation()
  const binderTab = useShellStore((s) => s.binderTab)
  const setBinderTab = useShellStore((s) => s.setBinderTab)
  const binderWidth = useShellStore((s) => s.binderWidth)
  const setBinderWidth = useShellStore((s) => s.setBinderWidth)
  const projectPath = useProjectStore((s) => s.projectPath)
  const [changedIds, setChangedIds] = useState<Set<string>>(new Set())
  const selectedIds = useSelectionStore((s) => s.sceneIds)
  const stages = useStageStore((s) => s.stages)
  // The binder shows the book by default. A writer looking for something they
  // parked has to be able to ask for it, and to see both at once while deciding.
  const [sceneFilter, setSceneFilter] = useState<'active' | 'all' | 'inactive'>('active')

  const targets = useTargetStore((s) => s.targets)
  const [labelList, setLabelList] = useState<{ key: string; label: string; color: string }[]>([])

  // Loaded per project, not per row: the binder paints a dot for every scene.
  useEffect(() => {
    if (projectPath) {
      void useStageStore.getState().load()
      void useTargetStore.getState().load()
      void rpc
        .request<{ key: string; label: string; color: string }[]>('labels/list')
        .then(setLabelList)
        .catch(() => setLabelList([]))
    }
  }, [projectPath])


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

  // Word counts move on every save, so the bars follow the chapter list rather
  // than only refreshing when a target is edited.
  useEffect(() => {
    if (projectPath) void useTargetStore.getState().load()
  }, [chapters, projectPath])

  // A deleted, archived or moved-away scene must leave the selection with it,
  // otherwise the bulk bar keeps offering to act on something that is gone.
  useEffect(() => {
    useSelectionStore
      .getState()
      .prune(chapters.flatMap((chapter) => chapter.scenes.map((scene) => scene.id)))
  }, [chapters])

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

  /** Dragging a scene that is part of the selection carries the whole selection;
   *  dragging one outside it moves only that scene. */
  const dragPayload = (sceneId: string): string[] => {
    const selection = useSelectionStore.getState().sceneIds
    return selection.includes(sceneId) ? selection : [sceneId]
  }

  const onChapterDrop = (target: { guid: string; order: number }): void => {
    if (!drag) return
    if (drag.kind === 'chapter' && drag.chapterGuid !== target.guid) {
      void store.getState().reorderChapter(drag.chapterGuid, target.order)
    } else if (drag.kind === 'scene' && drag.chapterGuid !== target.guid) {
      void store.getState().moveScenes(dragPayload(drag.sceneId), target.guid, 0)
    }
    setDrag(null)
  }

  const onSceneDrop = (chapterGuid: string, target: { id: string; order: number }, index: number): void => {
    if (!drag || drag.kind !== 'scene') return
    if (drag.sceneId === target.id) { setDrag(null); return }
    const payload = dragPayload(drag.sceneId)
    // Reordering within a chapter moves one scene at a time; a multi-scene drag
    // goes through the cross-chapter move, which inserts them as a block.
    if (drag.chapterGuid === chapterGuid && payload.length === 1) {
      void store.getState().reorderScene(chapterGuid, drag.sceneId, target.order)
    } else {
      void store.getState().moveScenes(payload, chapterGuid, index)
    }
    setDrag(null)
  }
  const [menu, setMenu] = useState<MenuState | null>(null)
  const [archived, setArchived] = useState<ArchivedScene[] | null>(null)
  const [trashed, setTrashed] = useState<TrashedChapter[]>([])
  // Where a restored scene lands. Empty means the first chapter, which is what
  // it always used to be - except it was that whether the writer wanted it or
  // not, with no way to say otherwise.
  const [restoreInto, setRestoreInto] = useState('')

  const loadArchived = async (): Promise<void> => {
    setArchived(await rpc.request<ArchivedScene[]>('scenes/archived'))
    setTrashed(await rpc.request<TrashedChapter[]>('project/trashedChapters'))
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
      // Right-clicking inside a selection acts on all of it. Right-clicking
      // outside one replaced the selection when the menu opened, so this is
      // always the scenes the writer just pointed at.
      const labels = labelList
      const selection = useSelectionStore.getState().sceneIds
      const targets: { chapterGuid: string; sceneId: string }[] =
        selection.length > 1 && selection.includes(scene.id)
          ? chapters.flatMap((c) =>
              c.scenes
                .filter((sc) => selection.includes(sc.id))
                .map((sc) => ({ chapterGuid: c.guid, sceneId: sc.id }))
            )
          : [{ chapterGuid: chapter.guid, sceneId: scene.id }]

      /** Appends "(N scenes)" so a menu row never silently does more than it says. */
      const scoped = (label: string): string =>
        targets.length > 1 ? `${label} (${t('bulk.scopeCount', { count: targets.length })})` : label

      // One entry per label the book defines, plus a way back to none.
      const labelItems: ContextMenuItem[] = [
        ...labels.map((label) => ({
          label: scoped(`${t('labels.set')}: ${label.label}`),
          onClick: () => {
            void (async () => {
              for (const target of targets)
                await rpc.request('labels/setScene', [target.sceneId, label.key])
              useProjectStore
                .getState()
                .applyState(await rpc.request<ProjectStateDto>('project/getState'))
            })()
          }
        })),
        ...(labels.length > 0
          ? [
              {
                label: scoped(t('labels.none')),
                onClick: () => {
                  void (async () => {
                    for (const target of targets)
                      await rpc.request('labels/setScene', [target.sceneId, null])
                    useProjectStore
                      .getState()
                      .applyState(await rpc.request<ProjectStateDto>('project/getState'))
                  })()
                }
              }
            ]
          : [])
      ]

      // One entry per stage, plus a way back to untriaged. A submenu would be
      // better, but ContextMenu is a flat list and prefixing keeps it readable.
      const stageItems: ContextMenuItem[] = useStageStore.getState().stages.map((stage) => ({
        label: scoped(`${t('stages.setTo')}: ${stage.label}`),
        onClick: () => {
          void (async () => {
            for (const target of targets) {
              await useStageStore
                .getState()
                .setSceneStage(target.chapterGuid, target.sceneId, stage.key)
            }
          })()
        }
      }))
      if (targets.some((target) => chapters.some((c) =>
        c.scenes.some((sc) => sc.id === target.sceneId && sc.stage))))
      {
        stageItems.push({
          label: scoped(t('stages.clear')),
          onClick: () => {
            void (async () => {
              for (const target of targets) {
                await useStageStore
                  .getState()
                  .setSceneStage(target.chapterGuid, target.sceneId, null)
              }
            })()
          }
        })
      }

      return [
        ...sceneMoves,
        ...labelItems,
        ...stageItems,
        // Only where there is a next scene to merge into this one.
        ...(sceneIndex < chapter.scenes.length - 1
          ? [
              {
                label: t('splitMerge.mergeWithNext', {
                  title: chapter.scenes[sceneIndex + 1].title
                }),
                onClick: () => {
                  const next = chapter.scenes[sceneIndex + 1]
                  void rpc
                    .request<{
                      sceneId: string | null
                      state: import('../stores/projectStore').ProjectStateDto
                    }>('sceneSplit/merge', [chapter.guid, scene.id, next.id])
                    .then((result) => {
                      store.getState().applyState(result.state)
                      // The merged scene grew, so whatever the editor is
                      // showing of it is now short.
                      void store.getState().openScene(chapter.guid, scene.id)
                    })
                }
              }
            ]
          : []),
        {
          label: scoped(t('targets.setScene')),
          onClick: () =>
            setPending({
              kind: 'sceneTarget',
              targets,
              current: String(useTargetStore.getState().find('scene', scene.id)?.target ?? '')
            })
        },
        {
          // Reads as what it does to the book, not as a field being set: the
          // scene stays in the binder either way.
          label: scoped(
            scene.excludeFromExport ? t('export.includeScene') : t('export.excludeScene')
          ),
          onClick: () => {
            void rpc
              .request<{ state: import('../stores/projectStore').ProjectStateDto }>(
                'sceneBulk/setExportInclusion',
                [targets.map((target) => target.sceneId), scene.excludeFromExport]
              )
              .then((result) => store.getState().applyState(result.state))
          }
        },
        {
          // Out of the book, still in the plan. The step between keeping a
          // scene and archiving it, which until now was the only way down.
          label: scoped(scene.inactive ? t('scene.makeActive') : t('scene.makeInactive')),
          onClick: () => {
            void Promise.all(
              targets.map((target) =>
                rpc.request('scenes/setInactive', [
                  target.chapterGuid,
                  target.sceneId,
                  !scene.inactive
                ])
              )
            )
              .then(() => rpc.request<ProjectStateDto>('project/getState'))
              .then((state) => store.getState().applyState(state))
          }
        },
        {
          // Made from a scene that already reads right, rather than described
          // in a form: pointing at one is easier than writing down what it is.
          label: t('explorer.saveAsTemplate'),
          onClick: () =>
            setPending({
              kind: 'sceneTemplate',
              chapterGuid: chapter.guid,
              sceneId: scene.id,
              title: scene.title
            })
        },
        {
          label: scoped(t('explorer.contextArchive')),
          onClick: () => {
            void rpc
              .request('sceneBulk/archive', [targets.map((target) => target.sceneId)])
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
          label: scoped(t('explorer.contextDelete')),
          danger: true,
          onClick: () =>
            setPending(
              targets.length > 1
                ? { kind: 'deleteScenes', targets }
                : {
                    kind: 'deleteScene',
                    chapterGuid: chapter.guid,
                    sceneId: scene.id,
                    title: scene.title
                  }
            )
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
        label: t('targets.setChapter'),
        onClick: () =>
          setPending({
            kind: 'chapterTarget',
            chapterGuid: chapter.guid,
            current: String(
              useTargetStore.getState().find('chapter', chapter.guid)?.explicit
                ? (useTargetStore.getState().find('chapter', chapter.guid)?.target ?? '')
                : ''
            )
          })
      },
      // Only offered where an act exists to set it on.
      ...(chapter.act
        ? [
            {
              label: t('targets.setAct', { act: chapter.act }),
              onClick: () =>
                setPending({
                  kind: 'actTarget',
                  actName: chapter.act,
                  current: String(
                    useTargetStore.getState().find('act', chapter.act)?.explicit
                      ? (useTargetStore.getState().find('act', chapter.act)?.target ?? '')
                      : ''
                  )
                })
            }
          ]
        : []),
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
      {/* A scene taken out of the book is still in the plan, so the binder has
          to be able to show it, hide it, or show only the parked ones - which
          is the whole point of a state between keeping and archiving. */}
      {binderTab === 'chapters' && (
        <div className="binder-scene-filter">
          {(['active', 'all', 'inactive'] as const).map((mode) => (
            <button
              key={mode}
              className={`binder-filter-chip${sceneFilter === mode ? ' active' : ''}`}
              aria-pressed={sceneFilter === mode}
              title={t('binder.sceneFilter')}
              onClick={() => setSceneFilter(mode)}
            >
              {t(
                mode === 'active'
                  ? 'binder.showActive'
                  : mode === 'all'
                    ? 'binder.showAll'
                    : 'binder.showInactive'
              )}
            </button>
          ))}
        </div>
      )}
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
              chapter.scenes
                .filter(
                  (scene) =>
                    sceneFilter === 'all' ||
                    (sceneFilter === 'inactive' ? scene.inactive : !scene.inactive)
                )
                .map((scene, sceneIndex) => (
                <div key={scene.id} className="binder-scene-wrap">
                <button
                  className={`binder-scene-row${openSceneId === scene.id ? ' active' : ''}${
                    changedIds.has(scene.id) ? ' changed' : ''
                  }${selectedIds.includes(scene.id) ? ' selected' : ''}${
                    scene.inactive ? ' inactive' : ''
                  }`}
                  draggable
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
                  <span className="binder-scene-title">{scene.title}</span>
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
            {archived !== null && trashed.length > 0 && (
              <div className="binder-trash-chapters">
                {trashed.map((chapter) => (
                  <div key={chapter.guid} className="binder-scene-row">
                    <span className="binder-scene-title">
                      {chapter.title}
                      <span className="binder-trash-meta">
                        {' '}
                        {t('explorer.trashScenes', { count: chapter.sceneCount })}
                      </span>
                    </span>
                    <button
                      className="snapshot-restore"
                      onClick={() => {
                        void rpc
                          .request<import('../stores/projectStore').ProjectStateDto>(
                            'project/restoreChapter',
                            [chapter.guid]
                          )
                          .then((state) => {
                            store.getState().applyState(state)
                            void loadArchived()
                          })
                      }}
                    >
                      {t('snapshots.restore')}
                    </button>
                    <button
                      className="snapshot-restore"
                      onClick={() => {
                        // The only action in the binder that destroys anything.
                        if (!window.confirm(t('explorer.purgeConfirm', { title: chapter.title })))
                          return
                        void rpc
                          .request('project/purgeChapter', [chapter.guid])
                          .then(() => loadArchived())
                      }}
                    >
                      {t('explorer.purge')}
                    </button>
                  </div>
                ))}
              </div>
            )}
            {archived !== null && archived.length > 0 && chapters.length > 0 && (
              <label className="binder-restore-target">
                {t('explorer.restoreInto')}
                <select
                  className="inspector-input"
                  value={restoreInto}
                  onChange={(e) => setRestoreInto(e.target.value)}
                >
                  {chapters.map((c) => (
                    <option key={c.guid} value={c.guid}>
                      {c.title}
                    </option>
                  ))}
                </select>
              </label>
            )}
            {archived?.map((scene) => (
              <div key={scene.id} className="binder-scene-row">
                <span className="binder-scene-title">{scene.title}</span>
                <button
                  className="snapshot-restore"
                  onClick={() => {
                    const target = restoreInto || chapters[0]?.guid
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
            {archived !== null && archived.length === 0 && trashed.length === 0 && (
              <div className="binder-placeholder">{t('explorer.archiveEmpty')}</div>
            )}
          </div>
        )}
      </div>
      <SceneBulkBar />
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
      {pending?.kind === 'sceneTemplate' && (
        <InputDialog
          title={t('explorer.templateNameTitle')}
          placeholder={t('explorer.templateNamePlaceholder')}
          initialValue={pending.title}
          onCancel={() => setPending(null)}
          onSubmit={(name) => {
            const target = pending
            setPending(null)
            void rpc.request('sceneTemplates/saveFromScene', [
              target.chapterGuid,
              target.sceneId,
              name
            ])
          }}
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
      {pending?.kind === 'sceneTarget' && (
        <InputDialog
          title={t('targets.prompt')}
          placeholder={pending.current}
          onCancel={() => setPending(null)}
          onSubmit={(value) => {
            const p = pending
            setPending(null)
            void (async () => {
              for (const target of p.targets) {
                await useTargetStore
                  .getState()
                  .setScene(target.chapterGuid, target.sceneId, Number(value) || null)
              }
            })()
          }}
        />
      )}
      {pending?.kind === 'chapterTarget' && (
        <InputDialog
          title={t('targets.prompt')}
          placeholder={pending.current}
          onCancel={() => setPending(null)}
          onSubmit={(value) => {
            const p = pending
            setPending(null)
            void useTargetStore.getState().setChapter(p.chapterGuid, Number(value) || null)
          }}
        />
      )}
      {pending?.kind === 'actTarget' && (
        <InputDialog
          title={t('targets.prompt')}
          placeholder={pending.current}
          onCancel={() => setPending(null)}
          onSubmit={(value) => {
            const p = pending
            setPending(null)
            void useTargetStore.getState().setAct(p.actName, Number(value) || null)
          }}
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
      {pending?.kind === 'deleteScenes' && (
        <ConfirmDialog
          title={t('explorer.deleteTitle')}
          message={t('bulk.confirmDelete', { count: pending.targets.length })}
          onCancel={() => setPending(null)}
          onConfirm={() => {
            const ids = pending.targets.map((target) => target.sceneId)
            setPending(null)
            void rpc
              .request<{ state: import('../stores/projectStore').ProjectStateDto }>(
                'sceneBulk/delete',
                [ids]
              )
              .then((result) => store.getState().applyState(result.state))
          }}
        />
      )}
    </nav>
  )
}
