import { useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { FilePlus2, FolderOpen, Import } from 'lucide-react'
import { useProjectStore } from '../stores/projectStore'
import { CreateProjectDialog } from './CreateProjectDialog'
import { ImportPluginDialog } from './ImportPluginDialog'
import './shellDialogs.css'

/** Start-menu overlay reachable while a project is open: recent projects,
 * new project, open project, and import. Reuses the start-screen flows. */
export function StartMenuOverlay({ onClose }: { onClose(): void }): React.JSX.Element {
  const { t } = useTranslation()
  const recentProjects = useProjectStore((s) => s.recentProjects)
  const openProject = useProjectStore((s) => s.openProject)
  const pickAndOpenProject = useProjectStore((s) => s.pickAndOpenProject)
  const projectPath = useProjectStore((s) => s.projectPath)
  const [createOpen, setCreateOpen] = useState(false)
  const [importOpen, setImportOpen] = useState(false)
  const initialPath = useRef(projectPath)

  useEffect(() => void useProjectStore.getState().loadRecents(), [])

  // Loading/creating/importing a project changes projectPath; close the overlay
  // once that happens so we do not sit on top of the freshly opened project.
  useEffect(() => {
    if (projectPath !== initialPath.current) onClose()
  }, [projectPath, onClose])

  return (
    <div className="dialog-overlay" onPointerDown={(e) => e.target === e.currentTarget && onClose()}>
      <div
        className="dialog-card start-menu"
        role="dialog"
        aria-label={t('shell.menu')}
        onKeyDown={(e) => e.key === 'Escape' && onClose()}
      >
        <div className="dialog-title">{t('shell.menu')}</div>
        <div className="start-menu-actions">
          <button className="start-menu-action" onClick={() => setCreateOpen(true)}>
            <FilePlus2 size={16} strokeWidth={1.75} />
            {t('welcome.newProject')}
          </button>
          <button
            className="start-menu-action"
            onClick={() => void pickAndOpenProject()}
          >
            <FolderOpen size={16} strokeWidth={1.75} />
            {t('welcome.browseFolder')}
          </button>
          <button className="start-menu-action" onClick={() => setImportOpen(true)}>
            <Import size={16} strokeWidth={1.75} />
            {t('welcome.importPlugin')}
          </button>
        </div>

        <div className="start-recents-label">{t('welcome.recentProjects')}</div>
        <div className="start-menu-recents">
          {recentProjects.length === 0 && (
            <p className="start-recents-empty">{t('welcome.noRecentProjects')}</p>
          )}
          {recentProjects.map((p) => (
            <button key={p.path} className="start-recent" onClick={() => void openProject(p.path)}>
              <span className="start-recent-name">{p.name}</span>
              <span className="start-recent-path">{p.path}</span>
            </button>
          ))}
        </div>

        <div className="dialog-actions">
          <button className="dialog-button" onClick={onClose}>
            {t('dialog.close')}
          </button>
        </div>
      </div>

      {createOpen && <CreateProjectDialog onClose={() => setCreateOpen(false)} />}
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
