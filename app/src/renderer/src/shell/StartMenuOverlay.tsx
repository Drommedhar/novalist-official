import { useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { BookOpen, FilePlus2, FolderOpen, Import, Plug, Settings } from 'lucide-react'
import { useProjectStore } from '../stores/projectStore'
import { useShellStore } from '../stores/shellStore'
import { CreateProjectDialog } from './CreateProjectDialog'
import { ImportPluginDialog } from './ImportPluginDialog'
import { ImportManuscriptDialog } from './ImportManuscriptDialog'
import './shellDialogs.css'

/**
 * Left-anchored backstage drawer reached from the burger while a project is
 * open, mirroring the desktop StartMenuOverlay: project actions, recent
 * projects, and Settings / Extensions. Slides in from the left rather than
 * appearing as a centred modal.
 */
export function StartMenuOverlay({ onClose }: { onClose(): void }): React.JSX.Element {
  const { t } = useTranslation()
  const recentProjects = useProjectStore((s) => s.recentProjects)
  const openProject = useProjectStore((s) => s.openProject)
  const pickAndOpenProject = useProjectStore((s) => s.pickAndOpenProject)
  const projectPath = useProjectStore((s) => s.projectPath)
  const backendVersion = useShellStore((s) => s.backendVersion)
  const [createOpen, setCreateOpen] = useState(false)
  const [importOpen, setImportOpen] = useState(false)
  const [manuscriptImportOpen, setManuscriptImportOpen] = useState(false)
  const initialPath = useRef(projectPath)

  useEffect(() => void useProjectStore.getState().loadRecents(), [])

  // Loading/creating/importing a project changes projectPath; close the overlay
  // once that happens so we do not sit on top of the freshly opened project.
  useEffect(() => {
    if (projectPath !== initialPath.current) onClose()
  }, [projectPath, onClose])

  return (
    <div
      className="start-drawer-overlay"
      onPointerDown={(e) => e.target === e.currentTarget && onClose()}
    >
      <div
        className={`start-drawer${window.novalist.platform === 'darwin' ? ' start-drawer-mac' : ''}`}
        role="dialog"
        aria-label={t('shell.menu')}
        onKeyDown={(e) => e.key === 'Escape' && onClose()}
      >
        <div className="start-drawer-scroll">
          <div className="start-drawer-actions">
            <button className="start-menu-action" onClick={() => setCreateOpen(true)}>
              <FilePlus2 size={16} strokeWidth={1.75} />
              {t('welcome.newProject')}
            </button>
            <button className="start-menu-action" onClick={() => void pickAndOpenProject()}>
              <FolderOpen size={16} strokeWidth={1.75} />
              {t('welcome.browseFolder')}
            </button>
            <button className="start-menu-action" onClick={() => setImportOpen(true)}>
              <Import size={16} strokeWidth={1.75} />
              {t('welcome.importPlugin')}
            </button>
            <button className="start-menu-action" onClick={() => setManuscriptImportOpen(true)}>
              <Import size={16} strokeWidth={1.75} />
              {t('manuscriptImport.action')}
            </button>
          </div>

          <div className="start-recents-label">{t('welcome.recentProjects')}</div>
          <div className="start-menu-recents">
            {recentProjects.length === 0 && (
              <p className="start-recents-empty">{t('welcome.noRecentProjects')}</p>
            )}
            {recentProjects.map((p) => (
              <button
                key={p.path}
                className="start-recent start-recent-row"
                onClick={() => void openProject(p.path)}
              >
                {p.cover ? (
                  <img className="start-recent-thumb" src={p.cover} alt="" />
                ) : (
                  <span className="start-recent-thumb start-recent-thumb-empty">
                    <BookOpen size={16} strokeWidth={1.5} />
                  </span>
                )}
                <span className="start-recent-row-text">
                  <span className="start-recent-name">{p.name}</span>
                  <span className="start-recent-path">{p.path}</span>
                </span>
              </button>
            ))}
          </div>

          <div className="start-drawer-sep" />
          <div className="start-drawer-actions">
            <button
              className="start-menu-action"
              onClick={() => {
                useShellStore.getState().openSettings()
                onClose()
              }}
            >
              <Settings size={16} strokeWidth={1.75} />
              {t('ribbon.settingsLabel')}
            </button>
            <button
              className="start-menu-action"
              onClick={() => {
                useShellStore.getState().setMainView('extensions')
                onClose()
              }}
            >
              <Plug size={16} strokeWidth={1.75} />
              {t('extensions.title')}
            </button>
            <button
              className="start-menu-action"
              onClick={() => {
                useShellStore.getState().setHelpOpen(true)
                onClose()
              }}
            >
              <BookOpen size={16} strokeWidth={1.75} />
              {t('help.manual')}
            </button>
          </div>
        </div>

        {backendVersion && (
          <div className="start-drawer-version">
            {t('shell.backendConnected', { version: backendVersion })}
          </div>
        )}
      </div>

      {createOpen && <CreateProjectDialog onClose={() => setCreateOpen(false)} />}
      {manuscriptImportOpen && (
        <ImportManuscriptDialog onClose={() => setManuscriptImportOpen(false)} />
      )}
      {importOpen && (
        <ImportPluginDialog
          onClose={() => setImportOpen(false)}
          onImported={(projectPath) => {
            setImportOpen(false)
            void openProject(projectPath)
          }}
        />
      )}
    </div>
  )
}
