import { useShellStore } from '../stores/shellStore'

export function useIsPhone(): boolean {
  const layout = useShellStore((state) => state.mobileLayout)
  return window.novalist.isMobile === true && layout === 'phone'
}
