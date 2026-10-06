import {
  Children,
  Fragment,
  createContext,
  useContext,
  useLayoutEffect,
  useRef,
  useState,
  useSyncExternalStore,
  type ReactNode
} from 'react'
import './motion-presence.css'

const MotionPresent = createContext(true)
const REDUCED_MOTION = '(prefers-reduced-motion: reduce)'

function subscribeReducedMotion(onChange: () => void): () => void {
  const media = window.matchMedia(REDUCED_MOTION)
  media.addEventListener('change', onChange)
  return () => media.removeEventListener('change', onChange)
}

function reducedMotion(): boolean {
  return window.matchMedia(REDUCED_MOTION).matches
}

export function useMotionPresent(): boolean {
  return useContext(MotionPresent)
}

export function MotionPresence({ children, disabled = false, collapse = false, onExitComplete }: {
  children: ReactNode
  disabled?: boolean
  collapse?: boolean
  onExitComplete?(): void
}): React.JSX.Element {
  const ancestorPresent = useMotionPresent()
  const present = Children.toArray(children).length > 0
  const reduceMotion = useSyncExternalStore(subscribeReducedMotion, reducedMotion)
  const [retainedChildren, setRetainedChildren] = useState(children)
  const [session, setSession] = useState({ present, generation: 0 })
  const ref = useRef<HTMLDivElement>(null)
  const completion = useRef(onExitComplete)
  const immediate = disabled || reduceMotion || !ancestorPresent || document.documentElement.dataset.shell !== 'desktop'

  if (session.present !== present) {
    setSession({ present, generation: session.generation + (present ? 1 : 0) })
  }

  useLayoutEffect(() => {
    if (present) setRetainedChildren(children)
  }, [children, present])

  useLayoutEffect(() => { completion.current = onExitComplete }, [onExitComplete])

  useLayoutEffect(() => {
    if (present) return
    const container = ref.current
    const active = document.activeElement
    if (active instanceof HTMLElement && container?.contains(active)) active.blur()
    const finish = (): void => {
      setRetainedChildren(null)
      if (Children.toArray(retainedChildren).length > 0) completion.current?.()
    }
    const animations = !immediate && container
      ? Array.from(container.children).flatMap((element) => element.getAnimations())
        .filter((animation) => animation instanceof CSSAnimation && animation.animationName === 'surface-presence-exit')
      : []
    if (animations.length === 0) {
      finish()
      return
    }
    let cancelled = false
    void Promise.allSettled(animations.map((animation) => animation.finished)).then(() => {
      if (!cancelled) finish()
    })
    return () => { cancelled = true }
  }, [present, immediate])

  return (
    <MotionPresent.Provider value={ancestorPresent && present}>
      <div
        ref={ref}
        className="motion-presence"
        data-motion-presence={present ? 'open' : 'closing'}
        data-motion-disabled={immediate || undefined}
        data-motion-collapse={collapse || undefined}
        inert={!ancestorPresent || !present}
        aria-hidden={!ancestorPresent || !present || undefined}
      >
        <Fragment key={session.generation}>
          {present ? children : immediate ? null : retainedChildren}
        </Fragment>
      </div>
    </MotionPresent.Provider>
  )
}
