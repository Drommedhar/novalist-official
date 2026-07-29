import { useEffect, useRef, useState } from 'react'

interface LazyBlockProps {
  /**
   * Height to reserve while the block is unmounted. Scrolling has to land in
   * the same place whether or not the block has been rendered yet, so the
   * placeholder is the size of what it stands in for, not zero.
   */
  estimatedHeight: number
  /** Rendered immediately regardless of position - the first screenful. */
  eager?: boolean
  className?: string
  children: React.ReactNode
}

/**
 * Renders its children only once they are near the viewport, and keeps them
 * rendered afterwards.
 *
 * The manuscript's corkboard and outliner built every card and every row of
 * the whole book up front. At fifty chapters that is thousands of text areas
 * and inputs, laid out and painted before the writer has scrolled past the
 * first three - the slowdown the troubleshooting page used to admit to.
 *
 * Blocks stay mounted once shown: unmounting them again would throw away text
 * a writer was part-way through typing into a synopsis box.
 */
export function LazyBlock({
  estimatedHeight,
  eager = false,
  className,
  children
}: LazyBlockProps): React.JSX.Element {
  const [shown, setShown] = useState(eager)
  const ref = useRef<HTMLDivElement>(null)

  useEffect(() => {
    if (shown) return
    const node = ref.current
    // No IntersectionObserver (a test DOM, an old runtime) means everything
    // renders, which is the behaviour this replaced - slower, never wrong.
    if (!node || typeof IntersectionObserver === 'undefined') {
      setShown(true)
      return
    }

    const observer = new IntersectionObserver(
      (entries) => {
        if (entries.some((e) => e.isIntersecting)) setShown(true)
      },
      // A screen and a half of runway, so a fast scroll meets rendered cards
      // rather than blank space it has to wait for.
      { rootMargin: '150% 0px' }
    )
    observer.observe(node)
    return () => observer.disconnect()
  }, [shown])

  return (
    <div ref={ref} className={className} style={shown ? undefined : { height: estimatedHeight }}>
      {shown ? children : null}
    </div>
  )
}
