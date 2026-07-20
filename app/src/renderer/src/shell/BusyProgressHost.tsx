import { useTranslation } from 'react-i18next'
import { useHostBridgeStore } from '../stores/hostBridgeStore'
import './hostBridge.css'

/**
 * Renders extension-host busy-progress dialogs (IHostServices.ShowBusyProgress),
 * e.g. the AI Assistant's knowledge scan and synopsis generation. Only the most
 * recently opened dialog is shown; Cancel routes back to the host, which fires
 * the extension's CancellationToken.
 */
export function BusyProgressHost(): React.JSX.Element | null {
  const { t } = useTranslation()
  const progress = useHostBridgeStore((s) => s.progress)
  const cancel = useHostBridgeStore((s) => s.cancelProgress)

  const active = progress[progress.length - 1]
  if (!active) return null

  const pct = Math.round(Math.max(0, Math.min(1, active.progress)) * 100)

  return (
    <div className="dialog-overlay" role="presentation">
      <div className="dialog-card busy-progress-card" role="dialog" aria-label={active.title} aria-live="polite">
        <div className="dialog-title">{active.title}</div>
        {active.status && <div className="busy-progress-status">{active.status}</div>}
        {active.showProgressBar && (
          <div
            className={`busy-progress-track${active.indeterminate ? ' indeterminate' : ''}`}
            role="progressbar"
            aria-valuemin={0}
            aria-valuemax={100}
            aria-valuenow={active.indeterminate ? undefined : pct}
          >
            <div
              className="busy-progress-fill"
              style={active.indeterminate ? undefined : { width: `${pct}%` }}
            />
          </div>
        )}
        {active.details.length > 0 && (
          <ul className="busy-progress-details">
            {active.details.map((line, i) => (
              <li key={i}>{line}</li>
            ))}
          </ul>
        )}
        {active.allowCancel && (
          <div className="dialog-actions">
            <button className="dialog-button" onClick={() => cancel(active.token)}>
              {active.cancelLabel || t('wizard.cancel')}
            </button>
          </div>
        )}
      </div>
    </div>
  )
}
