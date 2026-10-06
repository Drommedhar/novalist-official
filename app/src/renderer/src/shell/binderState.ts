import { useCallback, useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'

import { useBookScope, useProjectStore, type ProjectStateDto } from '../stores/projectStore'
import { rpc } from '../rpc/client'

import { useIsPhone } from './useIsPhone'

import { BINDER_MAX, BINDER_MIN, panelWidthForShell, useShellStore } from '../stores/shellStore'
import { useSelectionStore } from '../stores/selectionStore'
import { useStageStore } from '../stores/stageStore'
import { useNarrationStore } from '../stores/narrationStore'
import { openBinderScene } from './binderNavigation'
import { useTargetStore } from '../stores/targetStore'
import { loadBookScoped } from '../stores/bookScopedLoad'

import { STATUS_CYCLE, type SortMode, type BinderPlotline, type MenuState, type PendingAction, type ArchivedScene, type TrashedChapter } from './binderTypes'

function useBinderView() {
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
  // The binder shows the book by default. A writer looking for something they
  // parked has to be able to ask for it, and to see both at once while deciding.
  const [sceneFilter, setSceneFilter] = useState<'active' | 'all' | 'inactive'>('active')
  const [sortMode, setSortMode] = useState<SortMode>('order')
  const [plotlineFilter, setPlotlineFilter] = useState('')
  const [pinnedOpen, setPinnedOpen] = useState(true)
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
  const [menu, setMenu] = useState<MenuState | null>(null)
  const [pending, setPending] = useState<PendingAction | null>(null)
  return { t, binderTab, setBinderTab, preferredBinderWidth, shellWidth, binderWidth, setBinderWidth, sceneFilter, setSceneFilter, sortMode, setSortMode, plotlineFilter, setPlotlineFilter, pinnedOpen, setPinnedOpen, isMobile, isPhone, tabletLayout, foldControls, controlsOpen, setControlsOpen, addChapterOpen, setAddChapterOpen, addSceneChapter, setAddSceneChapter, collapsed, setCollapsed, drag, setDrag, menu, setMenu, pending, setPending }
}

function useBinderBook() {
  const projectPath = useProjectStore((s) => s.projectPath)
  const bookScope = useBookScope()
  const workspaceBusy = useProjectStore((s) => s.workspaceBusy)
  const workspaceEpoch = useProjectStore((s) => s.workspaceEpoch)
  const [changedIds, setChangedIds] = useState<Set<string>>(new Set())
  const selectedIds = useSelectionStore((s) => s.sceneIds)
  const stages = useStageStore((s) => s.stages)
  const [plotlines, setPlotlines] = useState<BinderPlotline[]>([])

  const targets = useTargetStore((s) => s.targets)
  const [labelList, setLabelList] = useState<{ key: string; label: string; color: string }[]>([])
  const chapters = useProjectStore((s) => s.chapters)

  const editorSceneId = useProjectStore((s) => s.openSceneId)
  const mainView = useShellStore((s) => s.mainView)
  const narrationSceneId = useNarrationStore(
    (s) => s.selected?.sceneId ?? s.sceneNavigation?.sceneId ?? null
  )
  const openSceneId = mainView === 'narration' ? narrationSceneId : editorSceneId
  const openScene = openBinderScene
  const store = useProjectStore
  return { projectPath, bookScope, workspaceBusy, workspaceEpoch, changedIds, setChangedIds, selectedIds, stages, plotlines, setPlotlines, targets, labelList, setLabelList, chapters, editorSceneId, mainView, narrationSceneId, openSceneId, openScene, store }
}

function useBinderBookEffects(book: ReturnType<typeof useBinderBook>): void {
  const { projectPath, bookScope, workspaceBusy, setChangedIds, setPlotlines, setLabelList, chapters } = book
  // Loaded per book, not per row: the binder paints a dot for every scene.
  // Stages, targets, labels and plotlines all belong to the active book, so
  // this has to follow a book switch and not just a project change.
  useEffect(() => {
    if (projectPath && !workspaceBusy) {
      let active = true
      void useStageStore.getState().load().catch(() => {})
      void useTargetStore.getState().load().catch(() => {})
      void rpc
        .request<{ key: string; label: string; color: string }[]>('labels/list')
        .then((labels) => { if (active) setLabelList(labels) })
        .catch(() => { if (active) setLabelList([]) })
      void rpc
        .request<BinderPlotline[]>('binder/plotlines')
        .then((lines) => { if (active) setPlotlines(lines) })
        .catch(() => { if (active) setPlotlines([]) })
      return () => { active = false }
    }
  }, [bookScope, workspaceBusy])

  // Poll which scenes have uncommitted Git changes so their rows can be marked
  // in the explorer (matches the desktop change markers). Quiet no-op outside a repo.
  useEffect(() => {
    if (workspaceBusy) return
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
  }, [projectPath, workspaceBusy])

  // Word counts move on every save, so the bars follow the chapter list rather
  // than only refreshing when a target is edited.
  useEffect(() => {
    if (projectPath && !workspaceBusy) void useTargetStore.getState().load().catch(() => {})
  }, [chapters, projectPath, workspaceBusy])

  // A deleted, archived or moved-away scene must leave the selection with it,
  // otherwise the bulk bar keeps offering to act on something that is gone.
  useEffect(() => {
    useSelectionStore
      .getState()
      .prune(chapters.flatMap((chapter) => chapter.scenes.map((scene) => scene.id)))
  }, [chapters])
}

function useBinderArchive(book: ReturnType<typeof useBinderBook>) {
  const { bookScope, workspaceBusy, workspaceEpoch } = book
  const [archiveOpen, setArchiveOpen] = useState(false)
  const [archived, setArchived] = useState<ArchivedScene[]>([])
  const [trashed, setTrashed] = useState<TrashedChapter[]>([])
  // Where a restored scene lands. Empty means the first chapter, which is what
  // it always used to be - except it was that whether the writer wanted it or
  // not, with no way to say otherwise.
  const [restoreInto, setRestoreInto] = useState('')

  const loadArchived = useCallback((): Promise<void> => loadBookScoped(useProjectStore.getState,
    () => Promise.all([
      rpc.request<ArchivedScene[]>('scenes/archived'),
      rpc.request<TrashedChapter[]>('project/trashedChapters')
    ]),
    ([scenes, chapters]) => {
      setArchived(scenes)
      setTrashed(chapters)
    }), [])
  useEffect(() => {
    setArchived([])
    setTrashed([])
    setRestoreInto('')
  }, [bookScope, workspaceEpoch])
  useEffect(() => {
    if (archiveOpen && !workspaceBusy) void loadArchived().catch(() => {})
  }, [archiveOpen, bookScope, workspaceEpoch, workspaceBusy, loadArchived])
  return { archiveOpen, setArchiveOpen, archived, setArchived, trashed, setTrashed, restoreInto, setRestoreInto, loadArchived }
}

function useBinderData() {
  const view = useBinderView()
  const book = useBinderBook()
  useBinderBookEffects(book)
  return { ...view, ...book, ...useBinderArchive(book) }
}

type BinderData = ReturnType<typeof useBinderData>

function binderDragHandlers(data: BinderData) {
  const { drag, setDrag, store } = data
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
  return { dragPayload, onChapterDrop, onSceneDrop }
}

function binderDisplay(data: BinderData) {
  const { sceneFilter, sortMode, plotlineFilter, stages, chapters, store } = data
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
  return { scenesOf, pinned, togglePin, cycleStatus }
}

export function useBinderState() {
  const data = useBinderData()
  return { ...data, ...binderDragHandlers(data), ...binderDisplay(data) }
}

export type BinderState = ReturnType<typeof useBinderState>
