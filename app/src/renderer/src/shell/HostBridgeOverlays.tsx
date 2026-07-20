import { BusyProgressHost } from './BusyProgressHost'
import { ExtensionWizardHost } from './ExtensionWizardHost'
import { ToastHost } from './ToastHost'

/**
 * Mounts the extension-host UI surfaces (toasts, busy-progress dialogs, wizard
 * runner). Rendered once from MainArea; each surface reads its state from the
 * host-bridge store, so view switches don't disturb an in-flight dialog.
 */
export function HostBridgeOverlays(): React.JSX.Element {
  return (
    <>
      <ToastHost />
      <BusyProgressHost />
      <ExtensionWizardHost />
    </>
  )
}
