import type { Frame, Page } from '@playwright/test'

export async function holdMotion(page: Page | Frame, selector: string) {
  return page.evaluateHandle((target) => {
    const held = new Set<Animation>()
    const pause = (): void => {
      for (const element of document.querySelectorAll(target)) {
        for (const animation of element.getAnimations({ subtree: true })) {
          const end = animation.effect?.getComputedTiming().endTime
          if (typeof end !== 'number' || !Number.isFinite(end) || end <= 0 || animation.playState === 'finished') continue
          animation.pause()
          held.add(animation)
        }
      }
    }
    const observer = new MutationObserver(pause)
    observer.observe(document.body, { childList: true, subtree: true, attributes: true })
    document.addEventListener('animationstart', pause, true)
    document.addEventListener('transitionrun', pause, true)
    return {
      details: () => [...held].filter(animation => animation.playState !== 'idle').map(animation => ({
        duration: Number(animation.effect?.getTiming().duration),
        fading: animation.effect instanceof KeyframeEffect && animation.effect.getKeyframes().some(frame => frame.opacity !== undefined)
      })),
      middle: () => {
        for (const animation of held) {
          if (animation.playState !== 'idle') animation.currentTime = Number(animation.effect?.getTiming().duration) / 2
        }
      },
      finish: () => {
        for (const animation of held) {
          if (animation.playState !== 'idle') animation.finish()
        }
        held.clear()
      },
      stop: () => {
        observer.disconnect()
        document.removeEventListener('animationstart', pause, true)
        document.removeEventListener('transitionrun', pause, true)
      }
    }
  }, selector)
}
