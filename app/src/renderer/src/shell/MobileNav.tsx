import { createContext, useCallback, useContext, useMemo, useRef, useState } from 'react'
import { ChevronLeft, ChevronRight } from 'lucide-react'
import './mobile-nav.css'

/**
 * Drill-down navigation for the phone shell.
 *
 * A phone shows one thing at a time. Views built for a desktop pane put
 * everything on one page and let the writer scroll, which on a 393px screen
 * turns Settings into seventeen sections in a single column - findable only by
 * scrolling past the sixteen you did not want.
 *
 * So a view can hand a page to this stack instead: the root stays mounted, the
 * pushed page covers it, and a back bar returns. It is the iOS pattern, and it
 * is deliberately small - a stack of {title, content}, no routing, no history
 * integration. Switching tabs unmounts the view and takes the stack with it,
 * which is the behaviour a native tab bar has anyway.
 *
 * Anything inside can push: useMobileNav() gives a child the same push/pop the
 * root has, so a section can open a sub-section without threading callbacks.
 */

export interface MobileNavPage {
  /**
   * Which page this is. The stack holds an id, never rendered content.
   *
   * Content pushed as an element is frozen at the moment it was pushed: React
   * keeps handing that element the props it was created with, so every
   * controlled input inside a pushed page was stuck on the value its view had
   * when the row was tapped. Settings' font size could not be changed at all -
   * each keystroke was written to the backend and then overwritten in the DOM
   * by the stale value - and a Plot Grid thread's checkboxes never moved. An id
   * is resolved through renderPage on every render instead, so a pushed page
   * sees exactly the state the root would.
   */
  id: string
  /** Shown in the back bar of whatever this page pushes, and as the title. */
  title: string
}

interface MobileNavApi {
  push: (page: MobileNavPage) => void
  pop: () => void
  depth: number
}

const MobileNavContext = createContext<MobileNavApi | null>(null)

/** Push/pop from inside a page. Outside a MobileNav the calls are no-ops, so a
 *  view that is shared with the desktop does not have to branch. */
export function useMobileNav(): MobileNavApi {
  return useContext(MobileNavContext) ?? { push: () => {}, pop: () => {}, depth: 0 }
}

export function MobileNav({
  title,
  renderPage,
  children
}: {
  /** The root page's title, shown in the back bar of the first pushed page. */
  title: string
  /**
   * The content of a pushed page, by the id it was pushed under. Called during
   * render, from the view that owns this stack, so the page is built from the
   * state the view holds now rather than the state it held when the row was
   * tapped. Returning null for an id whose subject has gone (a deleted
   * plotline, a section a project close took away) leaves the page empty rather
   * than showing a stale copy of it.
   */
  renderPage: (id: string) => React.ReactNode
  children: React.ReactNode
}): React.JSX.Element {
  const [stack, setStack] = useState<MobileNavPage[]>([])
  const rootRef = useRef<HTMLDivElement>(null)
  /** Where each page below the top was scrolled to when it was left. */
  const offsets = useRef<number[]>([])

  // Which element is actually scrolling is not the same question in every view.
  // Settings scrolls in the tab's own .mobile-content; a Codex entry scrolls
  // inside itself, in .codex-detail, and .mobile-content never moves. So look
  // for a scrolling box within the view first and fall back to the tab's.
  const scroller = useCallback((): HTMLElement | null => {
    const root = rootRef.current
    if (!root) return null
    for (const el of Array.from(root.querySelectorAll<HTMLElement>('*'))) {
      const style = getComputedStyle(el)
      const scrolls = style.overflowY === 'auto' || style.overflowY === 'scroll'
      if (scrolls && el.scrollHeight > el.clientHeight + 1) return el
    }
    return (root.closest('.mobile-content') as HTMLElement | null) ?? null
  }, [])

  const push = useCallback(
    (page: MobileNavPage) => {
      offsets.current.push(scroller()?.scrollTop ?? 0)
      setStack((s) => [...s, page])
      // A new page starts at its top, once React has put it there.
      requestAnimationFrame(() => {
        const el = scroller()
        if (el) el.scrollTop = 0
      })
    },
    [scroller]
  )

  const pop = useCallback(() => {
    const restore = offsets.current.pop() ?? 0
    setStack((s) => s.slice(0, -1))
    // Back goes to where the writer was, not to the top. On a Codex entry the
    // rows sit below the fields, so landing at the top cost a scroll back down
    // for every editor opened.
    //
    // Two frames, because the page being returned to is remounted rather than
    // revealed: its scroller does not exist on the first frame, and cannot hold
    // an offset until its content has been laid out.
    requestAnimationFrame(() => {
      requestAnimationFrame(() => {
        const el = scroller()
        if (el) el.scrollTop = restore
      })
    })
  }, [scroller])

  const api = useMemo(() => ({ push, pop, depth: stack.length }), [push, pop, stack.length])

  const top = stack[stack.length - 1]
  // The back button names where it goes, as iOS does - the page below, or the
  // root's own title when there is only one page on the stack.
  const backTitle = stack.length > 1 ? stack[stack.length - 2].title : title

  return (
    <MobileNavContext.Provider value={api}>
      {/* The wrapper is always in the DOM so the scroller can be found from it,
          but at the root it is `display: contents` and has no box of its own -
          the view underneath keeps the layout it had before there was a stack. */}
      <div className={top ? 'mobile-nav' : 'mobile-nav-root'} ref={rootRef}>
        {top ? (
          <>
            <header className="mobile-nav-bar">
              <button type="button" className="mobile-nav-back" onClick={pop}>
                <ChevronLeft size={20} strokeWidth={2} />
                <span className="mobile-nav-back-label">{backTitle}</span>
              </button>
              <h1 className="mobile-nav-title">{top.title}</h1>
            </header>
            <div className="mobile-nav-page">{renderPage(top.id)}</div>
          </>
        ) : (
          children
        )}
      </div>
    </MobileNavContext.Provider>
  )
}

/** A grouped block of rows, with iOS's small caps header above it. */
export function MobileGroup({
  header,
  footer,
  children
}: {
  header?: string
  footer?: string
  children: React.ReactNode
}): React.JSX.Element {
  return (
    <section className="mobile-group">
      {header && <h2 className="mobile-group-header">{header}</h2>}
      <div className="mobile-group-body">{children}</div>
      {footer && <p className="mobile-group-footer">{footer}</p>}
    </section>
  )
}

/**
 * One row in a grouped list: a label, an optional value read off to the right,
 * and a chevron when tapping it opens something. Rendered as a button when it
 * acts and a plain row when it only states.
 */
export function MobileRow({
  label,
  value,
  onClick,
  children
}: {
  label: string
  /** The current setting, right-aligned, the way iOS states a value. */
  value?: string
  onClick?: () => void
  /** A control that lives in the row itself (a switch, a stepper). */
  children?: React.ReactNode
}): React.JSX.Element {
  const content = (
    <>
      <span className="mobile-row-label">{label}</span>
      {value !== undefined && <span className="mobile-row-value">{value}</span>}
      {children}
      {onClick && <ChevronRight size={18} strokeWidth={2} className="mobile-row-chevron" />}
    </>
  )
  return onClick ? (
    <button type="button" className="mobile-row" onClick={onClick}>
      {content}
    </button>
  ) : (
    <div className="mobile-row">{content}</div>
  )
}
