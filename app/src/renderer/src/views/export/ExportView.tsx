import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { FileDown } from 'lucide-react'
import { rpc } from '../../rpc/client'
import { ReviewImportDialog } from '../../shell/ReviewImportDialog'
import { BookMatterPanel } from './BookMatterPanel'
import { PublishingPanel } from './PublishingPanel'
import { ReplacementsPanel } from './ReplacementsPanel'
import { ExportLayoutPanel } from './ExportLayoutPanel'
import { useProjectStore } from '../../stores/projectStore'
import { useStageStore } from '../../stores/stageStore'
import './export.css'

/**
 * What is being exported, kept apart from what file it comes out as.
 *
 * These were one dropdown, which meant "Codex (Markdown)" sat among the file
 * formats as if it were one - two different questions answered by one control.
 */
const CONTENTS = [
  { key: 'manuscript', labelKey: 'export.contentManuscript' },
  { key: 'codex', labelKey: 'export.contentCodex' },
  // Everything Novalist writes is prose or a document. Nothing machine-readable
  // left the project, so an outline could only reach a spreadsheet by retyping.
  { key: 'data', labelKey: 'export.contentData' },
  // Compiled out of what the writer already recorded. Every scene carried a
  // synopsis and a POV, and neither could be read as a whole.
  { key: 'report', labelKey: 'export.contentReport' }
] as const
type Content = (typeof CONTENTS)[number]['key']

const FORMATS: { format: string; extension: string; labelKey: string; content: Content }[] = [
  { format: 'Epub', extension: '.epub', labelKey: 'export.formatEpub', content: 'manuscript' },
  { format: 'Docx', extension: '.docx', labelKey: 'export.formatDocx', content: 'manuscript' },
  { format: 'Pdf', extension: '.pdf', labelKey: 'export.formatPdf', content: 'manuscript' },
  { format: 'Markdown', extension: '.md', labelKey: 'export.formatMarkdown', content: 'manuscript' },
  { format: 'FinalDraft', extension: '.fdx', labelKey: 'export.formatFinalDraft', content: 'manuscript' },
  { format: 'LaTeX', extension: '.tex', labelKey: 'export.formatLatex', content: 'manuscript' },
  { format: 'Codex', extension: '.md', labelKey: 'export.formatMarkdown', content: 'codex' },
  { format: 'CodexPdf', extension: '.pdf', labelKey: 'export.formatPdf', content: 'codex' },
  { format: 'Csv', extension: '.csv', labelKey: 'export.formatCsv', content: 'data' },
  { format: 'Json', extension: '.json', labelKey: 'export.formatJson', content: 'data' },
  { format: 'CodexCsv', extension: '.csv', labelKey: 'export.formatCodexCsv', content: 'data' },
  { format: 'Opml', extension: '.opml', labelKey: 'export.formatOpml', content: 'data' },
  { format: 'WorldJson', extension: '.json', labelKey: 'export.formatWorldJson', content: 'data' },
  { format: 'WorldHtml', extension: '.html', labelKey: 'export.formatWorldHtml', content: 'data' },
  {
    format: 'SynopsisReport',
    extension: '.md',
    labelKey: 'export.formatSynopsisReport',
    content: 'report'
  },
  { format: 'PovReport', extension: '.md', labelKey: 'export.formatPovReport', content: 'report' }
]

/** Codex entity kinds, in the order the export renders them. */
const ENTITY_KINDS: { kind: string; labelKey: string }[] = [
  { kind: 'character', labelKey: 'codexHub.characters' },
  { kind: 'location', labelKey: 'codexHub.locations' },
  { kind: 'item', labelKey: 'codexHub.items' },
  { kind: 'lore', labelKey: 'codexHub.lore' }
]

interface EntityOption {
  key: string
  name: string
}

/** Whatever a picker needs; the layout editor reads the rest of the record. */
interface PresetDto {
  id: string
  displayName: string
  description: string
  isCustom: boolean
}

/** What the current selection would produce, from the same compile the export runs. */
interface PreviewDto {
  chapters: number
  scenes: number
  words: number
  characters: number
  pages: number
  /** Exact only on the Normseite grid; an estimate everywhere else. */
  pagesAreExact: boolean
  undescribedImages: number
}

interface ExtensionFormatDto {
  formatKey: string
  displayName: string
  fileExtension: string
  supportsCover: boolean
}

export function ExportView(): React.JSX.Element {
  const { t } = useTranslation()
  const projectName = useProjectStore((s) => s.projectName)
  const chapters = useProjectStore((s) => s.chapters)
  const books = useProjectStore((s) => s.books)
  const activeBookId = useProjectStore((s) => s.activeBookId)
  const otherBooks = books.filter((b) => b.id !== activeBookId)
  const [content, setContent] = useState<Content>('manuscript')
  // One remembered format per content, so switching across and back does not
  // silently reset the writer's choice.
  const [formats, setFormats] = useState<Record<Content, string>>({
    manuscript: 'Epub',
    codex: 'Codex',
    data: 'Csv',
    report: 'SynopsisReport'
  })
  const format = formats[content]
  const setFormat = (next: string): void => setFormats({ ...formats, [content]: next })
  const [presetId, setPresetId] = useState('default')
  const [presets, setPresets] = useState<PresetDto[]>([])
  const [extFormats, setExtFormats] = useState<ExtensionFormatDto[]>([])
  const [title, setTitle] = useState(projectName ?? '')
  const [author, setAuthor] = useState('')
  const [includeTitlePage, setIncludeTitlePage] = useState(true)
  // Off for Shunn: a submission manuscript does not carry a cover.
  const [includeCover, setIncludeCover] = useState(true)
  // A world page that lists the villain's real name beside everything else
  // is worse than no world page at all.
  const [forReaders, setForReaders] = useState(false)
  const [tocDepth, setTocDepth] = useState(1)
  const [tocTitle, setTocTitle] = useState('')
  const [referenceDoc, setReferenceDoc] = useState('')
  // Null means every part, which is what a codex export always carried.
  const [codexParts, setCodexParts] = useState<Set<string>>(
    new Set(['images', 'fields', 'relationships', 'sections'])
  )
  const [sectionTitles, setSectionTitles] = useState<string[]>([])
  const [pickedSections, setPickedSections] = useState<Set<string>>(new Set())
  const [retailers, setRetailers] = useState<{ key: string; name: string }[]>([])
  const [retailerKey, setRetailerKey] = useState('')
  // Empty means every stage, which is what an export that names no filter has
  // always done and has to keep doing.
  const [stageFilter, setStageFilter] = useState<Set<string>>(new Set())
  const stages = useStageStore((st) => st.stages)
  const [preview, setPreview] = useState<PreviewDto | null>(null)
  const [reviewOpen, setReviewOpen] = useState(false)
  const [selected, setSelected] = useState<Set<string>>(new Set())
  const [initialized, setInitialized] = useState(false)
  const [entities, setEntities] = useState<Record<string, EntityOption[]>>({})
  const [entitiesLoaded, setEntitiesLoaded] = useState(false)
  const [selectedEntities, setSelectedEntities] = useState<Set<string>>(new Set())
  const [entityQuery, setEntityQuery] = useState('')
  /**
   * Further books to append after this one, for a box set.
   *
   * Empty is one book, which is what every export did before. Only offered on
   * manuscript formats: a codex or a data export is already project-wide.
   */
  const [extraBooks, setExtraBooks] = useState<Set<string>>(new Set())
  const [busy, setBusy] = useState(false)
  const [result, setResult] = useState<string | null>(null)

  useEffect(() => {
    // The same list the layout editor writes to, so a layout the writer
    // authored is pickable here the moment it exists.
    void rpc.request<PresetDto[]>('exportPresets/list').then(setPresets)
    void rpc.request<ExtensionFormatDto[]>('export/extensionFormats').then(setExtFormats)
  }, [])

  // Select every chapter once they have loaded.
  useEffect(() => {
    if (!initialized && chapters.length > 0) {
      setSelected(new Set(chapters.map((c) => c.guid)))
      setInitialized(true)
    }
  }, [chapters, initialized])

  const isCodex = content === 'codex'
  const isData = content === 'data' || content === 'report'
  // The Codex rides along in JSON, where it can nest. A single sheet cannot
  // hold a scene list and a character list without one of them being wrong.
  const entitiesVisible = isCodex || format === 'Json'
  const extFormat = extFormats.find((f) => f.formatKey === format)
  // A contributed format is given the selection now, so hiding the list from it
  // would be the app deciding the writer cannot send somebody three chapters in
  // anything but a built-in format.
  const chaptersVisible = !isCodex

  // What the current selection would actually produce. Recomputed whenever a
  // choice that changes it changes, so the writer never exports blind.
  useEffect(() => {
    // A metadata sheet has no pages and no compiled word count, and reporting
    // the manuscript's would be answering a question nobody asked.
    if (!chaptersVisible || isData) {
      setPreview(null)
      return
    }
    let current = true
    void rpc
      .request<PreviewDto>('export/preview', [[...selected], presetId, [...stageFilter]])
      .then((result) => {
        if (current) setPreview(result)
      })
      .catch(() => {
        if (current) setPreview(null)
      })
    return () => {
      current = false
    }
  }, [chaptersVisible, isData, selected, presetId, stageFilter])

  // Load the codex entities the first time a codex format is picked; every
  // entry starts selected so the default export matches the old behaviour.
  useEffect(() => {
    if (!entitiesVisible || entitiesLoaded) return
    setEntitiesLoaded(true)
    void Promise.all(
      ENTITY_KINDS.map(async ({ kind }) => {
        const list = await rpc
          .request<{ id: string; name: string }[]>('entities/list', [kind])
          .catch(() => [])
        const sorted = list
          .map((e) => ({ key: `${kind}:${e.id}`, name: e.name }))
          .sort((a, b) => a.name.localeCompare(b.name, undefined, { sensitivity: 'base' }))
        return [kind, sorted] as const
      })
    ).then((loaded) => {
      setEntities(Object.fromEntries(loaded))
      setSelectedEntities(new Set(loaded.flatMap(([, list]) => list.map((e) => e.key))))
    })
  }, [entitiesVisible, entitiesLoaded])

  // The stores this book has links for, so a build can be made for one.
  useEffect(() => {
    void rpc
      .request<{ key: string; name: string }[]>('export/retailers')
      .then(setRetailers)
      .catch(() => setRetailers([]))
  }, [])

  // The titles this project actually uses, so the picker names them rather than
  // asking for them to be typed the same way twice.
  useEffect(() => {
    if (!isCodex) return
    void rpc
      .request<string[]>('export/codexSections')
      .then((titles) => {
        setSectionTitles(titles)
        setPickedSections(new Set(titles))
      })
      .catch(() => setSectionTitles([]))
  }, [isCodex])

  const allEntities = ENTITY_KINDS.flatMap(({ kind }) => entities[kind] ?? [])
  // Same grouping and name order the export itself writes, filtered by the search box.
  const needle = entityQuery.trim().toLocaleLowerCase()
  const visibleEntities = ENTITY_KINDS.map(({ kind, labelKey }) => ({
    kind,
    labelKey,
    list: (entities[kind] ?? []).filter((e) => e.name.toLocaleLowerCase().includes(needle))
  })).filter((group) => group.list.length > 0)
  const visibleKeys = visibleEntities.flatMap((group) => group.list.map((e) => e.key))
  const activePreset = presets.find((p) => p.id === presetId)

  const toggle = (guid: string, checked: boolean): void => {
    setSelected((prev) => {
      const next = new Set(prev)
      if (checked) next.add(guid)
      else next.delete(guid)
      return next
    })
  }

  // The codex writers have no translations of their own; the fixed labels they
  // print come from here, in the interface language.
  const codexLabels = (): Record<string, string> => ({
    characters: t('codexHub.characters'),
    locations: t('codexHub.locations'),
    items: t('codexHub.items'),
    lore: t('codexHub.lore'),
    relationships: t('export.codexLabel.relationships'),
    role: t('export.codexLabel.role'),
    age: t('export.codexLabel.age'),
    gender: t('export.codexLabel.gender'),
    group: t('export.codexLabel.group'),
    eyes: t('export.codexLabel.eyes'),
    hair: t('export.codexLabel.hair'),
    height: t('export.codexLabel.height'),
    build: t('export.codexLabel.build'),
    skin: t('export.codexLabel.skin'),
    notable: t('export.codexLabel.notable'),
    type: t('export.codexLabel.type'),
    description: t('export.codexLabel.description')
  })

  const toggleEntity = (key: string, checked: boolean): void => {
    setSelectedEntities((prev) => {
      const next = new Set(prev)
      if (checked) next.add(key)
      else next.delete(key)
      return next
    })
  }

  const run = async (): Promise<void> => {
    const extension = extFormat?.fileExtension ?? FORMATS.find((f) => f.format === format)?.extension ?? ''
    const output = await window.novalist.saveFile(`${title || 'manuscript'}${extension}`)
    if (!output) return
    setBusy(true)
    setResult(null)
    try {
      const exported = await rpc.request<{ outputPath: string; success: boolean }>('export/run', [
        format,
        output,
        title,
        author,
        includeTitlePage,
        chaptersVisible ? [...selected] : [],
        presetId,
        entitiesVisible ? [...selectedEntities] : null,
        isCodex ? codexLabels() : null,
        includeCover,
        [...stageFilter],
        tocDepth,
        tocTitle,
        referenceDoc,
        isCodex ? [...codexParts] : null,
        // Naming every title is the same as naming none, and sending null keeps
        // the payload the size it was.
        isCodex && pickedSections.size < sectionTitles.length ? [...pickedSections] : null,
        retailerKey || null,
        chaptersVisible && extraBooks.size > 0 ? [...extraBooks] : null,
        forReaders
      ])
      setResult(exported.success ? t('export.exportSuccess') : t('export.exportFailed'))
    } catch {
      setResult(t('export.exportFailed'))
    } finally {
      setBusy(false)
    }
  }

  const exportDisabled =
    busy ||
    (chaptersVisible && selected.size === 0) ||
    (entitiesVisible && allEntities.length > 0 && selectedEntities.size === 0)

  return (
    <div className="dashboard export-view">
      <h1 className="dashboard-title">{t('shell.view.export')}</h1>
      <div className="dashboard-card export-card">
        <div className="export-field">
          <label className="inspector-label" htmlFor="export-content">
            {t('export.content')}
          </label>
          <select
            id="export-content"
            className="dialog-input"
            value={content}
            onChange={(e) => setContent(e.target.value as Content)}
          >
            {CONTENTS.map((c) => (
              <option key={c.key} value={c.key}>
                {t(c.labelKey)}
              </option>
            ))}
          </select>
        </div>

        <div className="export-field">
          <label className="inspector-label" htmlFor="export-format">
            {t('export.format')}
          </label>
          <select
            id="export-format"
            className="dialog-input"
            value={format}
            onChange={(e) => setFormat(e.target.value)}
          >
            {FORMATS.filter((f) => f.content === content).map((f) => (
              <option key={f.format} value={f.format}>
                {t(f.labelKey)}
              </option>
            ))}
            {/* An extension writes the manuscript out; nothing contributes a
                codex writer, so those belong under the manuscript only. */}
            {content === 'manuscript' &&
              extFormats.map((f) => (
                <option key={f.formatKey} value={f.formatKey}>
                  {f.displayName}
                </option>
              ))}
          </select>
        </div>

        {/* A layout is page geometry and typography; a metadata file has
            neither, so offering one would be a control that changes nothing. */}
        {!isData && (
        <div className="export-field">
          <label className="inspector-label" htmlFor="export-preset">
            {t('export.preset')}
          </label>
          <select
            id="export-preset"
            className="dialog-input"
            value={presetId}
            onChange={(e) => setPresetId(e.target.value)}
          >
            {presets.map((p) => (
              <option key={p.id} value={p.id}>
                {p.displayName}
                {p.isCustom ? '' : ` (${t('layout.builtIn')})`}
              </option>
            ))}
          </select>
          {activePreset?.description && (
            <span className="export-preset-desc">{activePreset.description}</span>
          )}
        </div>
        )}

        <div className="export-field">
          <label className="inspector-label" htmlFor="export-title">
            {t('export.title')}
          </label>
          <input
            id="export-title"
            className="dialog-input"
            value={title}
            onChange={(e) => setTitle(e.target.value)}
          />
        </div>

        <div className="export-field">
          <label className="inspector-label" htmlFor="export-author">
            {t('export.author')}
          </label>
          <input
            id="export-author"
            className="dialog-input"
            value={author}
            onChange={(e) => setAuthor(e.target.value)}
          />
        </div>

        {!isData && (
          <label className="relationships-toggle export-toggle">
            <input
              type="checkbox"
              checked={includeTitlePage}
              onChange={(e) => setIncludeTitlePage(e.target.checked)}
            />
            {t('export.includeTitlePage')}
          </label>
        )}

        {/* Shown only where a cover actually lands in the file. A control that
            changes nothing is worse than no control, so a contributed format has
            to say it can hold one. */}
        {(format === 'Epub' ||
          format === 'Pdf' ||
          extFormats.some((f) => f.formatKey === format && f.supportsCover)) && (
          <label className="relationships-toggle export-toggle">
            <input
              type="checkbox"
              checked={includeCover}
              onChange={(e) => setIncludeCover(e.target.checked)}
            />
            {t('export.includeCover')}
          </label>
        )}

        {/* Only on the formats that carry the world out of the app. On a
            manuscript export there is nothing for it to hide. */}
        {(format === 'WorldHtml' || format === 'WorldJson' || format === 'Json'
          || format === 'Codex' || format === 'CodexPdf' || format === 'CodexCsv') && (
          <label className="relationships-toggle export-toggle">
            <input
              type="checkbox"
              checked={forReaders}
              onChange={(e) => setForReaders(e.target.checked)}
            />
            {t('export.forReaders')}
          </label>
        )}

        {/* How deep the contents list goes, and what it is called. A flat
            chapter list is right for a novel and wrong for a collection, and
            "Table of Contents" is wrong in every language but English. */}
        {format === 'Epub' && (
          <div className="export-field-row">
            <label className="export-field">
              <span className="export-field-label">{t('export.tocDepth')}</span>
              <select
                className="inspector-input"
                value={tocDepth}
                onChange={(e) => setTocDepth(Number(e.target.value))}
              >
                <option value={1}>{t('export.tocDepthChapters')}</option>
                <option value={2}>{t('export.tocDepthScenes')}</option>
              </select>
            </label>
            <label className="export-field">
              <span className="export-field-label">{t('export.tocTitle')}</span>
              <input
                className="inspector-input"
                value={tocTitle}
                placeholder={t('export.tocTitlePlaceholder')}
                onChange={(e) => setTocTitle(e.target.value)}
              />
            </label>
          </div>
        )}

        {/* A house style arrives as a styled Word file, not as a list of
            settings. Point at it once and the export comes out in it. */}
        {format === 'Docx' && (
          <div className="export-field-row">
            <label className="export-field export-field-grow">
              <span className="export-field-label">{t('export.referenceDoc')}</span>
              <input
                className="inspector-input"
                value={referenceDoc}
                placeholder={t('export.referenceDocPlaceholder')}
                onChange={(e) => setReferenceDoc(e.target.value)}
              />
            </label>
            <button
              type="button"
              className="btn-secondary"
              onClick={() => {
                void window.novalist
                  .pickFile(t('export.referenceDoc'), 'all')
                  .then((chosen) => chosen && setReferenceDoc(chosen))
              }}
            >
              {t('export.referenceDocChoose')}
            </button>
            {referenceDoc && (
              <button type="button" className="btn-secondary" onClick={() => setReferenceDoc('')}>
                {t('export.referenceDocClear')}
              </button>
            )}
          </div>
        )}

        {/* Which parts of an entry leave the project. A series bible that has
            to leave the portraits out, or a packet that wants the names and
            nothing else, was an all-or-nothing choice per entry before this. */}
        {isCodex && (
          <div className="export-field-row">
            {(['images', 'fields', 'relationships', 'sections'] as const).map((part) => (
              <label key={part} className="relationships-toggle export-toggle">
                <input
                  type="checkbox"
                  checked={codexParts.has(part)}
                  onChange={(e) =>
                    setCodexParts((prev) => {
                      const next = new Set(prev)
                      if (e.target.checked) next.add(part)
                      else next.delete(part)
                      return next
                    })
                  }
                />
                {t(`export.codexPart_${part}`)}
              </label>
            ))}
          </div>
        )}
        {isCodex && codexParts.has('sections') && sectionTitles.length > 0 && (
          <div className="export-field-row">
            <span className="export-field-label">{t('export.codexSections')}</span>
            {sectionTitles.map((title) => (
              <label key={title} className="relationships-toggle export-toggle">
                <input
                  type="checkbox"
                  checked={pickedSections.has(title)}
                  onChange={(e) =>
                    setPickedSections((prev) => {
                      const next = new Set(prev)
                      if (e.target.checked) next.add(title)
                      else next.delete(title)
                      return next
                    })
                  }
                />
                {title}
              </label>
            ))}
          </div>
        )}

        {/* A build for one shop. Back matter written with <$storename> and
            <$storelink> resolves to that shop, so a reader is sent back where
            they bought it rather than to a competitor. */}
        {retailers.length > 0 && !isCodex && !isData && (
          <label className="export-field">
            <span className="export-field-label">{t('export.buildFor')}</span>
            <select
              className="inspector-input"
              value={retailerKey}
              onChange={(e) => setRetailerKey(e.target.value)}
            >
              <option value="">{t('export.buildForNone')}</option>
              {retailers.map((r) => (
                <option key={r.key} value={r.key}>
                  {r.name || r.key}
                </option>
              ))}
            </select>
          </label>
        )}

        {preview && (
          <p className="export-preview" aria-live="polite">
            {t('export.previewCounts', {
              chapters: preview.chapters,
              scenes: preview.scenes,
              words: preview.words.toLocaleString()
            })}{' '}
            {t(preview.pagesAreExact ? 'export.previewPagesExact' : 'export.previewPages', {
              pages: preview.pages
            })}
            {preview.undescribedImages > 0 && (
              <>
                {' '}
                <span className="export-warning">
                  {t('export.previewUndescribed', { count: preview.undescribedImages })}
                </span>
              </>
            )}
          </p>
        )}

        {chaptersVisible && stages.length > 0 && (
          <>
            <div className="inspector-label">{t('export.stageFilter')}</div>
            <div className="export-stage-filter">
              <label className="relationships-toggle">
                <input
                  type="checkbox"
                  checked={stageFilter.size === 0}
                  onChange={() => setStageFilter(new Set())}
                />
                {t('export.stageFilterAll')}
              </label>
              {stages.map((stage) => (
                <label key={stage.key} className="relationships-toggle">
                  <input
                    type="checkbox"
                    checked={stageFilter.has(stage.key)}
                    onChange={(e) => {
                      const next = new Set(stageFilter)
                      if (e.target.checked) next.add(stage.key)
                      else next.delete(stage.key)
                      setStageFilter(next)
                    }}
                  />
                  {stage.label}
                </label>
              ))}
            </div>
            <div className="settings-hint">{t('export.excludedNote')}</div>
          </>
        )}

        {/* A series in one file. The chapter list belongs to the open book, so
            a further volume comes in whole rather than chapter by chapter. */}
        {chaptersVisible && otherBooks.length > 0 && (
          <>
            <div className="export-chapters-header">
              <span className="export-field-label">{t('export.alsoInclude')}</span>
            </div>
            <div className="export-stage-filter">
              {otherBooks.map((book) => (
                <label key={book.id} className="relationships-toggle">
                  <input
                    type="checkbox"
                    checked={extraBooks.has(book.id)}
                    onChange={(e) => {
                      const next = new Set(extraBooks)
                      if (e.target.checked) next.add(book.id)
                      else next.delete(book.id)
                      setExtraBooks(next)
                    }}
                  />
                  {book.name}
                </label>
              ))}
            </div>
          </>
        )}

        {chaptersVisible && (
          <>
            <div className="export-chapters-header">
              <div className="inspector-label">{t('export.selectChapters')}</div>
              <div className="export-select-buttons">
                <button
                  className="export-inline-btn"
                  onClick={() => setSelected(new Set(chapters.map((c) => c.guid)))}
                >
                  {t('export.selectAll')}
                </button>
                <button className="export-inline-btn" onClick={() => setSelected(new Set())}>
                  {t('export.selectNone')}
                </button>
              </div>
            </div>
            <div className="export-chapters">
              {chapters.map((chapter) => (
                <label key={chapter.guid} className="relationships-toggle">
                  <input
                    type="checkbox"
                    checked={selected.has(chapter.guid)}
                    onChange={(e) => toggle(chapter.guid, e.target.checked)}
                  />
                  {chapter.title}
                </label>
              ))}
            </div>
            <span className="export-count">
              {t('export.selectedOfTotal', { selected: selected.size, total: chapters.length })}
            </span>
          </>
        )}

        {entitiesVisible && allEntities.length > 0 && (
          <>
            <div className="export-chapters-header">
              <div className="inspector-label">{t('export.selectEntities')}</div>
              <div className="export-select-buttons">
                <button
                  className="export-inline-btn"
                  onClick={() => setSelectedEntities((prev) => new Set([...prev, ...visibleKeys]))}
                >
                  {t('export.selectAll')}
                </button>
                <button
                  className="export-inline-btn"
                  onClick={() =>
                    setSelectedEntities((prev) => {
                      const next = new Set(prev)
                      for (const key of visibleKeys) next.delete(key)
                      return next
                    })
                  }
                >
                  {t('export.selectNone')}
                </button>
              </div>
            </div>
            <input
              className="dialog-input export-entity-search"
              type="search"
              value={entityQuery}
              placeholder={t('export.searchEntities')}
              onChange={(e) => setEntityQuery(e.target.value)}
            />
            <div className="export-chapters">
              {visibleEntities.map(({ kind, labelKey, list }) => (
                <div key={kind} className="export-entity-group">
                  <div className="export-entity-group-title">{t(labelKey)}</div>
                  {list.map((entity) => (
                    <label key={entity.key} className="relationships-toggle">
                      <input
                        type="checkbox"
                        checked={selectedEntities.has(entity.key)}
                        onChange={(e) => toggleEntity(entity.key, e.target.checked)}
                      />
                      {entity.name}
                    </label>
                  ))}
                </div>
              ))}
              {visibleEntities.length === 0 && (
                <span className="export-count">{t('export.noEntityMatches')}</span>
              )}
            </div>
            <span className="export-count">
              {t('export.selectedOfTotal', {
                selected: selectedEntities.size,
                total: allEntities.length
              })}
            </span>
          </>
        )}

        <button className="start-open export-run" disabled={exportDisabled} onClick={() => void run()}>
          <FileDown size={15} strokeWidth={2} />
          {busy ? t('export.exporting') : t('export.exportAction')}
        </button>
        {result && <p className="inspector-meta export-result">{result}</p>}

        {/* The pages around the story. Typed, so each is set its own way. */}
        {!isData && (
        <details className="export-matter">
          <summary>{t('matter.title')}</summary>
          <BookMatterPanel />
        </details>
        )}

        {/* What a shop and a distributor need, beyond title and author. */}
        {!isData && (
        <details className="export-matter">
          <summary>{t('publishing.title')}</summary>
          <PublishingPanel />
          {/* Applied to the output only, so a rule can be turned off without
              anything to undo - unlike Find and Replace, which rewrites the
              scenes themselves. */}
          <ReplacementsPanel />
        </details>
        )}

        {/* Page geometry, separators and ebook CSS for whichever layout is
            picked above, rather than a second dropdown listing the same ones. */}
        {!isData && (
        <details className="export-matter">
          <summary>{t('layout.title')}</summary>
          <ExportLayoutPanel
            selectedId={presetId}
            onLayouts={(all, select) => {
              setPresets(all)
              if (select !== undefined) setPresetId(select)
            }}
          />
        </details>
        )}

        {/* The other half of the round trip: a DOCX goes out to an editor and
            their marked-up copy comes back here. */}
        <div className="settings-button-row export-review-row">
          <button className="dialog-button" onClick={() => setReviewOpen(true)}>
            {t('review.openAction')}
          </button>
          <span className="settings-hint">{t('review.openHint')}</span>
        </div>
      </div>

      {reviewOpen && <ReviewImportDialog onClose={() => setReviewOpen(false)} />}
    </div>
  )
}
