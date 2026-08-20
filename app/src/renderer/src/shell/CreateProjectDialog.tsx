import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { FolderOpen } from 'lucide-react'
import { rpc } from '../rpc/client'
import { useProjectStore, type ProjectStateDto } from '../stores/projectStore'
import { SnowflakeSetup } from './SnowflakeSetup'

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
  const [snowflake, setSnowflake] = useState(false)
  const [setupOpen, setSetupOpen] = useState(false)

  useEffect(() => {
    void rpc
      .request<{ id: string; name: string; description: string }[]>('project/templates')
      .then(setTemplates)
      .catch(() => setTemplates([]))
  }, [])

  // On iOS a project has to go somewhere the app can still read after an update,
  // and the writer has no filesystem to reason about - so Novalist's own folder
  // in the Files app is filled in for them, and Browse is there for anyone who
  // wants somewhere else. The desktop does not implement this and keeps its empty
  // field: there, where a book lives is the writer's decision to make first.
  useEffect(() => {
    void window.novalist.defaultProjectRoot?.().then((root) => {
      if (root) setLocation((current) => current || root)
    })
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
      // The premise ladder is asked once the project exists, because it has to
      // have somewhere to be saved - and it takes the dialog over, so this one
      // stays up underneath it rather than closing and taking it with it.
      // Otherwise the work is done and there is nothing left to fill in: the
      // form used to sit there over the project it had just made, still
      // showing a spinning Create button, as though it had not worked.
      if (snowflake) setSetupOpen(true)
      else onClose()
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

        <label className="relationships-toggle">
          <input
            type="checkbox"
            checked={snowflake}
            onChange={(e) => setSnowflake(e.target.checked)}
          />
          {t('premise.startWithPremise')}
        </label>

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
      {setupOpen && <SnowflakeSetup onClose={onClose} />}
    </div>
  )
}
