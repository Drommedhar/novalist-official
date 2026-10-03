import { useEffect, useRef } from 'react'
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
  installing: boolean
  ready: boolean
  manualInstall: boolean
  checking: boolean
  onCheck(): void
  onInstall(): void
  /** Exact handoff failure, kept visible so a retry is informed. */
  error: string | null
  onDownload(): void
  onUpdateExt(u: StoreUpdate): void
  onClose(): void
}

/**
 * Release review with an explicit download, then install action. Downloads
 * can continue after dismissal; installation retains the workspace save guard.
 * Extension updates are applied inline, including from the bookshelf.
 */
export function UpdateDialog({
  appUpdate,
  currentVersion,
  extUpdates,
  updatingExtId,
  progress,
  downloading,
  installing,
  ready,
  manualInstall,
  checking,
  onCheck,
  onInstall,
  error,
  onDownload,
  onUpdateExt,
  onClose
}: UpdateDialogProps): React.JSX.Element {
  const { t } = useTranslation()
  const dialogRef = useRef<HTMLDialogElement>(null)
  useEffect(() => {
    const previous = document.activeElement as HTMLElement | null
    const dialog = dialogRef.current!
    dialog.showModal()
    return () => { dialog.close(); previous?.focus() }
  }, [])
  const nothing = !appUpdate && extUpdates.length === 0
  const heading = checking ? t('desktopRefresh.checking') : error && nothing ? t('desktopRefresh.checkFailed') : nothing ? t('update.upToDate') : t('update.available')

  const versionLine = appUpdate
    ? t('update.versionInfo').replace('{0}', currentVersion ?? '').replace('{1}', appUpdate.version)
    : ''

  return (
    <dialog ref={dialogRef} className="dialog-card update-dialog" tabIndex={-1} aria-labelledby="update-heading"
      onCancel={(event) => { event.preventDefault(); if (!installing) onClose() }}
      onKeyDown={(event) => {
        if (event.key === 'Escape') { event.preventDefault(); event.stopPropagation(); if (!installing) onClose(); return }
        if (event.key !== 'Tab') return
        const controls = Array.from(event.currentTarget.querySelectorAll<HTMLElement>('button:not(:disabled), a[href], [tabindex="0"]'))
          .filter((element) => element.getClientRects().length > 0)
        const first = controls[0], last = controls[controls.length - 1]
        if (!first) { event.preventDefault(); event.currentTarget.focus(); return }
        if (event.shiftKey && (document.activeElement === first || document.activeElement === event.currentTarget)) {
          event.preventDefault(); last?.focus()
        } else if (!event.shiftKey && document.activeElement === last) {
          event.preventDefault(); first?.focus()
        }
      }}>
        <div className="dialog-title" id="update-heading">{heading}</div>
        {nothing && !checking && !error && <p className="dialog-message">{t('update.upToDateDetail')}</p>}
        {error && <p className="update-error" role="alert">{error}</p>}
        {checking && <p role="status">{t('desktopRefresh.checking')}</p>}

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
            <p className="dialog-message" role="status">{installing ? t('desktopRefresh.installing') : ready ? t('desktopRefresh.ready') : manualInstall ? t('desktopRefresh.manualInstall') : t('desktopRefresh.downloadHint')}</p>
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
                  disabled={installing || updatingExtId !== null}
                  onClick={() => onUpdateExt(u)}
                >
                  {updatingExtId === u.extensionId ? t('update.updating') : t('update.updateAction')}
                </button>
              </div>
            ))}
          </div>
        )}

        <div className="dialog-actions">
          {appUpdate && <button className="dialog-button" disabled={installing} onClick={() => void window.novalist.openExternal(RELEASES_URL)}>{t('update.viewRelease')}</button>}
          {!appUpdate && <button className="dialog-button" disabled={checking || installing} onClick={onCheck}>{t('desktopRefresh.retryCheck')}</button>}
          {appUpdate && !manualInstall && <button className="dialog-button primary" disabled={downloading || installing || checking || updatingExtId !== null} onClick={ready ? onInstall : onDownload}>
            {ready ? t('desktopRefresh.install') : t('desktopRefresh.download')}
          </button>}
          <button className="dialog-button" disabled={installing} onClick={onClose}>
            {downloading ? t('desktopRefresh.continueWriting') : nothing ? t('dialog.close') : t('update.later')}
          </button>
        </div>
    </dialog>
  )
}
