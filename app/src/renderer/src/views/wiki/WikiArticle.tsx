import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import Markdown, { defaultUrlTransform } from 'react-markdown'
import remarkGfm from 'remark-gfm'
import { SquarePen, MapPin, X, Sparkles, Loader2 } from 'lucide-react'
import { useWikiStore, type WikiArticle as Article, type WikiLead } from '../../stores/wikiStore'
import { useCodexStore } from '../../stores/codexStore'
import { useShellStore } from '../../stores/shellStore'
import { WikiInfobox, imageSrc } from './WikiInfobox'
import { WikiRelationships } from './WikiRelationships'
import { WikiAppearances } from './WikiAppearances'

const BUILT_IN_TYPE_KEYS: Record<string, string> = {
  character: 'codexHub.characters',
  location: 'codexHub.locations',
  item: 'codexHub.items',
  lore: 'codexHub.lore'
}

function typeLabel(article: Article, t: (key: string) => string): string {
  if (article.customTypeLabel) return article.customTypeLabel
  const key = BUILT_IN_TYPE_KEYS[article.typeKey]
  return key ? t(key) : article.typeKey
}

/** Builds the localized one-line descriptor from the lead parts. */
function descriptor(lead: WikiLead, t: (key: string, opts?: Record<string, string>) => string): string {
  if (!lead.primary) return ''
  if (!lead.secondary) return lead.primary
  switch (lead.secondaryConnector) {
    case 'dot':
      return `${lead.primary} · ${lead.secondary}`
    case 'in':
      return `${lead.primary} ${t('wiki.leadIn', { place: lead.secondary })}`
    case 'from':
      return `${lead.primary} ${t('wiki.leadFrom', { origin: lead.secondary })}`
    default:
      return lead.primary
  }
}

// Preserve the custom `nventity:` cross-link scheme (react-markdown's default
// transform would strip it); everything else goes through the safe default.
function proseUrlTransform(url: string): string {
  return url.startsWith('nventity:') ? url : defaultUrlTransform(url)
}

/** The text of a blockquote's first paragraph, for spotting a callout marker. */
function calloutText(children: React.ReactNode): string | null {
  const first = Array.isArray(children)
    ? children.find((c) => typeof c === 'object' && c !== null)
    : children
  const props = (first as { props?: { children?: React.ReactNode } } | undefined)?.props
  const inner = props?.children
  const text = Array.isArray(inner) ? inner[0] : inner
  return typeof text === 'string' ? text : null
}

/**
 * The same children with the `[!kind] Title` line taken off the front. The
 * marker is shown as the callout's own heading, so leaving it in the body
 * would print it twice.
 */
function stripCalloutMarker(children: React.ReactNode): React.ReactNode {
  const list = Array.isArray(children) ? children : [children]
  return list.map((child, index) => {
    if (index !== list.findIndex((c) => typeof c === 'object' && c !== null)) return child
    const element = child as {
      props?: { children?: React.ReactNode }
      type?: unknown
    }
    const inner = element?.props?.children
    const parts = Array.isArray(inner) ? [...inner] : [inner]
    if (typeof parts[0] === 'string') {
      const stripped = parts[0].replace(/^\[!\w+\]\s*.*(\n|$)/, '')
      if (stripped.trim().length === 0) parts.shift()
      else parts[0] = stripped
    }
    return <p key={index}>{parts}</p>
  })
}

export function WikiArticle({ article }: { article: Article }): React.JSX.Element {
  const { t } = useTranslation()
  const openArticle = useWikiStore((s) => s.openArticle)
  const navigateToMapPin = useShellStore((s) => s.navigateToMapPin)
  const regenerate = useWikiStore((s) => s.regenerate)
  const regenerating = useWikiStore((s) => s.regenerating)
  const regenerateError = useWikiStore((s) => s.regenerateError)
  const [lightbox, setLightbox] = useState<string | null>(null)

  // Renders section-prose links: an `nventity:{type}/{id}` href (produced by the
  // backend WikiProseLinker) becomes a click-through to that article; any other
  // href stays an ordinary external link.
  const proseComponents = {
    /**
     * A blockquote whose first line reads `[!note] Title` is a callout - the
     * convention Obsidian uses, which stays a plain quote anywhere that does
     * not know it, so a note is never turned into noise.
     */
    blockquote: ({ children }: { children?: React.ReactNode }): React.JSX.Element => {
      const text = calloutText(children)
      const match = text ? /^\[!(\w+)\]\s*(.*)$/.exec(text.trim()) : null
      if (!match) return <blockquote>{children}</blockquote>
      return (
        <div className="wiki-callout" data-kind={match[1].toLowerCase()}>
          {match[2].length > 0 && <div className="wiki-callout-title">{match[2]}</div>}
          <div className="wiki-callout-body">{stripCalloutMarker(children)}</div>
        </div>
      )
    },
    a: ({ href, children }: { href?: string; children?: React.ReactNode }): React.JSX.Element => {
      if (href && href.startsWith('nventity:')) {
        const rest = href.slice('nventity:'.length)
        const slash = rest.indexOf('/')
        const type = rest.slice(0, slash)
        const entityId = rest.slice(slash + 1)
        return (
          <button type="button" className="wiki-link wiki-prose-link" onClick={() => void openArticle(type, entityId)}>
            {children}
          </button>
        )
      }
      return (
        <a href={href} target="_blank" rel="noreferrer">
          {children}
        </a>
      )
    }
  }

  // Close the image lightbox on Escape.
  useEffect(() => {
    if (!lightbox) return
    const onKey = (e: KeyboardEvent): void => {
      if (e.key === 'Escape') setLightbox(null)
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [lightbox])

  const editInCodex = async (): Promise<void> => {
    await useCodexStore.getState().setType(article.typeKey)
    await useCodexStore.getState().select(article.id)
    useShellStore.getState().setMainView('codex')
  }

  const scrollTo = (id: string): void => {
    document.getElementById(id)?.scrollIntoView({ behavior: 'smooth', block: 'start' })
  }

  // Table-of-contents entries, in render order, for whatever sections exist.
  const toc: { id: string; label: string }[] = []
  article.sections.forEach((s, i) => toc.push({ id: `sec-${i}`, label: s.title || t('wiki.untitledSection') }))
  if (article.relationships.length > 0) toc.push({ id: 'relationships', label: t('wiki.relationships') })
  if (article.referencedBy.length > 0) toc.push({ id: 'referenced-by', label: t('wiki.referencedBy') })
  if (article.contains.length > 0) toc.push({ id: 'contains', label: t('wiki.contains') })
  if (article.appearsWith.length > 0) toc.push({ id: 'appears-with', label: t('wiki.appearsWith') })
  if (article.plotlines.length > 0) toc.push({ id: 'plotlines', label: t('wiki.plotlines') })
  if (article.mapPins.length > 0) toc.push({ id: 'maps', label: t('wiki.maps') })
  if (article.research.length > 0) toc.push({ id: 'research', label: t('wiki.research') })
  if (article.events.length > 0) toc.push({ id: 'events', label: t('wiki.events') })
  if (article.overrides.length > 0) toc.push({ id: 'overrides', label: t('wiki.changesOverTime') })
  if (article.appearances.length > 0) toc.push({ id: 'appearances', label: t('wiki.appearances') })

  const lead = descriptor(article.lead, t)
  const isEmpty =
    toc.length === 0 && !article.description && !lead && article.stats == null

  return (
    <article className="wiki-article">
      <header className="wiki-article-header">
        <div className="wiki-article-heading">
          <h1>{article.title}</h1>
          <div className="wiki-article-meta">
            <span className="wiki-type-chip">{typeLabel(article, t)}</span>
            <span className={`wiki-scope-chip${article.isWorldBible ? ' worldbible' : ''}`}>
              {article.isWorldBible ? t('wiki.scopeWorldBible') : t('wiki.scopeBook')}
            </span>
          </div>
        </div>
        <button type="button" className="wiki-edit-btn" onClick={() => void editInCodex()}>
          <SquarePen size={14} strokeWidth={1.75} /> {t('wiki.editInCodex')}
        </button>
      </header>

      <div className="wiki-article-body">
        <div className="wiki-article-content">
          {/* Lead: bold name, alternate names, one-line descriptor. */}
          <p className="wiki-lead-line">
            <strong>{article.title}</strong>
            {article.aliases.length > 0 && (
              <span className="wiki-aka">
                {' '}
                ({t('wiki.alsoKnownAs', { names: article.aliases.join(', ') })})
              </span>
            )}
            {lead && <span> {'—'} {lead}</span>}
          </p>
          {article.description && <p className="wiki-lead">{article.description}</p>}

          {(article.generatorAvailable || article.generated) && (
            <div className="wiki-summary">
              {article.generated && (
                <>
                  <div className="wiki-summary-head">
                    <span className="wiki-summary-label">{t('wiki.aiSummary')}</span>
                    {article.generated.stale && (
                      <span className="wiki-summary-stale">{t('wiki.summaryStale')}</span>
                    )}
                  </div>
                  <p className="wiki-summary-text">{article.generated.summary}</p>
                </>
              )}
              {article.generatorAvailable && (
                <div className="wiki-summary-actions">
                  <button
                    type="button"
                    className="wiki-summary-btn"
                    disabled={regenerating}
                    onClick={() => void regenerate()}
                  >
                    {regenerating ? (
                      <>
                        <Loader2 size={13} strokeWidth={1.75} className="wiki-spin" />{' '}
                        {t('wiki.generating')}
                      </>
                    ) : (
                      <>
                        <Sparkles size={13} strokeWidth={1.75} />{' '}
                        {article.generated ? t('wiki.regenerate') : t('wiki.generateSummary')}
                      </>
                    )}
                  </button>
                  {regenerateError && <span className="wiki-summary-error">{regenerateError}</span>}
                </div>
              )}
            </div>
          )}

          {article.stats && (
            <div className="wiki-stats">
              <Stat label={t('wiki.statAppearances')} value={String(article.stats.appearanceCount)} />
              <Stat label={t('wiki.statChapters')} value={String(article.stats.chapterCount)} />
              {article.stats.povSceneCount != null && (
                <Stat label={t('wiki.statPovScenes')} value={String(article.stats.povSceneCount)} />
              )}
              {article.stats.first && (
                <Stat
                  label={t('wiki.statFirst')}
                  value={article.stats.first.storyDate || article.stats.first.sceneTitle}
                />
              )}
              {article.stats.last && (
                <Stat
                  label={t('wiki.statLast')}
                  value={article.stats.last.storyDate || article.stats.last.sceneTitle}
                />
              )}
            </div>
          )}

          {toc.length > 1 && (
            <nav className="wiki-toc" aria-label={t('wiki.contents')}>
              <div className="wiki-toc-title">{t('wiki.contents')}</div>
              <ol>
                {toc.map((entry) => (
                  <li key={entry.id}>
                    <button type="button" className="wiki-link" onClick={() => scrollTo(entry.id)}>
                      {entry.label}
                    </button>
                  </li>
                ))}
              </ol>
            </nav>
          )}

          {article.sections.map((section, i) => (
            <section className="wiki-section" id={`sec-${i}`} key={`${section.title}-${i}`}>
              {section.title && <h2>{section.title}</h2>}
              {/* Section content is authored Markdown in the Codex; rendered read-only.
                  Entity references are pre-linked by the backend as `nventity:` links. */}
              <div className="wiki-prose">
                <Markdown
                  remarkPlugins={[remarkGfm]}
                  urlTransform={proseUrlTransform}
                  components={proseComponents}
                >
                  {section.content}
                </Markdown>
              </div>
            </section>
          ))}

          <WikiRelationships relationships={article.relationships} id="relationships" />

          {article.referencedBy.length > 0 && (
            <section className="wiki-section" id="referenced-by">
              <h2>{t('wiki.referencedBy')}</h2>
              <ul className="wiki-references">
                {article.referencedBy.map((ref, i) => (
                  <li key={`${ref.entityId}-${ref.role}-${i}`}>
                    <span className="wiki-rel-role">{ref.role}</span>
                    {ref.entityId && ref.typeKey ? (
                      <button
                        type="button"
                        className="wiki-link"
                        onClick={() => void openArticle(ref.typeKey!, ref.entityId!)}
                      >
                        {ref.name}
                      </button>
                    ) : (
                      <span className="wiki-rel-plain">{ref.name}</span>
                    )}
                  </li>
                ))}
              </ul>
            </section>
          )}

          {article.contains.length > 0 && (
            <section className="wiki-section" id="contains">
              <h2>{t('wiki.contains')}</h2>
              <div className="wiki-chips">
                {article.contains.map((child) => (
                  <button
                    key={child.entityId ?? child.name}
                    type="button"
                    className="wiki-chip"
                    onClick={() => void openArticle(child.typeKey!, child.entityId!)}
                  >
                    {child.name}
                  </button>
                ))}
              </div>
            </section>
          )}

          {article.appearsWith.length > 0 && (
            <section className="wiki-section" id="appears-with">
              <h2>{t('wiki.appearsWith')}</h2>
              <div className="wiki-chips">
                {article.appearsWith.map((co) => (
                  <button
                    key={co.entityId}
                    type="button"
                    className="wiki-chip"
                    onClick={() => void openArticle(co.typeKey, co.entityId)}
                  >
                    {co.name}
                    <span className="wiki-chip-count">{co.sharedScenes}</span>
                  </button>
                ))}
              </div>
            </section>
          )}

          {article.plotlines.length > 0 && (
            <section className="wiki-section" id="plotlines">
              <h2>{t('wiki.plotlines')}</h2>
              <div className="wiki-chips">
                {article.plotlines.map((plot) => (
                  <span key={plot.id} className="wiki-chip wiki-chip-static">
                    <span className="wiki-plot-dot" style={{ background: plot.color }} aria-hidden="true" />
                    {plot.name}
                  </span>
                ))}
              </div>
            </section>
          )}

          {article.mapPins.length > 0 && (
            <section className="wiki-section" id="maps">
              <h2>{t('wiki.maps')}</h2>
              <ul className="wiki-mappins">
                {article.mapPins.map((pin) => (
                  <li key={`${pin.mapId}-${pin.pinId}`}>
                    <button
                      type="button"
                      className="wiki-link wiki-mappin"
                      onClick={() => navigateToMapPin(pin.mapId, pin.pinId)}
                    >
                      <MapPin size={13} strokeWidth={1.75} />
                      {pin.pinLabel || pin.mapName}
                      <span className="wiki-mappin-map">{pin.mapName}</span>
                    </button>
                  </li>
                ))}
              </ul>
            </section>
          )}

          {article.research.length > 0 && (
            <section className="wiki-section" id="research">
              <h2>{t('wiki.research')}</h2>
              <ul className="wiki-references">
                {article.research.map((item) => (
                  <li key={item.id}>
                    <span className="wiki-rel-role">
                      {t(`research.type${item.type}`, { defaultValue: item.type })}
                    </span>
                    <button
                      type="button"
                      className="wiki-link"
                      onClick={() => useShellStore.getState().navigateToResearch(item.id)}
                    >
                      {item.title}
                    </button>
                  </li>
                ))}
              </ul>
            </section>
          )}

          {article.events.length > 0 && (
            <section className="wiki-section" id="events">
              <h2>{t('wiki.events')}</h2>
              <ul className="wiki-events">
                {article.events.map((event) => (
                  <li key={event.id}>
                    <button
                      type="button"
                      className="wiki-event"
                      onClick={() => useShellStore.getState().setMainView('timeline')}
                    >
                      {event.date && <span className="wiki-event-date">{event.date}</span>}
                      <span className="wiki-event-title">{event.title}</span>
                      {event.description && (
                        <span className="wiki-event-desc">{event.description}</span>
                      )}
                    </button>
                  </li>
                ))}
              </ul>
            </section>
          )}

          {article.overrides.length > 0 && (
            <section className="wiki-section" id="overrides">
              <h2>{t('wiki.changesOverTime')}</h2>
              <ul className="wiki-overrides">
                {article.overrides.map((ovr, i) => (
                  <li key={`${ovr.scope}-${i}`} className="wiki-override">
                    <div className="wiki-override-scope">{ovr.scope}</div>

                    {ovr.images.length > 0 && (
                      <div className="wiki-override-images">
                        {ovr.images.map((image, j) => (
                          <figure key={`${image.url}-${j}`}>
                            <button
                              type="button"
                              className="wiki-image-btn"
                              title={t('wiki.viewImage')}
                              onClick={() => setLightbox(imageSrc(image.url))}
                            >
                              <img src={imageSrc(image.url)} alt="" />
                            </button>
                            {image.name && <figcaption>{image.name}</figcaption>}
                          </figure>
                        ))}
                      </div>
                    )}

                    {ovr.changes.length > 0 && (
                      <dl className="wiki-override-changes">
                        {ovr.changes.map((change, j) => (
                          <div className="wiki-override-row" key={`${change.labelKey ?? change.literalLabel}-${j}`}>
                            <dt>{change.labelKey ? t(change.labelKey) : change.literalLabel}</dt>
                            <dd>{change.value}</dd>
                          </div>
                        ))}
                      </dl>
                    )}

                    {ovr.relationships.length > 0 && (
                      <ul className="wiki-relationships">
                        {ovr.relationships.map((rel, j) => (
                          <li key={`${rel.role}-${j}`}>
                            <span className="wiki-rel-role">{rel.role}</span>
                            <span className="wiki-rel-targets">
                              {rel.targets.map((target, k) => (
                                <span key={`${target.name}-${k}`}>
                                  {k > 0 && ', '}
                                  {target.entityId && target.typeKey ? (
                                    <button
                                      type="button"
                                      className="wiki-link"
                                      onClick={() => void openArticle(target.typeKey!, target.entityId!)}
                                    >
                                      {target.name}
                                    </button>
                                  ) : (
                                    <span className="wiki-rel-plain">{target.name}</span>
                                  )}
                                </span>
                              ))}
                            </span>
                          </li>
                        ))}
                      </ul>
                    )}

                    {ovr.aliases.length > 0 && (
                      <div className="wiki-override-note">
                        {t('wiki.alsoKnownAs', { names: ovr.aliases.join(', ') })}
                      </div>
                    )}

                    {ovr.sectionTitles.length > 0 && (
                      <div className="wiki-override-note">
                        {t('wiki.sectionsChanged', { names: ovr.sectionTitles.join(', ') })}
                      </div>
                    )}
                  </li>
                ))}
              </ul>
            </section>
          )}

          <WikiAppearances
            appearances={article.appearances}
            id="appearances"
            bookName={article.bookName}
            multipleBooks={article.multipleBooks}
          />

          {/* An article with content but no appearances is easy to misread as
              broken; say why the timeline is absent. */}
          {!isEmpty && article.appearances.length === 0 && (
            <p className="wiki-empty-body">{t('wiki.noAppearances')}</p>
          )}

          {isEmpty && <p className="wiki-empty-body">{t('wiki.emptyArticle')}</p>}
        </div>

        <WikiInfobox infobox={article.infobox} onImageClick={setLightbox} />
      </div>

      {lightbox && (
        <div
          className="wiki-lightbox"
          role="dialog"
          aria-modal="true"
          aria-label={t('wiki.viewImage')}
          onClick={() => setLightbox(null)}
        >
          <button
            type="button"
            className="wiki-lightbox-close"
            aria-label={t('hostBridge.dismiss')}
            onClick={() => setLightbox(null)}
          >
            <X size={20} strokeWidth={1.75} />
          </button>
          {/* Stop propagation so clicking the image itself does not close. */}
          <img src={lightbox} alt="" onClick={(e) => e.stopPropagation()} />
        </div>
      )}
    </article>
  )
}

function Stat({ label, value }: { label: string; value: string }): React.JSX.Element {
  return (
    <div className="wiki-stat">
      <span className="wiki-stat-value">{value}</span>
      <span className="wiki-stat-label">{label}</span>
    </div>
  )
}
