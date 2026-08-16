import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { ChevronDown, ChevronRight, MoreHorizontal, Pin, Plus } from 'lucide-react'
import { useBookScope, useProjectStore, type ProjectStateDto } from '../stores/projectStore'
import { rpc } from '../rpc/client'
import { ContextMenu, type ContextMenuItem } from './ContextMenu'
import { MobileBookDraftBar } from './MobileBookDraftBar'
import { useIsPhone } from './useIsPhone'
import { InputDialog } from './InputDialog'
import { ConfirmDialog } from './ConfirmDialog'
import { ChapterDialog } from './ChapterDialog'
import { SceneDialog } from './SceneDialog'
import { StoryDateRangeDialog } from './StoryDateRangeDialog'
import { SmartListsPanel } from './SmartListsPanel'
import { BookmarksPanel } from './BookmarksPanel'
import { CollectionsPanel } from './CollectionsPanel'
import {
  BINDER_MAX,
  BINDER_MIN,
  panelWidthForShell,
  savePanelSize,
  useShellStore
} from '../stores/shellStore'
import { handleSceneClick, useSelectionStore } from '../stores/selectionStore'
import { useStageStore } from '../stores/stageStore'
import { useTargetStore } from '../stores/targetStore'
import { SceneBulkBar } from './SceneBulkBar'
import { PanelResizer } from './PanelResizer'

const STATUS_CYCLE = ['Outline', 'FirstDraft', 'Revised', 'Edited', 'Final']

/**
 * How the scenes inside a chapter are ordered.
 *
 * Reading order is the book. The rest are ways of looking for something -
 * the longest scene, the one whose title you half remember, everything still
 * at the same stage - and none of them is the order the book is in, which is
 * why dragging is off while one is active. A drag under a title sort would
 * write a reorder nobody meant.
 */
const SORT_MODES = ['order', 'title', 'words', 'stage'] as const
type SortMode = (typeof SORT_MODES)[number]

interface BinderPlotline {
  id: string
  name: string
  color: string
}

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
  | { kind: 'insertChapter'; beforeOrder: number }
  | { kind: 'chapterDescription'; chapterGuid: string; current: string }

interface ArchivedScene {
  id: string
  title: string
  wordCount: number
  /** The chapter it left, by title. Empty when that chapter is gone. */
  originChapterTitle: string
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
  const preferredBinderWidth = useShellStore((s) => s.binderWidth)
  const shellWidth = useShellStore((s) => s.shellWidth)
  const binderWidth = panelWidthForShell(
    preferredBinderWidth,
    shellWidth,
    BINDER_MIN,
    BINDER_MAX
  )
  const setBinderWidth = useShellStore((s) => s.setBinderWidth)
  const projectPath = useProjectStore((s) => s.projectPath)
  const bookScope = useBookScope()
  const [changedIds, setChangedIds] = useState<Set<string>>(new Set())
  const selectedIds = useSelectionStore((s) => s.sceneIds)
  const stages = useStageStore((s) => s.stages)
  // The binder shows the book by default. A writer looking for something they
  // parked has to be able to ask for it, and to see both at once while deciding.
  const [sceneFilter, setSceneFilter] = useState<'active' | 'all' | 'inactive'>('active')
  const [sortMode, setSortMode] = useState<SortMode>('order')
  const [plotlineFilter, setPlotlineFilter] = useState('')
  const [plotlines, setPlotlines] = useState<BinderPlotline[]>([])
  const [pinnedOpen, setPinnedOpen] = useState(true)

  const targets = useTargetStore((s) => s.targets)
  const [labelList, setLabelList] = useState<{ key: string; label: string; color: string }[]>([])

  // Loaded per book, not per row: the binder paints a dot for every scene.
  // Stages, targets, labels and plotlines all belong to the active book, so
  // this has to follow a book switch and not just a project change.
  useEffect(() => {
    if (projectPath) {
      void useStageStore.getState().load()
      void useTargetStore.getState().load()
      void rpc
        .request<{ key: string; label: string; color: string }[]>('labels/list')
        .then(setLabelList)
        .catch(() => setLabelList([]))
      void rpc
        .request<BinderPlotline[]>('binder/plotlines')
        .then(setPlotlines)
        .catch(() => setPlotlines([]))
    }
  }, [bookScope])


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
  const isPhone = useIsPhone()
  /**
   * Whether the filter / sort / book-draft rows fold behind a single row.
   *
   * The question is how wide the BINDER is, not how wide the window is. On an
   * iPad the window is wide but the binder is a ~350px column, so these three
   * rows cost the same quarter of the pane they cost on a phone before the
   * first chapter - on the tab a writer opens most. The desktop keeps them
   * open, where the pane has the room and hiding them would only cost a click.
   */
  const tabletLayout = useShellStore((s) => s.mobileLayout) === 'tablet'
  const foldControls = isPhone || tabletLayout
  /** Folded layouts only: whether the filter / sort / book-draft rows show. */
  const [controlsOpen, setControlsOpen] = useState(false)
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
          // Top of the binder, whatever the sort and wherever the chapter. The
          // flag has been on the model and saved to disk for years with no way
          // for a writer to set it.
          label: scene.isFavorite ? t('binder.unpin') : t('binder.pin'),
          onClick: () => togglePin(chapter.guid, scene.id, !scene.isFavorite)
        },
        {
          // A place worth coming back to, which is a different question from
          // "which scenes match this query" - the one saved lists answer.
          label: t('bookmarks.addScene'),
          onClick: () => {
            void rpc
              .request('bookmarks/save', [
                {
                  id: '',
                  kind: 'Scene',
                  label: scene.title,
                  group: '',
                  chapterGuid: chapter.guid,
                  targetId: scene.id,
                  targetType: '',
                  anchorText: '',
                  storyDate: '',
                  order: 0
                }
              ])
              .then(() => useShellStore.getState().setBinderTab('bookmarks'))
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
        // Without this the only way to put a chapter mid-book was to append it
        // and drag it up past everything after it - a dozen drags on a long
        // book, each one a save.
        label: t('explorer.insertChapterBefore'),
        onClick: () => setPending({ kind: 'insertChapter', beforeOrder: chapter.order })
      },
      {
        label: t('explorer.insertChapterAfter'),
        onClick: () => setPending({ kind: 'insertChapter', beforeOrder: chapter.order + 1 })
      },
      {
        // What the chapter is for, in the writer's words. Distinct from the
        // subtitle, which is what a reader sees.
        label: t('explorer.chapterDescription'),
        onClick: () =>
          setPending({
            kind: 'chapterDescription',
            chapterGuid: chapter.guid,
            current: chapter.description ?? ''
          })
      },
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

  /** The scenes of one chapter, filtered and ordered as the head asks. */
  const scenesOf = (chapter: (typeof chapters)[number]): (typeof chapter.scenes) => {
    const stageOrder = new Map(stages.map((st, i) => [st.key, i]))
    const visible = chapter.scenes.filter(
      (scene) =>
        (sceneFilter === 'all' ||
          (sceneFilter === 'inactive' ? scene.inactive : !scene.inactive)) &&
        (plotlineFilter === '' || scene.plotlineIds.includes(plotlineFilter))
    )
    if (sortMode === 'order') return visible
    const sorted = [...visible]
    sorted.sort((a, b) => {
      if (sortMode === 'title') return a.title.localeCompare(b.title)
      if (sortMode === 'words') return b.wordCount - a.wordCount
      // Untriaged scenes sort last rather than first: they are the ones with
      // nothing said about them, not the ones at the earliest stage.
      const ai = a.stage ? (stageOrder.get(a.stage) ?? stages.length) : stages.length + 1
      const bi = b.stage ? (stageOrder.get(b.stage) ?? stages.length) : stages.length + 1
      return ai - bi || a.order - b.order
    })
    return sorted
  }

  /** Everything the writer pinned, in reading order, with its chapter. */
  const pinned = chapters.flatMap((chapter) =>
    chapter.scenes
      .filter((scene) => scene.isFavorite)
      .map((scene) => ({ chapter, scene }))
  )

  const togglePin = (chapterGuid: string, sceneId: string, next: boolean): void => {
    void rpc
      .request<ProjectStateDto>('binder/pinScene', [chapterGuid, sceneId, next])
      .then((state) => store.getState().applyState(state))
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
        {/* A list is a query and recomputes; a collection is eight scenes
            somebody gathered by hand, which no query describes. */}
        <button
          className={`binder-tab${binderTab === 'collections' ? ' active' : ''}`}
          onClick={() => setBinderTab('collections')}
        >
          {t('collections.section')}
        </button>
        {/* Beside saved lists because they sit next to each other in the head:
            a list is a query, a bookmark is a place. */}
        <button
          className={`binder-tab${binderTab === 'bookmarks' ? ' active' : ''}`}
          onClick={() => setBinderTab('bookmarks')}
        >
          {t('bookmarks.section')}
        </button>
      </div>
      {/* A scene taken out of the book is still in the plan, so the binder has
          to be able to show it, hide it, or show only the parked ones - which
          is the whole point of a state between keeping and archiving. */}
      {/* On a phone these three rows - the in-book filter, the sort and thread
          pickers, and the book/draft bar - came to a third of the screen before
          the first chapter, on the tab a writer opens most. Folded away, they
          are one row until asked for; the chapters get the rest. Open on the
          desktop, where the pane has the room and hiding them would only cost a
          click. */}
      {foldControls && binderTab === 'chapters' && (
        <button
          type="button"
          className="binder-controls-toggle"
          aria-expanded={controlsOpen}
          onClick={() => setControlsOpen((open) => !open)}
        >
          {controlsOpen ? <ChevronDown size={15} strokeWidth={2} /> : <ChevronRight size={15} strokeWidth={2} />}
          {t('binder.sceneFilter')}
        </button>
      )}
      {binderTab === 'chapters' && (!foldControls || controlsOpen) && (
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
      {/* Ordering and threads. Neither changes the book: one is a way of
          looking for a scene, the other a way of following one line through
          it. Reading order and every thread is where the binder starts. */}
      {binderTab === 'chapters' && chapters.length > 0 && (!foldControls || controlsOpen) && (
        <div className="binder-sort-row">
          <select
            className="binder-sort-select"
            aria-label={t('binder.sortBy')}
            title={t('binder.sortBy')}
            value={sortMode}
            onChange={(e) => setSortMode(e.target.value as SortMode)}
          >
            {SORT_MODES.map((mode) => (
              <option key={mode} value={mode}>
                {t(`binder.sort_${mode}`)}
              </option>
            ))}
          </select>
          {plotlines.length > 0 && (
            <select
              className="binder-sort-select"
              aria-label={t('binder.threadFilter')}
              title={t('binder.threadFilter')}
              value={plotlineFilter}
              onChange={(e) => setPlotlineFilter(e.target.value)}
            >
              <option value="">{t('binder.allThreads')}</option>
              {plotlines.map((line) => (
                <option key={line.id} value={line.id}>
                  {line.name}
                </option>
              ))}
            </select>
          )}
        </div>
      )}
      {isMobile && binderTab === 'chapters' && (!foldControls || controlsOpen) && <MobileBookDraftBar />}
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
        {binderTab === 'collections' && <CollectionsPanel />}
        {binderTab === 'bookmarks' && <BookmarksPanel />}
        {binderTab === 'chapters' && chapters.length === 0 && (
          <div className="binder-placeholder">{t('shell.binderEmpty')}</div>
        )}
        {/* Shown only when something is pinned. An empty "Pinned" heading is
            a permanent reminder of a feature, not a feature. */}
        {binderTab === 'chapters' && pinned.length > 0 && (
          <div className="binder-pinned">
            <button className="binder-pinned-head" onClick={() => setPinnedOpen((o) => !o)}>
              <ChevronRight
                size={13}
                strokeWidth={2}
                className={`binder-chevron${pinnedOpen ? ' open' : ''}`}
              />
              {t('binder.pinned', { count: pinned.length })}
            </button>
            {pinnedOpen &&
              pinned.map(({ chapter, scene }) => (
                <div key={`pin-${scene.id}`} className="binder-scene-wrap">
                  <button
                    className={`binder-scene-row${openSceneId === scene.id ? ' active' : ''}`}
                    onClick={() => void openScene(chapter.guid, scene.id)}
                  >
                    <Pin size={11} strokeWidth={2} className="binder-pin-icon" />
                    <span className="binder-scene-title" title={scene.title}>{scene.title}</span>
                    {/* Which chapter it came from - a pinned list with no
                        context is a list of titles floating free of the book. */}
                    <span className="binder-pin-chapter">{chapter.title}</span>
                  </button>
                </div>
              ))}
          </div>
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
            {!collapsed[chapter.guid] &&
              scenesOf(chapter).map((scene, sceneIndex) => (
                <div key={scene.id} className="binder-scene-wrap">
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
                    <span className="binder-scene-title" title={chapter.title}>
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
                  {/* The default, and what a writer restoring something almost
                      always means. Every restore used to land in chapter one
                      wherever the scene came from. */}
                  <option value="">{t('explorer.restoreHome')}</option>
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
                <span className="binder-scene-title" title={scene.title}>{scene.title}</span>
                {scene.originChapterTitle && (
                  <span className="binder-pin-chapter">{scene.originChapterTitle}</span>
                )}
                <button
                  className="snapshot-restore"
                  onClick={() => {
                    void rpc
                      .request('scenes/restoreArchived', [scene.id, restoreInto || null])
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
      {pending?.kind === 'insertChapter' && (
        <InputDialog
          title={t('explorer.insertChapterTitle')}
          placeholder={t('shell.newChapter')}
          onCancel={() => setPending(null)}
          onSubmit={(title) => {
            const at = pending.beforeOrder
            setPending(null)
            if (title.trim().length > 0) void store.getState().createChapter(title.trim(), at)
          }}
        />
      )}
      {pending?.kind === 'chapterDescription' && (
        <InputDialog
          title={t('explorer.chapterDescription')}
          placeholder={pending.current}
          onCancel={() => setPending(null)}
          onSubmit={(description) => {
            const chapterGuid = pending.chapterGuid
            setPending(null)
            void rpc
              .request<ProjectStateDto>('project/setChapterDescription', [
                chapterGuid,
                description
              ])
              .then((state) => store.getState().applyState(state))
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
