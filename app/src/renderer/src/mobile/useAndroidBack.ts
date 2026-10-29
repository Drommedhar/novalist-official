import { useEffect } from 'react'
import { useHostBridgeStore } from '../stores/hostBridgeStore'
import { useShellStore } from '../stores/shellStore'
import { handleMobileBack } from './backNavigation'

export function useAndroidBack(inspectorOpen: boolean, setInspectorOpen: (open: boolean) => void,
  planningOpen: boolean, setPlanningOpen: (open: boolean) => void): void {
  useEffect(() => {
    if (window.novalist.platform !== 'android') return
    const back = (event: Event): void => {
      event.preventDefault()
      if (inspectorOpen) { setInspectorOpen(false); return }
      if (planningOpen) { setPlanningOpen(false); return }
      if (!window.dispatchEvent(new Event('novalist-mobile-nav-back', { cancelable: true }))) return
      if (!window.dispatchEvent(new Event('novalist-mobile-detail-back', { cancelable: true }))) return
      useShellStore.getState().guardLeave(() => {
        void handleMobileBack().catch((error: unknown) => useHostBridgeStore.getState().pushToast(String(error)))
      })
    }
    window.addEventListener('novalist-android-back', back)
    return () => { window.removeEventListener('novalist-android-back', back) }
  }, [inspectorOpen, setInspectorOpen, planningOpen, setPlanningOpen])
}
