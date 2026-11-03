import { useShellStore } from '../stores/shellStore'
import { useProjectStore } from '../stores/projectStore'

export function installNativeLayout(initial: 'phone' | 'tablet'): void {
  window.__novalistLayout = (mode: string) => {
    const layout = mode === 'tablet' ? 'tablet' : 'phone'
    useShellStore.getState().setMobileLayout(layout)
    if (!useProjectStore.getState().closingProject) document.documentElement.dataset.mobileLayout = layout
  }
  window.__novalistLayout(initial)
  window.novalist.requestLayout?.()
}
