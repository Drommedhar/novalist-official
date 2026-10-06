import { createPortal } from 'react-dom'
import { useTranslation } from 'react-i18next'
import { useProjectStore } from '../stores/projectStore'
import { MotionPresence } from './MotionPresence'

/** Kept outside the inert workspace so restoring a temporarily unavailable
 * project never requires discarding the prose still held by its windows. */
export function WorkspaceRecoveryDialog(): React.JSX.Element | null {
  const { t } = useTranslation()
  const recovering = useProjectStore((state) => state.workspaceRecovering)
  const error = useProjectStore((state) => state.workspaceRecoveryError)
  const retry = async (): Promise<void> => {
    useProjectStore.setState({ workspaceRecoveryError: null })
    try { await window.novalist.retryWorkspaceRecovery?.() }
    catch (error) { useProjectStore.setState({ workspaceRecoveryError: String(error) }) }
  }
  return createPortal(
    <MotionPresence>{recovering &&
    <div className="dialog-overlay">
      <div className="dialog-card" role="dialog" aria-label={t('shell.backendConnecting')}>
        <div className="dialog-title">{t('shell.backendConnecting')}</div>
        {error && <><p>{error}</p><button className="dialog-button" onClick={() => void retry()}>{t('shell.retry')}</button></>}
      </div>
    </div>}</MotionPresence>, document.body
  )
}
