import { useShellStore } from '../stores/shellStore'

export function installNativeLayout(initial: 'phone' | 'tablet'): void {
  window.__novalistLayout = (mode: string) => {
    const layout = mode === 'tablet' ? 'tablet' : 'phone'
    useShellStore.getState().setMobileLayout(layout)
    document.documentElement.dataset.mobileLayout = layout
  }
  window.__novalistLayout(initial)
  window.novalist.requestLayout?.()
}
