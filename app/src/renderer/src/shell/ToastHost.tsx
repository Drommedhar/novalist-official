import { X } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { useHostBridgeStore } from '../stores/hostBridgeStore'
import './hostBridge.css'

/**
 * Bottom-right stack of extension-host toast notifications
 * (IHostServices.ShowNotification / extension-load failures). Auto-dismissing;
 * click the close affordance to dismiss early.
 */
export function ToastHost(): React.JSX.Element | null {
  const { t } = useTranslation()
  const toasts = useHostBridgeStore((s) => s.toasts)
  const dismiss = useHostBridgeStore((s) => s.dismissToast)

  if (toasts.length === 0) return null

  return (
    <div className="toast-host" role="region" aria-label={t('hostBridge.toastRegion')}>
      {toasts.map((toast) => (
        <div key={toast.id} className="toast-card" role="status">
          <span className="toast-message">{toast.message}</span>
          <button
            className="toast-dismiss"
            aria-label={t('hostBridge.dismiss')}
            onClick={() => dismiss(toast.id)}
          >
            <X size={14} />
          </button>
        </div>
      ))}
    </div>
  )
}
