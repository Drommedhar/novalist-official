import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { FolderOpen } from 'lucide-react'
import { rpc } from '../rpc/client'

interface DetectedProject {
  name: string
  path: string
}

/**
 * Obsidian plugin-vault import: pick the vault, choose the detected project,
 * name the output, run import/run, and hand the new project path back.
 */
export function ImportPluginDialog({
  onImported,
  onClose
}: {
  onImported: (projectPath: string) => void
  onClose: () => void
}): React.JSX.Element {
  const { t } = useTranslation()
  const [vaultPath, setVaultPath] = useState('')
  const [projects, setProjects] = useState<DetectedProject[]>([])
  const [selectedPath, setSelectedPath] = useState('')
  const [projectName, setProjectName] = useState('')
  const [bookName, setBookName] = useState('')
  const [outputPath, setOutputPath] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [importing, setImporting] = useState(false)

  const browseVault = async (): Promise<void> => {
    const folder = await window.novalist.pickFolder(t('import.selectFolder'))
    if (!folder) return
    setError(null)
    setVaultPath(folder)
    try {
      const detection = await rpc.request<{ hasPluginData: boolean; projects: DetectedProject[] }>(
        'import/detect',
        [folder]
      )
      if (detection.projects.length === 0) {
        setProjects([])
        setError(t('import.noProjectsFound'))
        return
      }
      setProjects(detection.projects)
      setSelectedPath(detection.projects[0].path)
      if (!projectName) setProjectName(detection.projects[0].name)
      if (!bookName) setBookName(detection.projects[0].name)
    } catch (e) {
      setError(String(e))
    }
  }

  const runImport = async (): Promise<void> => {
    if (!vaultPath || projects.length === 0) {
      setError(t('import.selectVaultFirst'))
      return
    }
    if (!projectName.trim()) {
      setError(t('import.projectNameRequired'))
      return
    }
    if (!outputPath) {
      setError(t('import.outputRequired'))
      return
    }
    setError(null)
    setImporting(true)
    try {
      const result = await rpc.request<{ projectPath: string }>('import/run', [
        vaultPath,
        selectedPath,
        outputPath,
        projectName.trim(),
        bookName.trim() || projectName.trim()
      ])
      onImported(result.projectPath)
    } catch (e) {
      setImporting(false)
      setError(String(e))
    }
  }

  return (
    <div
      className="dialog-overlay"
      onPointerDown={(e) => e.target === e.currentTarget && !importing && onClose()}
    >
      <div className="dialog-card type-manager-card" role="dialog" aria-label={t('import.title')}>
        <div className="dialog-title">{t('import.title')}</div>
        <p className="wizard-help">{t('import.description')}</p>

        <label className="inspector-label">{t('import.vaultFolder')}</label>
        <div className="type-manager-field">
          <input
            className="dialog-input"
            readOnly
            placeholder={t('import.vaultFolderPlaceholder')}
            value={vaultPath}
          />
          <button className="dialog-button" disabled={importing} onClick={() => void browseVault()}>
            <FolderOpen size={13} strokeWidth={2} /> {t('import.selectFolder')}
          </button>
        </div>

        {projects.length > 1 && (
          <>
            <label className="inspector-label">{t('import.selectProject')}</label>
            <select
              className="dialog-input"
              value={selectedPath}
              onChange={(e) => setSelectedPath(e.target.value)}
            >
              {projects.map((p) => (
                <option key={p.path} value={p.path}>
                  {p.name}
                </option>
              ))}
            </select>
          </>
        )}

        <label className="inspector-label">{t('welcome.projectName')}</label>
        <input
          className="dialog-input"
          value={projectName}
          onChange={(e) => setProjectName(e.target.value)}
        />

        <label className="inspector-label">{t('welcome.bookName')}</label>
        <input
          className="dialog-input"
          value={bookName}
          onChange={(e) => setBookName(e.target.value)}
        />

        <label className="inspector-label">{t('import.outputLocation')}</label>
        <div className="type-manager-field">
          <input className="dialog-input" readOnly value={outputPath} />
          <button
            className="dialog-button"
            disabled={importing}
            onClick={() =>
              void window.novalist.pickFolder(t('import.outputLocation')).then((folder) => {
                if (folder) setOutputPath(folder)
              })
            }
          >
            <FolderOpen size={13} strokeWidth={2} /> {t('import.selectFolder')}
          </button>
        </div>

        {error && <p className="findreplace-result">{error}</p>}

        <div className="dialog-actions">
          <button className="dialog-button" disabled={importing} onClick={onClose}>
            {t('dialog.cancel')}
          </button>
          <button
            className="dialog-button primary"
            disabled={importing}
            onClick={() => void runImport()}
          >
            {t('import.startImport')}
          </button>
        </div>
      </div>
    </div>
  )
}
