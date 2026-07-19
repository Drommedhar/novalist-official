import { useTranslation } from 'react-i18next'
import './shellDialogs.css'

/** GitHub releases page. On macOS the build is unsigned (notify-only), so the
 * download action opens this page rather than auto-installing. */
const RELEASES_URL = 'https://github.com/dominikgoblirsch/novalist-official/releases'

interface UpdateDialogProps {
  version: string
  /** Currently running version, if known (from system/ping). */
  currentVersion?: string | null
  /** Release notes from the update event, if any. */
  notes?: string | null
  onClose(): void
}

/** Update-available dialog: shows the new version, release notes (or a generic
 * message), a Skip/dismiss action, and a Download action that opens the
 * releases page in the browser. Replaces the inline update banner. */
export function UpdateDialog({
  version,
  currentVersion,
  notes,
  onClose
}: UpdateDialogProps): React.JSX.Element {
  const { t } = useTranslation()

  const versionLine = t('update.versionInfo')
    .replace('{0}', currentVersion ?? '')
    .replace('{1}', version)

  const download = (): void => {
    void window.novalist.openExternal(RELEASES_URL)
    onClose()
  }

  return (
    <div className="dialog-overlay" onPointerDown={(e) => e.target === e.currentTarget && onClose()}>
      <div
        className="dialog-card"
        role="dialog"
        aria-label={t('update.available')}
        onKeyDown={(e) => e.key === 'Escape' && onClose()}
      >
        <div className="dialog-title">{t('update.available')}</div>
        <p className="dialog-message">{versionLine}</p>
        <div className="update-notes">{notes?.trim() ? notes : t('update.noNotes')}</div>
        <div className="dialog-actions">
          <button className="dialog-button" onClick={onClose}>
            {t('update.skip')}
          </button>
          <button className="dialog-button primary" onClick={download}>
            {t('update.downloadInstall')}
          </button>
        </div>
      </div>
    </div>
  )
}
