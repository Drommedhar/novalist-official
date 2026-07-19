import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import {
  BookOpen,
  CalendarDays,
  ChartNoAxesGantt,
  ChevronRight,
  FileText,
  FolderGit2,
  Grid3x3,
  Images,
  LayoutDashboard,
  Library,
  Map,
  Network,
  NotebookPen,
  Send,
  Settings
} from 'lucide-react'
import { useShellStore, viewGroups, type MainView } from '../stores/shellStore'
import { useProjectStore } from '../stores/projectStore'
import { rpc } from '../rpc/client'
import { useExtensionsStore } from '../stores/extensionsStore'
import { ContextMenu, type ContextMenuItem } from './ContextMenu'
import { InputDialog } from './InputDialog'
import { ConfirmDialog } from './ConfirmDialog'
import { ChapterDialog } from './ChapterDialog'
import { SceneDialog } from './SceneDialog'
import { StoryDateRangeDialog } from './StoryDateRangeDialog'
import { SmartListsPanel } from './SmartListsPanel'

const viewIcons: Record<MainView, React.ComponentType<{ size?: number; strokeWidth?: number }>> = {
  write: NotebookPen,
  manuscript: BookOpen,
  dashboard: LayoutDashboard,
  timeline: ChartNoAxesGantt,
  plotGrid: Grid3x3,
  calendar: CalendarDays,
  relationships: Network,
  codex: Library,
  maps: Map,
  research: FileText,
  gallery: Images,
  export: Send,
  git: FolderGit2,
  settings: Settings
}

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
  const mainView = useShellStore((s) => s.mainView)
  const setMainView = useShellStore((s) => s.setMainView)
  const binderTab = useShellStore((s) => s.binderTab)
  const extView = useShellStore((s) => s.extView)
  const setExtView = useShellStore((s) => s.setExtView)
  const allExtViews = useExtensionsStore((s) => s.views)
  const extViews = allExtViews.filter((v) => v.placement === 'main')
  const setBinderTab = useShellStore((s) => s.setBinderTab)
  const chapters = useProjectStore((s) => s.chapters)
  const openSceneId = useProjectStore((s) => s.openSceneId)
  const openScene = useProjectStore((s) => s.openScene)
  const store = useProjectStore
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
      return [
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
    return [
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
    <nav className="binder">
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
            </div>
            {!collapsed[chapter.guid] &&
              chapter.scenes.map((scene, sceneIndex) => (
                <button
                  key={scene.id}
                  className={`binder-scene-row${openSceneId === scene.id ? ' active' : ''}`}
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
                >
                  <span className="binder-scene-title">{scene.title}</span>
                  <span className="binder-scene-words">
                    {scene.wordCount > 0 ? scene.wordCount.toLocaleString() : ''}
                  </span>
                </button>
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
      <div className="binder-rail">
        {extViews.length > 0 && (
          <div className="binder-group">
            <div className="binder-group-label">{t('extensions.title')}</div>
            {extViews.map((view) => (
              <button
                key={`${view.extensionId}|${view.key}`}
                className={`binder-rail-item${
                  extView?.key === view.key && extView.extensionId === view.extensionId
                    ? ' active'
                    : ''
                }`}
                onClick={() => setExtView({ extensionId: view.extensionId, key: view.key })}
              >
                <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.75">
                  <path d={view.iconPath || 'M12 2 2 7l10 5 10-5-10-5zM2 17l10 5 10-5M2 12l10 5 10-5'} />
                </svg>
                {view.title}
              </button>
            ))}
          </div>
        )}
        {viewGroups.map((group) => (
          <div key={group.key} className="binder-group">
            <div className="binder-group-label">{t(group.key)}</div>
            {group.views.map((view) => {
              const Icon = viewIcons[view]
              return (
                <button
                  key={view}
                  className={`binder-rail-item${mainView === view ? ' active' : ''}`}
                  onClick={() => setMainView(view)}
                >
                  <Icon size={15} strokeWidth={1.75} />
                  {t(`shell.view.${view}`)}
                </button>
              )
            })}
          </div>
        ))}
      </div>
      {menu && <ContextMenu x={menu.x} y={menu.y} items={menuItems()} onClose={() => setMenu(null)} />}
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
