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
import { useShellStore, viewGroups, type MainView } from '../stores/shellStore'

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

  return (
    <nav className="binder">
      <div className="binder-tree">
        <div className="binder-placeholder">{t('shell.binderEmpty')}</div>
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
