import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { FileDown } from 'lucide-react'
import { rpc } from '../../rpc/client'
import { useProjectStore } from '../../stores/projectStore'

const FORMATS: { format: string; extension: string }[] = [
  { format: 'Epub', extension: '.epub' },
  { format: 'Docx', extension: '.docx' },
  { format: 'Pdf', extension: '.pdf' },
  { format: 'Markdown', extension: '.md' },
  { format: 'FinalDraft', extension: '.fdx' },
  { format: 'LaTeX', extension: '.tex' },
  { format: 'Codex', extension: '.md' }
]

export function ExportView(): React.JSX.Element {
  const { t } = useTranslation()
  const projectName = useProjectStore((s) => s.projectName)
  const chapters = useProjectStore((s) => s.chapters)
  const [format, setFormat] = useState('Epub')
  const [title, setTitle] = useState(projectName ?? '')
  const [author, setAuthor] = useState('')
  const [includeTitlePage, setIncludeTitlePage] = useState(true)
  const [selected, setSelected] = useState<Set<string>>(new Set())
  const [busy, setBusy] = useState(false)
  const [result, setResult] = useState<string | null>(null)

  const run = async (): Promise<void> => {
    const extension = FORMATS.find((f) => f.format === format)?.extension ?? ''
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
        [...selected]
      ])
      setResult(
        exported.success
          ? t('export.exportSuccess', { 0: exported.outputPath })
          : t('export.exportFailed')
      )
    } catch {
      setResult(t('export.exportFailed'))
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="dashboard export-view">
      <h1 className="dashboard-title">{t('shell.view.export')}</h1>
      <div className="dashboard-card export-card">
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
              {t(`export.format${f.format === 'LaTeX' ? 'Latex' : f.format}`)}
            </option>
          ))}
        </select>
        <label className="inspector-label" htmlFor="export-title">
          {t('export.title')}
        </label>
        <input
          id="export-title"
          className="dialog-input"
          value={title}
          onChange={(e) => setTitle(e.target.value)}
        />
        <label className="inspector-label" htmlFor="export-author">
          {t('export.author')}
        </label>
        <input
          id="export-author"
          className="dialog-input"
          value={author}
          onChange={(e) => setAuthor(e.target.value)}
        />
        <label className="relationships-toggle export-toggle">
          <input
            type="checkbox"
            checked={includeTitlePage}
            onChange={(e) => setIncludeTitlePage(e.target.checked)}
          />
          {t('export.includeTitlePage')}
        </label>
        <div className="inspector-label">{t('export.selectChapters')}</div>
        <div className="export-chapters">
          {chapters.map((chapter) => (
            <label key={chapter.guid} className="relationships-toggle">
              <input
                type="checkbox"
                checked={selected.size === 0 || selected.has(chapter.guid)}
                onChange={(e) => {
                  const next = new Set(selected.size === 0 ? chapters.map((c) => c.guid) : selected)
                  if (e.target.checked) next.add(chapter.guid)
                  else next.delete(chapter.guid)
                  setSelected(next.size === chapters.length ? new Set() : next)
                }}
              />
              {chapter.title}
            </label>
          ))}
        </div>
        <button className="start-open export-run" disabled={busy} onClick={() => void run()}>
          <FileDown size={15} strokeWidth={2} />
          {busy ? t('export.exporting') : t('export.exportButton')}
        </button>
        {result && <p className="inspector-meta export-result">{result}</p>}
      </div>
    </div>
  )
}
