import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { FileDown } from 'lucide-react'
import { rpc } from '../../rpc/client'
import { ReviewImportDialog } from '../../shell/ReviewImportDialog'
import { BookMatterPanel } from './BookMatterPanel'
import { PublishingPanel } from './PublishingPanel'
import { ExportLayoutPanel } from './ExportLayoutPanel'
import { useProjectStore } from '../../stores/projectStore'
import './export.css'

/**
 * What is being exported, kept apart from what file it comes out as.
 *
 * These were one dropdown, which meant "Codex (Markdown)" sat among the file
 * formats as if it were one - two different questions answered by one control.
 */
const CONTENTS = [
  { key: 'manuscript', labelKey: 'export.contentManuscript' },
  { key: 'codex', labelKey: 'export.contentCodex' }
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
  { format: 'CodexPdf', extension: '.pdf', labelKey: 'export.formatPdf', content: 'codex' }
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

interface ExtensionFormatDto {
  formatKey: string
  displayName: string
  fileExtension: string
}

export function ExportView(): React.JSX.Element {
  const { t } = useTranslation()
  const projectName = useProjectStore((s) => s.projectName)
  const chapters = useProjectStore((s) => s.chapters)
  const [content, setContent] = useState<Content>('manuscript')
  // One remembered format per content, so switching across and back does not
  // silently reset the writer's choice.
  const [formats, setFormats] = useState<Record<Content, string>>({
    manuscript: 'Epub',
    codex: 'Codex'
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
  const [reviewOpen, setReviewOpen] = useState(false)
  const [selected, setSelected] = useState<Set<string>>(new Set())
  const [initialized, setInitialized] = useState(false)
  const [entities, setEntities] = useState<Record<string, EntityOption[]>>({})
  const [entitiesLoaded, setEntitiesLoaded] = useState(false)
  const [selectedEntities, setSelectedEntities] = useState<Set<string>>(new Set())
  const [entityQuery, setEntityQuery] = useState('')
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
  const extFormat = extFormats.find((f) => f.formatKey === format)
  const chaptersVisible = !isCodex && extFormat === undefined

  // Load the codex entities the first time a codex format is picked; every
  // entry starts selected so the default export matches the old behaviour.
  useEffect(() => {
    if (!isCodex || entitiesLoaded) return
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
  }, [isCodex, entitiesLoaded])

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
        isCodex ? [...selectedEntities] : null,
        isCodex ? codexLabels() : null,
        includeCover
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
    (isCodex && allEntities.length > 0 && selectedEntities.size === 0)

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

        <label className="relationships-toggle export-toggle">
          <input
            type="checkbox"
            checked={includeTitlePage}
            onChange={(e) => setIncludeTitlePage(e.target.checked)}
          />
          {t('export.includeTitlePage')}
        </label>

        {/* Only EPUB and PDF render a cover; the other formats have nowhere to
            put one, so the control would be a lie. */}
        {(format === 'Epub' || format === 'Pdf') && (
          <label className="relationships-toggle export-toggle">
            <input
              type="checkbox"
              checked={includeCover}
              onChange={(e) => setIncludeCover(e.target.checked)}
            />
            {t('export.includeCover')}
          </label>
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

        {isCodex && allEntities.length > 0 && (
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
        <details className="export-matter">
          <summary>{t('matter.title')}</summary>
          <BookMatterPanel />
        </details>

        {/* What a shop and a distributor need, beyond title and author. */}
        <details className="export-matter">
          <summary>{t('publishing.title')}</summary>
          <PublishingPanel />
        </details>

        {/* Page geometry, separators and ebook CSS for whichever layout is
            picked above, rather than a second dropdown listing the same ones. */}
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
