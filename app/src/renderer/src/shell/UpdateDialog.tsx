import { useTranslation } from 'react-i18next'
import Markdown from 'react-markdown'
import remarkGfm from 'remark-gfm'
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
  /** Exact handoff failure, kept visible so a retry is informed. */
  error: string | null
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
  error,
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
    <div
      className="dialog-overlay"
      onPointerDown={(e) => e.target === e.currentTarget && !downloading && onClose()}
    >
      <div
        className="dialog-card"
        role="dialog"
        aria-label={t('update.available')}
        onKeyDown={(e) => e.key === 'Escape' && !downloading && onClose()}
      >
        <div className="dialog-title">
          {nothing ? t('update.upToDate') : t('update.available')}
        </div>

        {nothing && <p className="dialog-message">{t('update.upToDateDetail')}</p>}

        {appUpdate && (
          <>
            <p className="dialog-message">{versionLine}</p>
            {/* The notes are the changelog's own Markdown, so they are read as
                Markdown - the Extensions store has rendered release bodies this
                way since it shipped, while this dialog showed the asterisks and
                hashes to the writer as characters. */}
            <div className="update-notes">
              {appUpdate.notes.trim() ? (
                <Markdown
                  remarkPlugins={[remarkGfm]}
                  components={{
                    a: ({ href, children }) => (
                      <a
                        href={href}
                        onClick={(event) => {
                          event.preventDefault()
                          if (!href) return
                          try {
                            const target = new URL(href, RELEASES_URL)
                            if (target.protocol === 'https:' || target.protocol === 'http:') {
                              void window.novalist.openExternal(target.toString())
                            }
                          } catch {
                            // Invalid release-note links remain inert.
                          }
                        }}
                      >
                        {children}
                      </a>
                    )
                  }}
                >
                  {appUpdate.notes}
                </Markdown>
              ) : (
                t('update.noNotes')
              )}
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
            {error && (
              <p className="update-error" role="alert">
                {t('update.error').replace('{0}', error)}
              </p>
            )}
            <div className="dialog-actions">
              <button
                className="dialog-button"
                disabled={downloading}
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
                  disabled={downloading || updatingExtId !== null}
                  onClick={() => onUpdateExt(u)}
                >
                  {updatingExtId === u.extensionId ? t('update.updating') : t('update.updateAction')}
                </button>
              </div>
            ))}
          </div>
        )}

        <div className="dialog-actions">
          <button className="dialog-button" disabled={downloading} onClick={onClose}>
            {nothing ? t('dialog.close') : t('update.later')}
          </button>
        </div>
      </div>
    </div>
  )
}
