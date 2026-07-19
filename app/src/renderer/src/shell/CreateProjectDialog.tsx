import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { FolderOpen } from 'lucide-react'
import { rpc } from '../rpc/client'
import { useProjectStore, type ProjectStateDto } from '../stores/projectStore'

/**
 * New-project dialog on the start screen: name, first book, parent folder,
 * and an optional project structure template (Blank, Three-Act, ...).
 */
export function CreateProjectDialog({ onClose }: { onClose: () => void }): React.JSX.Element {
  const { t } = useTranslation()
  const [projectName, setProjectName] = useState('')
  const [bookName, setBookName] = useState('')
  const [location, setLocation] = useState('')
  const [templateId, setTemplateId] = useState('blank')
  const [templates, setTemplates] = useState<{ id: string; name: string; description: string }[]>(
    []
  )
  const [error, setError] = useState<string | null>(null)
  const [creating, setCreating] = useState(false)

  useEffect(() => {
    void rpc
      .request<{ id: string; name: string; description: string }[]>('project/templates')
      .then(setTemplates)
      .catch(() => setTemplates([]))
  }, [])

  const create = async (): Promise<void> => {
    if (!projectName.trim() || !location) return
    setError(null)
    setCreating(true)
    try {
      const state = await rpc.request<ProjectStateDto>('project/create', [
        location,
        projectName.trim(),
        bookName.trim() || projectName.trim(),
        templateId
      ])
      window.novalist.setProjectRoot(state.projectPath)
      useProjectStore.getState().applyState(state)
    } catch (e) {
      setCreating(false)
      setError(String(e))
    }
  }

  return (
    <div
      className="dialog-overlay"
      onPointerDown={(e) => e.target === e.currentTarget && !creating && onClose()}
    >
      <div className="dialog-card" role="dialog" aria-label={t('welcome.createTitle')}>
        <div className="dialog-title">{t('welcome.createTitle')}</div>

        <label className="inspector-label">{t('welcome.projectName')}</label>
        <input
          className="dialog-input"
          autoFocus
          placeholder={t('welcome.projectNamePlaceholder')}
          value={projectName}
          onChange={(e) => setProjectName(e.target.value)}
        />

        <label className="inspector-label">{t('welcome.bookName')}</label>
        <input
          className="dialog-input"
          placeholder={t('welcome.bookNamePlaceholder')}
          value={bookName}
          onChange={(e) => setBookName(e.target.value)}
        />

        <label className="inspector-label">{t('welcome.location')}</label>
        <div className="type-manager-field">
          <input
            className="dialog-input"
            readOnly
            placeholder={t('welcome.locationPlaceholder')}
            value={location}
          />
          <button
            className="dialog-button"
            disabled={creating}
            onClick={() =>
              void window.novalist.pickFolder(t('welcome.pickFolderTitle')).then((folder) => {
                if (folder) setLocation(folder)
              })
            }
          >
            <FolderOpen size={13} strokeWidth={2} /> {t('welcome.browse')}
          </button>
        </div>

        {templates.length > 0 && (
          <>
            <label className="inspector-label">{t('welcome.template')}</label>
            <select
              className="dialog-input"
              value={templateId}
              onChange={(e) => setTemplateId(e.target.value)}
            >
              {templates.map((tpl) => (
                <option key={tpl.id} value={tpl.id} title={tpl.description}>
                  {tpl.name}
                </option>
              ))}
            </select>
          </>
        )}

        {error && <p className="findreplace-result">{error}</p>}

        <div className="dialog-actions">
          <button className="dialog-button" disabled={creating} onClick={onClose}>
            {t('dialog.cancel')}
          </button>
          <button
            className="dialog-button primary"
            disabled={creating || !projectName.trim() || !location}
            onClick={() => void create()}
          >
            {t('welcome.createProject')}
          </button>
        </div>
      </div>
    </div>
  )
}
