import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { FileDown } from 'lucide-react'
import { rpc } from '../../rpc/client'
import { useProjectStore } from '../../stores/projectStore'
import './export.css'

const FORMATS: { format: string; extension: string; labelKey: string }[] = [
  { format: 'Epub', extension: '.epub', labelKey: 'export.formatEpub' },
  { format: 'Docx', extension: '.docx', labelKey: 'export.formatDocx' },
  { format: 'Pdf', extension: '.pdf', labelKey: 'export.formatPdf' },
  { format: 'Markdown', extension: '.md', labelKey: 'export.formatMarkdown' },
  { format: 'FinalDraft', extension: '.fdx', labelKey: 'export.formatFinalDraft' },
  { format: 'LaTeX', extension: '.tex', labelKey: 'export.formatLatex' },
  { format: 'Codex', extension: '.md', labelKey: 'export.formatCodex' }
]

interface PresetDto {
  id: string
  displayName: string
  description: string
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
  const [format, setFormat] = useState('Epub')
  const [presetId, setPresetId] = useState('default')
  const [presets, setPresets] = useState<PresetDto[]>([])
  const [extFormats, setExtFormats] = useState<ExtensionFormatDto[]>([])
  const [smf, setSmf] = useState(false)
  const [title, setTitle] = useState(projectName ?? '')
  const [author, setAuthor] = useState('')
  const [includeTitlePage, setIncludeTitlePage] = useState(true)
  const [selected, setSelected] = useState<Set<string>>(new Set())
  const [initialized, setInitialized] = useState(false)
  const [busy, setBusy] = useState(false)
  const [result, setResult] = useState<string | null>(null)

  useEffect(() => {
    void rpc.request<PresetDto[]>('export/presets').then(setPresets)
    void rpc.request<ExtensionFormatDto[]>('export/extensionFormats').then(setExtFormats)
  }, [])

  // Select every chapter once they have loaded.
  useEffect(() => {
    if (!initialized && chapters.length > 0) {
      setSelected(new Set(chapters.map((c) => c.guid)))
      setInitialized(true)
    }
  }, [chapters, initialized])

  const isCodex = format === 'Codex'
  const isDocxPdf = format === 'Docx' || format === 'Pdf'
  const extFormat = extFormats.find((f) => f.formatKey === format)
  const chaptersVisible = !isCodex && extFormat === undefined
  const preset = smf && isDocxPdf ? 'shunn-manuscript' : presetId
  const activePreset = presets.find((p) => p.id === preset)

  const toggle = (guid: string, checked: boolean): void => {
    setSelected((prev) => {
      const next = new Set(prev)
      if (checked) next.add(guid)
      else next.delete(guid)
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
        preset,
        smf && isDocxPdf
      ])
      setResult(exported.success ? t('export.exportSuccess') : t('export.exportFailed'))
    } catch {
      setResult(t('export.exportFailed'))
    } finally {
      setBusy(false)
    }
  }

  const exportDisabled = busy || (chaptersVisible && selected.size === 0)

  return (
    <div className="dashboard export-view">
      <h1 className="dashboard-title">{t('shell.view.export')}</h1>
      <div className="dashboard-card export-card">
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
            {FORMATS.map((f) => (
              <option key={f.format} value={f.format}>
                {t(f.labelKey)}
              </option>
            ))}
            {extFormats.map((f) => (
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
            value={preset}
            disabled={smf && isDocxPdf}
            onChange={(e) => setPresetId(e.target.value)}
          >
            {presets.map((p) => (
              <option key={p.id} value={p.id}>
                {p.displayName}
              </option>
            ))}
          </select>
          {activePreset && <span className="export-preset-desc">{activePreset.description}</span>}
        </div>

        {isDocxPdf && (
          <label className="relationships-toggle export-toggle">
            <input type="checkbox" checked={smf} onChange={(e) => setSmf(e.target.checked)} />
            {t('export.smfToggle')}
          </label>
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

        <label className="relationships-toggle export-toggle">
          <input
            type="checkbox"
            checked={includeTitlePage}
            onChange={(e) => setIncludeTitlePage(e.target.checked)}
          />
          {t('export.includeTitlePage')}
        </label>

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

        <button className="start-open export-run" disabled={exportDisabled} onClick={() => void run()}>
          <FileDown size={15} strokeWidth={2} />
          {busy ? t('export.exporting') : t('export.exportAction')}
        </button>
        {result && <p className="inspector-meta export-result">{result}</p>}
      </div>
    </div>
  )
}
