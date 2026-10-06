import { X } from 'lucide-react'
import { useLayoutEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useHostBridgeStore } from '../stores/hostBridgeStore'
import { useProjectStore } from '../stores/projectStore'
import { MotionPresence } from './MotionPresence'
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
  const suspendMotion = useProjectStore((s) => s.closingProject || s.workspaceSuspended)
  const [retained, setRetained] = useState(toasts)
  const shownIds = new Set(toasts.map((toast) => toast.id))
  const rows = [...retained, ...toasts.filter((toast) => !retained.some((entry) => entry.id === toast.id))]

  useLayoutEffect(() => {
    setRetained((previous) => [
      ...previous,
      ...toasts.filter((toast) => !previous.some((entry) => entry.id === toast.id))
    ])
  }, [toasts])

  if (rows.length === 0) return null

  return (
    <div className="toast-host" role="region" aria-label={t('hostBridge.toastRegion')}>
      {rows.map((toast) => (
        <MotionPresence
          key={toast.id}
          disabled={suspendMotion}
          onExitComplete={() => setRetained((previous) => previous.filter((entry) => entry.id !== toast.id))}
        >{shownIds.has(toast.id) && <div className="toast-card" role="status">
          <span className="toast-message">{toast.message}</span>
          <button
            className="toast-dismiss"
            aria-label={t('hostBridge.dismiss')}
            onClick={() => dismiss(toast.id)}
          >
            <X size={14} />
          </button>
        </div>}</MotionPresence>
      ))}
    </div>
  )
}
