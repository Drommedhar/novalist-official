import { useEffect } from 'react'

export function useMobileDetailBack(active: boolean, onBack: () => void): void {
  useEffect(() => {
    if (!active || window.novalist.platform !== 'android') return
    const back = (event: Event): void => {
      if (event.defaultPrevented) return
      event.preventDefault()
      onBack()
    }
    window.addEventListener('novalist-mobile-detail-back', back)
    return () => window.removeEventListener('novalist-mobile-detail-back', back)
  }, [active, onBack])
}
