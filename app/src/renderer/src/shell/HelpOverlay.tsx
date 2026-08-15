import { useEffect, useMemo, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { BookOpen, X } from 'lucide-react'
import Markdown from 'react-markdown'
import remarkGfm from 'remark-gfm'
import manualPages from 'virtual:novalist-manual'
import manualImages from 'virtual:novalist-manual-images'
import type { ManualTarget } from './helpTargets'
import './help.css'

export interface ManualHeading {
  depth: number
  text: string
  anchor: string
  line: number
}

export interface ManualPage {
  file: string
  title: string
  content: string
  headings: ManualHeading[]
  /** Sort key: README (index) first, then by numeric NN prefix. */
  order: number
}

export interface ResolvedManualLink {
  file: string
  anchor?: string
}

interface ManualSearchResult extends ResolvedManualLink {
  key: string
  pageTitle: string
  heading?: string
  snippet?: string
}

interface HelpLocation extends ResolvedManualLink {
  serial: number
  focusDestination: boolean
}

const FENCE = /^\s*((?:\x60){3,}|~{3,})/

/** Matches the heading ids GitHub-style Markdown links in the manual use. */
export function slugifyManualHeading(value: string): string {
  return value
    .trim()
    .toLowerCase()
    .replace(/\[([^\]]+)]\([^)]+\)/g, '$1')
    .replace(/<[^>]*>/g, '')
    .replace(/[\x60*_~]/g, '')
    .replace(/[^\p{L}\p{N}\s-]/gu, '')
    .replace(/\s/g, '-')
}

export function extractManualHeadings(content: string): ManualHeading[] {
  const headings: ManualHeading[] = []
  const occurrences = new Map<string, number>()
  let fence: string | null = null

  content.split(/\r?\n/).forEach((line, index) => {
    const fenceMatch = line.match(FENCE)
    if (fenceMatch) {
      const marker = fenceMatch[1][0]
      fence = fence === null ? marker : fence === marker ? null : fence
      return
    }
    if (fence !== null) return

    const match = line.match(/^(#{1,6})\s+(.+?)\s*#*\s*$/)
    if (!match) return
    const text = match[2].trim()
    const base = slugifyManualHeading(text)
    const seen = occurrences.get(base) ?? 0
    occurrences.set(base, seen + 1)
    headings.push({
      depth: match[1].length,
      text,
      anchor: seen === 0 ? base : `${base}-${seen}`,
      line: index + 1
    })
  })
  return headings
}

/** Derives the ordered page list from whatever `.md` files were bundled. */
export function buildManualPages(source: Record<string, string> = manualPages): ManualPage[] {
  return Object.entries(source)
    .map(([file, content]) => {
      const headings = extractManualHeadings(content)
      const isIndex = file.toLowerCase() === 'readme.md'
      const prefix = file.match(/^(\d+)/)
      const order = isIndex ? -1 : prefix ? Number(prefix[1]) : 999
      return {
        file,
        content,
        headings,
        title: headings[0]?.text ?? file.replace(/\.md$/i, ''),
        order
      }
    })
    .sort((a, b) => a.order - b.order || a.file.localeCompare(b.file))
}

function decodeFragment(value: string): string {
  try {
    return decodeURIComponent(value)
  } catch {
    return value
  }
}

/** Resolves same-page and cross-page Markdown links without losing fragments. */
export function resolveManualLink(
  href: string,
  currentFile: string,
  pages: ManualPage[]
): ResolvedManualLink | null {
  if (/^[a-z]+:/i.test(href) || href.startsWith('//')) return null
  const hashAt = href.indexOf('#')
  const beforeHash = hashAt >= 0 ? href.slice(0, hashAt) : href
  const fragment = hashAt >= 0 ? decodeFragment(href.slice(hashAt + 1)) : ''
  const path = beforeHash.split('?')[0].replace(/^\.\//, '')
  const base = path ? (path.split('/').pop() ?? path) : currentFile
  if (!base.toLowerCase().endsWith('.md')) return null
  const match = pages.find((p) => p.file.toLowerCase() === base.toLowerCase())
  return match ? { file: match.file, ...(fragment ? { anchor: fragment } : {}) } : null
}

function plainMarkdown(value: string): string {
  return value
    .replace(/!\[([^\]]*)]\([^)]+\)/g, '$1')
    .replace(/\[([^\]]+)]\([^)]+\)/g, '$1')
    .replace(/<[^>]*>/g, ' ')
    .replace(/[#>*_\x60~|]/g, ' ')
    .replace(/\s+/g, ' ')
    .trim()
}

function snippetAround(value: string, query: string): string {
  const plain = plainMarkdown(value)
  if (!plain) return ''
  const at = plain.toLowerCase().indexOf(query)
  const start = Math.max(0, at < 0 ? 0 : at - 48)
  const end = Math.min(plain.length, start + 150)
  return `${start > 0 ? '…' : ''}${plain.slice(start, end)}${end < plain.length ? '…' : ''}`
}

function searchManual(pages: ManualPage[], query: string): ManualSearchResult[] {
  const q = query.trim().toLowerCase()
  if (!q) {
    return pages.map((page) => ({
      key: page.file,
      file: page.file,
      pageTitle: page.title
    }))
  }

  const results: ManualSearchResult[] = []
  for (const page of pages) {
    if (page.title.toLowerCase().includes(q)) {
      results.push({
        key: `${page.file}:page`,
        file: page.file,
        pageTitle: page.title,
        snippet: snippetAround(page.content, q)
      })
    }

    page.headings.forEach((heading, index) => {
      if (heading.depth === 1 && page.title.toLowerCase().includes(q)) return
      const next = page.headings[index + 1]
      const lines = page.content.split(/\r?\n/)
      const section = lines.slice(heading.line - 1, next ? next.line - 1 : undefined).join('\n')
      if (!heading.text.toLowerCase().includes(q) && !section.toLowerCase().includes(q)) return
      results.push({
        key: `${page.file}#${heading.anchor}`,
        file: page.file,
        anchor: heading.anchor,
        pageTitle: page.title,
        heading: heading.depth === 1 ? undefined : heading.text,
        snippet: snippetAround(section, q)
      })
    })
  }
  return results
}

function locationForTarget(
  target: ManualTarget | undefined,
  pages: ManualPage[]
): ResolvedManualLink {
  const fallback = pages.find((page) => page.order === -1) ?? pages[0]
  const match = target
    ? pages.find((page) => page.file.toLowerCase() === target.file.toLowerCase())
    : fallback
  return {
    file: match?.file ?? '',
    ...(match && target?.anchor ? { anchor: target.anchor.replace(/^#/, '') } : {})
  }
}

function headingAnchorAtLine(page: ManualPage, line: number | undefined): string | undefined {
  return line === undefined ? undefined : page.headings.find((heading) => heading.line === line)?.anchor
}

export interface HelpOverlayProps {
  onClose(): void
  /** Open directly on the current view/setting/feature rather than the index. */
  initialTarget?: ManualTarget
}

export function HelpOverlay({ onClose, initialTarget }: HelpOverlayProps): React.JSX.Element {
  const { t } = useTranslation()
  const pages = useMemo(() => buildManualPages(), [])
  const [query, setQuery] = useState(() => initialTarget?.query ?? '')
  const [location, setLocation] = useState<HelpLocation>(() => ({
    ...locationForTarget(initialTarget, pages),
    serial: 0,
    focusDestination: false
  }))
  const dialogRef = useRef<HTMLDivElement>(null)
  const searchRef = useRef<HTMLInputElement>(null)
  const contentRef = useRef<HTMLDivElement>(null)
  const previousFocusRef = useRef<HTMLElement | null>(null)
  const closeRef = useRef(onClose)
  closeRef.current = onClose

  const results = useMemo(() => searchManual(pages, query), [pages, query])
  const current = pages.find((page) => page.file === location.file) ?? pages[0]

  const navigate = (target: ResolvedManualLink, focusDestination = true): void => {
    setLocation((before) => ({
      ...target,
      serial: before.serial + 1,
      focusDestination
    }))
  }

  // A caller can reuse the mounted overlay for a new contextual help request.
  useEffect(() => {
    setQuery(initialTarget?.query ?? '')
    navigate(locationForTarget(initialTarget, pages), false)
    // The primitive fields are the target's identity; object identity is not.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [initialTarget?.file, initialTarget?.anchor, initialTarget?.query, pages])

  // Scroll after Markdown has rendered. Search/link navigation also transfers
  // focus to the destination heading; an initial contextual target keeps focus
  // in Search so the dialog remains immediately keyboard-operable.
  useEffect(() => {
    const pane = contentRef.current
    if (!pane) return
    const frame = window.requestAnimationFrame(() => {
      const destination = location.anchor
        ? Array.from(pane.querySelectorAll<HTMLElement>('[id]')).find(
            (element) => element.id === location.anchor
          )
        : pane.querySelector<HTMLElement>('h1')
      if (destination) {
        destination.scrollIntoView({ block: 'start' })
        if (location.focusDestination) destination.focus({ preventScroll: true })
      } else {
        pane.scrollTo({ top: 0 })
      }
    })
    return () => window.cancelAnimationFrame(frame)
  }, [current?.file, location.anchor, location.focusDestination, location.serial])

  // Modal keyboard behavior: initial focus, Escape, a contained Tab cycle, and
  // return focus to the control that opened Help.
  useEffect(() => {
    if (previousFocusRef.current === null && document.activeElement instanceof HTMLElement)
      previousFocusRef.current = document.activeElement
    const frame = window.requestAnimationFrame(() => searchRef.current?.focus())
    const onKeyDown = (event: KeyboardEvent): void => {
      if (event.key === 'Escape') {
        event.preventDefault()
        closeRef.current()
        return
      }
      if (event.key !== 'Tab' || !dialogRef.current) return
      const focusable = Array.from(
        dialogRef.current.querySelectorAll<HTMLElement>(
          'button:not([disabled]), input:not([disabled]), a[href], [tabindex]:not([tabindex="-1"])'
        )
      ).filter((element) => element.getAttribute('aria-hidden') !== 'true')
      if (focusable.length === 0) return
      const first = focusable[0]
      const last = focusable[focusable.length - 1]
      const active = document.activeElement
      if (event.shiftKey ? active === first || !dialogRef.current.contains(active) : active === last) {
        event.preventDefault()
        ;(event.shiftKey ? last : first).focus()
      }
    }
    document.addEventListener('keydown', onKeyDown)
    return () => {
      window.cancelAnimationFrame(frame)
      document.removeEventListener('keydown', onKeyDown)
      const previous = previousFocusRef.current
      if (previous?.isConnected) previous.focus()
    }
  }, [])

  return (
    <div
      className="help-overlay"
      onPointerDown={(e) => e.target === e.currentTarget && onClose()}
    >
      <div
        ref={dialogRef}
        className="help-card"
        role="dialog"
        aria-modal="true"
        aria-label={t('help.title')}
      >
        <div className="help-sidebar">
          <div className="help-sidebar-head">
            <BookOpen size={16} strokeWidth={1.75} />
            <span className="help-title">{t('help.title')}</span>
          </div>
          <input
            ref={searchRef}
            className="help-search dialog-input"
            placeholder={t('help.searchPlaceholder')}
            aria-label={t('help.searchPlaceholder')}
            value={query}
            onChange={(e) => setQuery(e.target.value)}
          />
          <div className="help-page-list" aria-live="polite">
            {results.length === 0 && <p className="help-empty">{t('help.noResults')}</p>}
            {results.map((result) => {
              const active =
                result.file === location.file &&
                (query.trim() === '' || result.anchor === location.anchor)
              return (
              <button
                type="button"
                key={result.key}
                className={`help-page-item${active ? ' active' : ''}`}
                aria-current={active ? 'location' : undefined}
                onClick={() =>
                  navigate({
                    file: result.file,
                    ...(result.anchor ? { anchor: result.anchor } : {})
                  })
                }
              >
                <span className="help-result-page">{result.pageTitle}</span>
                {result.heading && (
                  <span className="help-result-heading">{result.heading}</span>
                )}
                {query.trim() && result.snippet && (
                  <span className="help-result-snippet">{result.snippet}</span>
                )}
              </button>
              )
            })}
          </div>
        </div>
        <div className="help-main">
          <button className="help-close" onClick={onClose} aria-label={t('help.close')}>
            <X size={18} strokeWidth={1.75} />
          </button>
          <div className="help-content" ref={contentRef}>
            {current && (
              <Markdown
                remarkPlugins={[remarkGfm]}
                components={{
                  h1: ({ node, children, ...props }) => (
                    <h1
                      {...props}
                      id={headingAnchorAtLine(current, node?.position?.start.line)}
                      tabIndex={-1}
                    >
                      {children}
                    </h1>
                  ),
                  h2: ({ node, children, ...props }) => (
                    <h2
                      {...props}
                      id={headingAnchorAtLine(current, node?.position?.start.line)}
                      tabIndex={-1}
                    >
                      {children}
                    </h2>
                  ),
                  h3: ({ node, children, ...props }) => (
                    <h3
                      {...props}
                      id={headingAnchorAtLine(current, node?.position?.start.line)}
                      tabIndex={-1}
                    >
                      {children}
                    </h3>
                  ),
                  h4: ({ node, children, ...props }) => (
                    <h4
                      {...props}
                      id={headingAnchorAtLine(current, node?.position?.start.line)}
                      tabIndex={-1}
                    >
                      {children}
                    </h4>
                  ),
                  // Manual images are referenced as `images/<file>.png`; resolve
                  // them to the inlined data URIs (no real asset origin exists in
                  // the packaged app). Unknown sources render nothing.
                  img: ({ src, alt }) => {
                    const key = typeof src === 'string' ? (src.split('/').pop() ?? '') : ''
                    const resolved = manualImages[key]
                    return resolved ? (
                      <img className="help-image" src={resolved} alt={alt ?? ''} />
                    ) : null
                  },
                  a: ({ href, children, ...rest }) => {
                    const target = href ? resolveManualLink(href, current.file, pages) : null
                    if (target) {
                      return (
                        <a
                          href={href}
                          onClick={(e) => {
                            e.preventDefault()
                            navigate(target)
                          }}
                        >
                          {children}
                        </a>
                      )
                    }
                    return (
                      <a
                        {...rest}
                        href={href}
                        onClick={(e) => {
                          e.preventDefault()
                          if (href && /^https?:/i.test(href)) void window.novalist.openExternal(href)
                        }}
                      >
                        {children}
                      </a>
                    )
                  }
                }}
              >
                {current.content}
              </Markdown>
            )}
          </div>
        </div>
      </div>
    </div>
  )
}
