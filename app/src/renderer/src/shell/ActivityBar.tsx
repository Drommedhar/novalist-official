import { useTranslation } from 'react-i18next'
import {
  BookOpen,
  Blocks,
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
  Send,
  Settings
} from 'lucide-react'
import { activityGroups, useShellStore, type MainView } from '../stores/shellStore'
import { useExtensionsStore } from '../stores/extensionsStore'

/**
 * Slim icon-only activity bar (the left-most 44px rail), mirroring the desktop
 * MainWindow activity bar. This is the top-level view switcher; the binder to
 * its right is only the chapter/scene tree. The editor ("write") is reached by
 * opening a scene, so it has no rail button.
 */

type IconComponent = React.ComponentType<{ size?: number; strokeWidth?: number }>

const viewIcons: Partial<Record<MainView, IconComponent>> = {
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

export function ActivityBar(): React.JSX.Element {
  const { t } = useTranslation()
  const mainView = useShellStore((s) => s.mainView)
  const extView = useShellStore((s) => s.extView)
  const setMainView = useShellStore((s) => s.setMainView)
  const setExtView = useShellStore((s) => s.setExtView)
  const extViews = useExtensionsStore((s) => s.views).filter((v) => v.placement === 'main')

  return (
    <nav className="activity-bar" aria-label={t('shell.activityBar')}>
      <div className="activity-bar-top">
        {activityGroups.map((group, groupIndex) => (
          <div key={group.key} className="activity-bar-group">
            {groupIndex > 0 && <div className="activity-bar-sep" />}
            {group.views.map((view) => {
              const Icon = viewIcons[view]
              if (!Icon) return null
              const active = !extView && mainView === view
              return (
                <button
                  key={view}
                  type="button"
                  className={`activity-bar-item${active ? ' active' : ''}`}
                  data-tip={t(`shell.view.${view}`)}
                  aria-label={t(`shell.view.${view}`)}
                  aria-current={active ? 'page' : undefined}
                  onClick={() => setMainView(view)}
                >
                  <Icon size={19} strokeWidth={1.75} />
                </button>
              )
            })}
          </div>
        ))}
        {extViews.length > 0 && (
          <div className="activity-bar-group">
            <div className="activity-bar-sep" />
            {extViews.map((view) => {
              const active =
                extView?.key === view.key && extView.extensionId === view.extensionId
              return (
                <button
                  key={`${view.extensionId}|${view.key}`}
                  type="button"
                  className={`activity-bar-item${active ? ' active' : ''}`}
                  data-tip={view.title}
                  aria-label={view.title}
                  aria-current={active ? 'page' : undefined}
                  onClick={() => setExtView({ extensionId: view.extensionId, key: view.key })}
                >
                  <svg
                    width="19"
                    height="19"
                    viewBox="0 0 24 24"
                    fill="none"
                    stroke="currentColor"
                    strokeWidth="1.75"
                  >
                    <path
                      d={
                        view.iconPath ||
                        'M12 2 2 7l10 5 10-5-10-5zM2 17l10 5 10-5M2 12l10 5 10-5'
                      }
                    />
                  </svg>
                </button>
              )
            })}
          </div>
        )}
      </div>
      <div className="activity-bar-bottom">
        <button
          type="button"
          className={`activity-bar-item${!extView && mainView === 'extensions' ? ' active' : ''}`}
          data-tip={t('extensions.title')}
          aria-label={t('extensions.title')}
          aria-current={!extView && mainView === 'extensions' ? 'page' : undefined}
          onClick={() => setMainView('extensions')}
        >
          <Blocks size={19} strokeWidth={1.75} />
        </button>
        <button
          type="button"
          className={`activity-bar-item${!extView && mainView === 'settings' ? ' active' : ''}`}
          data-tip={t('shell.view.settings')}
          aria-label={t('shell.view.settings')}
          aria-current={!extView && mainView === 'settings' ? 'page' : undefined}
          onClick={() => setMainView('settings')}
        >
          <Settings size={19} strokeWidth={1.75} />
        </button>
      </div>
    </nav>
  )
}
