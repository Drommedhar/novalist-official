import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { rpc } from '../rpc/client'
import { useProjectStore, type ProjectStateDto } from '../stores/projectStore'
import { useManuscriptStore } from '../stores/manuscriptStore'
import { flushPendingWrites } from '../stores/pendingWrites'

export function RestoreBackupDialog({ onClose, archivePath = '' }: {
  onClose: () => void
  archivePath?: string
}): React.JSX.Element {
  const { t } = useTranslation()
  const [archive, setArchive] = useState(archivePath)
  const [location, setLocation] = useState('')
  const [name, setName] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    void window.novalist.defaultProjectRoot?.().then((root) => {
      if (root) setLocation((current) => current || root)
    })
  }, [])

  const restore = async (): Promise<void> => {
    setBusy(true)
    setError(null)
    try {
      await flushPendingWrites()
      await useProjectStore.getState().flushPendingSave()
      await useManuscriptStore.getState().flushPendingSave()
      const state = await rpc.request<ProjectStateDto>('backup/restoreAsNewProject', [archive, location, name.trim()])
      useProjectStore.getState().applyState(state, true)
      useManuscriptStore.setState({ loaded: false, sections: [], composed: null })
      onClose()
    } catch (e) {
      setError(String(e))
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="dialog-overlay" onPointerDown={(e) => e.target === e.currentTarget && !busy && onClose()}>
      <div className="dialog-card" role="dialog" aria-label={t('backup.restoreAsNew')} aria-busy={busy}>
        <div className="dialog-title">{t('backup.restoreAsNew')}</div>
        <p className="settings-hint">{t('backup.restoreAsNewDesc')}</p>
        <label className="inspector-label" htmlFor="restore-backup-file">{t('backup.archiveFile')}</label>
        <div className="type-manager-field">
          <input id="restore-backup-file" className="dialog-input" readOnly value={archive} />
          <button className="dialog-button" disabled={busy} onClick={() => {
            void window.novalist.pickFile(t('backup.archiveFile'), 'all').then((path) => {
              if (path) setArchive(path)
            }).catch((e: unknown) => setError(String(e)))
          }}>{t('welcome.browse')}</button>
        </div>
        <label className="inspector-label" htmlFor="restore-project-name">{t('welcome.projectName')}</label>
        <input id="restore-project-name" className="dialog-input" disabled={busy} value={name}
          onChange={(e) => setName(e.target.value)} autoFocus />
        <label className="inspector-label" htmlFor="restore-project-location">{t('welcome.location')}</label>
        <div className="type-manager-field">
          <input id="restore-project-location" className="dialog-input" readOnly value={location} />
          <button className="dialog-button" disabled={busy} onClick={() => {
            void window.novalist.pickFolder(t('welcome.pickFolderTitle')).then((path) => {
              if (path) setLocation(path)
            }).catch((e: unknown) => setError(String(e)))
          }}>{t('welcome.browse')}</button>
        </div>
        {error && <p className="findreplace-result" role="alert">{error}</p>}
        <div className="dialog-actions">
          <button className="dialog-button" disabled={busy} onClick={onClose}>{t('dialog.cancel')}</button>
          <button className="dialog-button primary" disabled={busy || !archive || !location || !name.trim()}
            onClick={() => void restore()}>{t('backup.restoreAndOpen')}</button>
        </div>
      </div>
    </div>
  )
}
