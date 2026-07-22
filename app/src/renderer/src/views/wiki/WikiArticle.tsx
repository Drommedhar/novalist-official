import { useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { SquarePen, MapPin, X } from 'lucide-react'
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

export function WikiArticle({ article }: { article: Article }): React.JSX.Element {
  const { t } = useTranslation()
  const index = useWikiStore((s) => s.index)
  const openArticle = useWikiStore((s) => s.openArticle)
  const navigateToMapPin = useShellStore((s) => s.navigateToMapPin)
  const [lightbox, setLightbox] = useState<string | null>(null)

  // id -> typeKey, so a click on a persisted `nv-entity-mention` span (which
  // stores only the id) can open the right article.
  const idToType = useMemo(() => {
    const map = new Map<string, string>()
    for (const scope of index ?? [])
      for (const group of scope.types)
        for (const entry of group.entries) map.set(entry.id, entry.typeKey)
    return map
  }, [index])

  // Close the image lightbox on Escape.
  useEffect(() => {
    if (!lightbox) return
    const onKey = (e: KeyboardEvent): void => {
      if (e.key === 'Escape') setLightbox(null)
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [lightbox])

  const onProseClick = (e: React.MouseEvent<HTMLDivElement>): void => {
    const el = (e.target as HTMLElement).closest('[data-entity-id]')
    if (!el) return
    const id = el.getAttribute('data-entity-id')
    const type = id ? idToType.get(id) : undefined
    if (id && type) {
      e.preventDefault()
      void openArticle(type, id)
    }
  }

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
  if (article.appearsWith.length > 0) toc.push({ id: 'appears-with', label: t('wiki.appearsWith') })
  if (article.plotlines.length > 0) toc.push({ id: 'plotlines', label: t('wiki.plotlines') })
  if (article.mapPins.length > 0) toc.push({ id: 'maps', label: t('wiki.maps') })
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
        <div className="wiki-article-content" onClick={onProseClick}>
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
              <div
                className="wiki-prose"
                // Section content is authored HTML from the Codex; rendered read-only.
                dangerouslySetInnerHTML={{ __html: section.content }}
              />
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

          <WikiAppearances appearances={article.appearances} id="appearances" />

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
