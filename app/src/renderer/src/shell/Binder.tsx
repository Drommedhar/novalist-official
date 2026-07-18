import { useTranslation } from 'react-i18next'
import {
  BookOpen,
  CalendarDays,
  ChartNoAxesGantt,
  FileText,
  FolderGit2,
  Grid3x3,
  Images,
  LayoutDashboard,
  Library,
  Map,
  Network,
  NotebookPen,
  Send
} from 'lucide-react'
import { ChevronRight } from 'lucide-react'
import { useState } from 'react'
import { useShellStore, viewGroups, type MainView } from '../stores/shellStore'
import { useProjectStore } from '../stores/projectStore'

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
  git: FolderGit2
}

export function Binder(): React.JSX.Element {
  const { t } = useTranslation()
  const mainView = useShellStore((s) => s.mainView)
  const setMainView = useShellStore((s) => s.setMainView)

  const chapters = useProjectStore((s) => s.chapters)
  const openSceneId = useProjectStore((s) => s.openSceneId)
  const openScene = useProjectStore((s) => s.openScene)
  const [collapsed, setCollapsed] = useState<Record<string, boolean>>({})

  return (
    <nav className="binder">
      <div className="binder-tree">
        {chapters.length === 0 && (
          <div className="binder-placeholder">{t('shell.binderEmpty')}</div>
        )}
        {chapters.map((chapter) => (
          <div key={chapter.guid} className="binder-chapter">
            <button
              className="binder-chapter-row"
              onClick={() =>
                setCollapsed((c) => ({ ...c, [chapter.guid]: !c[chapter.guid] }))
              }
            >
              <ChevronRight
                size={13}
                strokeWidth={2}
                className={`binder-chevron${collapsed[chapter.guid] ? '' : ' open'}`}
              />
              <span
                className="binder-status-dot"
                data-status={chapter.status}
                aria-hidden="true"
              />
              <span className="binder-chapter-title">{chapter.title}</span>
            </button>
            {!collapsed[chapter.guid] &&
              chapter.scenes.map((scene) => (
                <button
                  key={scene.id}
                  className={`binder-scene-row${openSceneId === scene.id ? ' active' : ''}`}
                  onClick={() => void openScene(chapter.guid, scene.id)}
                >
                  <span className="binder-scene-title">{scene.title}</span>
                  <span className="binder-scene-words">
                    {scene.wordCount > 0 ? scene.wordCount.toLocaleString() : ''}
                  </span>
                </button>
              ))}
          </div>
        ))}
      </div>
      <div className="binder-rail">
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
    </nav>
  )
}
