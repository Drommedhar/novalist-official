import { useTranslation } from 'react-i18next'
import type { StoreUpdate } from '../stores/extensionsStore'
import './shellDialogs.css'

const RELEASES_URL = 'https://github.com/Drommedhar/novalist-official/releases'

interface UpdateDialogProps {
  /** App update from the GitHub check, or null. */
  appUpdate: AppUpdate | null
  /** Currently running version, if known. */
  currentVersion?: string | null
  /** Installed extensions with an available update (actionable inline). */
  extUpdates: StoreUpdate[]
  /** The extension currently being updated, if any. */
  updatingExtId: string | null
  /** App-installer download progress percent, else null. */
  progress: number | null
  downloading: boolean
  onDownload(): void
  onUpdateExt(u: StoreUpdate): void
  onClose(): void
}

/**
 * Combined update dialog. The app self-update downloads + runs the platform
 * installer; each extension update is applied inline via the store (no tab
 * switch needed, so it works from the welcome screen too).
 */
export function UpdateDialog({
  appUpdate,
  currentVersion,
  extUpdates,
  updatingExtId,
  progress,
  downloading,
  onDownload,
  onUpdateExt,
  onClose
}: UpdateDialogProps): React.JSX.Element {
  const { t } = useTranslation()
  const nothing = !appUpdate && extUpdates.length === 0

  const versionLine = appUpdate
    ? t('update.versionInfo').replace('{0}', currentVersion ?? '').replace('{1}', appUpdate.version)
    : ''

  return (
    <div className="dialog-overlay" onPointerDown={(e) => e.target === e.currentTarget && onClose()}>
      <div
        className="dialog-card"
        role="dialog"
        aria-label={t('update.available')}
        onKeyDown={(e) => e.key === 'Escape' && onClose()}
      >
        <div className="dialog-title">
          {nothing ? t('update.upToDate') : t('update.available')}
        </div>

        {nothing && <p className="dialog-message">{t('update.upToDateDetail')}</p>}

        {appUpdate && (
          <>
            <p className="dialog-message">{versionLine}</p>
            <div className="update-notes">
              {appUpdate.notes.trim() ? appUpdate.notes : t('update.noNotes')}
            </div>
            {downloading && (
              <div className="update-progress">
                <div className="update-progress-track">
                  <div
                    className="update-progress-fill"
                    style={{ width: `${Math.max(2, progress ?? 0)}%` }}
                  />
                </div>
                <span className="update-progress-label">
                  {t('update.downloading').replace('{0}', String(progress ?? 0))}
                </span>
              </div>
            )}
            <div className="dialog-actions">
              <button
                className="dialog-button"
                onClick={() => void window.novalist.openExternal(RELEASES_URL)}
              >
                {t('update.viewRelease')}
              </button>
              <button className="dialog-button primary" disabled={downloading} onClick={onDownload}>
                {t('update.downloadInstall')}
              </button>
            </div>
          </>
        )}

        {extUpdates.length > 0 && (
          <div className="update-ext-list">
            {extUpdates.map((u) => (
              <div key={u.extensionId} className="update-ext-row">
                <span className="update-ext-name">{u.name}</span>
                <span className="update-ext-ver">
                  {u.installedVersion} &rarr; {u.availableVersion}
                </span>
                <button
                  className="dialog-button"
                  disabled={updatingExtId !== null}
                  onClick={() => onUpdateExt(u)}
                >
                  {updatingExtId === u.extensionId ? t('update.updating') : t('update.updateAction')}
                </button>
              </div>
            ))}
          </div>
        )}

        <div className="dialog-actions">
          <button className="dialog-button" onClick={onClose}>
            {nothing ? t('dialog.close') : t('update.later')}
          </button>
        </div>
      </div>
    </div>
  )
}
