import { useEffect, useMemo, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { BookOpen, X } from 'lucide-react'
import Markdown from 'react-markdown'
import remarkGfm from 'remark-gfm'
import manualPages from 'virtual:novalist-manual'
import './help.css'

interface ManualPage {
  file: string
  title: string
  content: string
  /** Sort key: README (index) first, then by numeric NN prefix. */
  order: number
}

/** Derives the ordered page list from whatever `.md` files were bundled. */
function buildPages(): ManualPage[] {
  return Object.entries(manualPages)
    .map(([file, content]) => {
      const heading = content.match(/^#\s+(.+?)\s*$/m)
      const isIndex = file.toLowerCase() === 'readme.md'
      const prefix = file.match(/^(\d+)/)
      const order = isIndex ? -1 : prefix ? Number(prefix[1]) : 999
      return {
        file,
        content,
        title: heading ? heading[1].trim() : file.replace(/\.md$/i, ''),
        order
      }
    })
    .sort((a, b) => a.order - b.order || a.file.localeCompare(b.file))
}

/** Resolves a Markdown link target to a bundled page filename, or null. */
function resolvePageLink(href: string, pages: ManualPage[]): string | null {
  if (/^[a-z]+:/i.test(href) || href.startsWith('//')) return null
  const file = href.replace(/^\.\//, '').split(/[#?]/)[0]
  if (!file.toLowerCase().endsWith('.md')) return null
  const base = file.split('/').pop() ?? file
  const match = pages.find((p) => p.file.toLowerCase() === base.toLowerCase())
  return match ? match.file : null
}

export function HelpOverlay({ onClose }: { onClose(): void }): React.JSX.Element {
  const { t } = useTranslation()
  const pages = useMemo(() => buildPages(), [])
  const [query, setQuery] = useState('')
  const [active, setActive] = useState(
    () => pages.find((p) => p.order === -1)?.file ?? pages[0]?.file ?? ''
  )
  const contentRef = useRef<HTMLDivElement>(null)

  const filtered = useMemo(() => {
    const q = query.trim().toLowerCase()
    if (!q) return pages
    return pages.filter(
      (p) => p.title.toLowerCase().includes(q) || p.content.toLowerCase().includes(q)
    )
  }, [pages, query])

  const current = pages.find((p) => p.file === active) ?? pages[0]

  // Scroll the reading pane back to the top whenever the page changes.
  useEffect(() => {
    contentRef.current?.scrollTo({ top: 0 })
  }, [active])

  const navigate = (file: string): void => setActive(file)

  return (
    <div
      className="help-overlay"
      onPointerDown={(e) => e.target === e.currentTarget && onClose()}
    >
      <div
        className="help-card"
        role="dialog"
        aria-label={t('help.title')}
        onKeyDown={(e) => e.key === 'Escape' && onClose()}
      >
        <div className="help-sidebar">
          <div className="help-sidebar-head">
            <BookOpen size={16} strokeWidth={1.75} />
            <span className="help-title">{t('help.title')}</span>
          </div>
          <input
            className="help-search dialog-input"
            placeholder={t('help.searchPlaceholder')}
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            autoFocus
          />
          <div className="help-page-list">
            {filtered.length === 0 && <p className="help-empty">{t('help.noResults')}</p>}
            {filtered.map((p) => (
              <button
                key={p.file}
                className={`help-page-item${p.file === active ? ' active' : ''}`}
                onClick={() => navigate(p.file)}
              >
                {p.title}
              </button>
            ))}
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
                  a: ({ href, children, ...rest }) => {
                    const target = href ? resolvePageLink(href, pages) : null
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
