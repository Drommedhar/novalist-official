import { useLayoutEffect, type RefObject } from 'react'

/** Fade a navigation change without remounting the editor or moving its caret. */
export function useContentTransition(ref: RefObject<HTMLElement | null>, contentKey: string): void {
  useLayoutEffect(() => {
    const element = ref.current
    const preference = window.matchMedia('(prefers-reduced-motion: reduce)')
    if (!element || preference.matches || document.documentElement.dataset.shell !== 'desktop') return

    const style = getComputedStyle(element)
    const duration = Number.parseFloat(style.getPropertyValue('--nl-motion-normal'))
    const animation = element.animate([{ opacity: 0 }, { opacity: 1 }], {
      duration,
      easing: style.getPropertyValue('--nl-motion-ease').trim()
    })
    const cancel = (): void => animation.cancel()
    preference.addEventListener('change', cancel)
    return () => {
      preference.removeEventListener('change', cancel)
      animation.cancel()
    }
  }, [ref, contentKey])
}
