import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { BookOpen, FilePlus2, FolderOpen, Import } from 'lucide-react'
import { ImportPluginDialog } from './ImportPluginDialog'
import { ScratchpadPanel } from './ScratchpadPanel'
import { CreateProjectDialog } from './CreateProjectDialog'

/** How many covers the welcome screen shows. Beyond this the row scrolls. */
const MAX_RECENTS = 8

interface StartScreenProps {
  recentProjects: { name: string; path: string; cover?: string | null }[]
  onOpenPath(path: string): void
  onPickProject(): void
}

export function StartScreen({
  recentProjects,
  onOpenPath,
  onPickProject
}: StartScreenProps): React.JSX.Element {
  const { t } = useTranslation()
  const [importOpen, setImportOpen] = useState(false)
  const [createOpen, setCreateOpen] = useState(false)

  return (
    <div className="start-screen">
      <div className="start-card">
        <BookOpen size={40} strokeWidth={1.25} className="start-logo" />
        <h1>Novalist</h1>
        <button className="start-open" onClick={() => setCreateOpen(true)}>
          <FilePlus2 size={16} strokeWidth={1.75} />
          {t('welcome.newProject')}
        </button>
        <button className="start-open secondary" onClick={onPickProject}>
          <FolderOpen size={16} strokeWidth={1.75} />
          {t('welcome.browseFolder')}
        </button>
        {createOpen && <CreateProjectDialog onClose={() => setCreateOpen(false)} />}
        <button className="start-open secondary" onClick={() => setImportOpen(true)}>
          <Import size={16} strokeWidth={1.75} />
          {t('welcome.importPlugin')}
        </button>
        {importOpen && (
          <ImportPluginDialog
            onClose={() => setImportOpen(false)}
            onImported={(projectPath) => {
              setImportOpen(false)
              onOpenPath(projectPath)
            }}
          />
        )}
        <div className="start-recents">
          <div className="start-recents-label">{t('welcome.recentProjects')}</div>
          {recentProjects.length === 0 && (
            <p className="start-recents-empty">{t('welcome.noRecentProjects')}</p>
          )}
          {/* One row, never a wall. The list grows with every project ever
              opened, and a grid that wraps pushed the scratchpad off the
              bottom of the screen by the tenth. */}
          <div className="start-recent-row">
            {recentProjects.slice(0, MAX_RECENTS).map((p) => (
              <button
                key={p.path}
                className="start-recent-card"
                onClick={() => onOpenPath(p.path)}
                title={p.path}
              >
                <div className="start-recent-cover">
                  {p.cover ? (
                    <img className="start-recent-cover-img" src={p.cover} alt="" />
                  ) : (
                    <div className="start-recent-cover-empty">
                      <BookOpen size={28} strokeWidth={1.25} />
                    </div>
                  )}
                </div>
                <span className="start-recent-name">{p.name}</span>
                <span className="start-recent-path">{p.path}</span>
              </button>
            ))}
          </div>
        </div>

        {/* Reachable with no project open, which is the whole point: this is
            where a thought goes when the project it belongs to is not the one
            in front of you. */}
        <ScratchpadPanel canFile={false} />
      </div>
    </div>
  )
}
