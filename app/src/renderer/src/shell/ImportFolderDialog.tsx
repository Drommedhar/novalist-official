import { useEffect, useId, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { FileDown, FolderOpen } from 'lucide-react'
import { rpc } from '../rpc/client'
import { useProjectStore, type ProjectStateDto } from '../stores/projectStore'
import { useCodexStore } from '../stores/codexStore'
import { flushPendingWrites } from '../stores/pendingWrites'
import { ImportApiPanel } from './ImportApiPanel'
import { useMotionPresent } from './MotionPresence'
import './ImportFolderDialog.css'

interface Scan {
  sessionId: string
  total: number
  unsupported: number
  folders: number
}
interface Folder {
  path: string
  files: number
  total: number
}
interface FolderPage {
  total: number
  items: Folder[]
}
interface Progress {
  total: number
  processed: number
  imported: number
  skipped: number
  failed: number
  done: boolean
  issues: { path: string; kind: string }[]
}

const PAGE_SIZE = 30
const TARGETS = [
  ['skip', 'folderImport.skip'],
  ['scene', 'folderImport.scenes'],
  ['character', 'codexHub.characters'],
  ['location', 'codexHub.locations'],
  ['item', 'codexHub.items'],
  ['lore', 'codexHub.lore'],
  ['research', 'folderImport.research']
] as const

function inheritedTarget(path: string, rules: Record<string, string>, fallback: string): string {
  let parent = path.includes('/') ? path.slice(0, path.lastIndexOf('/')) : ''
  while (parent) {
    if (rules[parent]) return rules[parent]
    parent = parent.includes('/') ? parent.slice(0, parent.lastIndexOf('/')) : ''
  }
  return fallback
}

export function ImportFolderDialog({ onClose, initialTarget = 'skip' }: {
  onClose(): void
  initialTarget?: string
}): React.JSX.Element {
  const { t } = useTranslation()
  const present = useMotionPresent()
  const titleId = useId()
  const filesTitleId = useId()
  const schemaTitleId = useId()
  const sourceId = useId()
  const [folder, setFolder] = useState('')
  const [scan, setScan] = useState<Scan | null>(null)
  const [page, setPage] = useState<FolderPage | null>(null)
  const [query, setQuery] = useState('')
  const [offset, setOffset] = useState(0)
  const [defaultTarget, setDefaultTarget] = useState(initialTarget)
  const [rules, setRules] = useState<Record<string, string>>({})
  const [customTypes, setCustomTypes] = useState<{ typeKey: string; displayNamePlural: string }[]>([])
  const [tag, setTag] = useState('')
  const [progress, setProgress] = useState<Progress | null>(null)
  const [busy, setBusy] = useState(false)
  const [loadingFolders, setLoadingFolders] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [stopped, setStopped] = useState(false)
  const [exporting, setExporting] = useState(false)
  const [schemaSaved, setSchemaSaved] = useState(false)
  const stop = useRef(false)
  const session = useRef<string | null>(null)
  const locked = busy || progress !== null

  useEffect(() => {
    let active = true
    void rpc.request<typeof customTypes>('entities/customTypes').then(types => {
      if (active) setCustomTypes(types)
    }).catch(() => {})
    return () => {
      active = false
      stop.current = true
      if (session.current) void rpc.request('folderImport/release', [session.current]).catch(() => {})
    }
  }, [])

  useEffect(() => {
    if (!scan) return
    let active = true
    setLoadingFolders(true)
    const timer = setTimeout(() => {
      void rpc.request<FolderPage>('folderImport/folders', [scan.sessionId, query, offset, PAGE_SIZE])
        .then(result => { if (active) setPage(result) })
        .catch(e => { if (active) setError(String(e)) })
        .finally(() => { if (active) setLoadingFolders(false) })
    }, 200)
    return () => { active = false; clearTimeout(timer) }
  }, [scan, query, offset])

  useEffect(() => {
    if (!present) return
    const onKey = (event: KeyboardEvent): void => {
      if (event.key !== 'Escape') return
      event.preventDefault()
      if (exporting) return
      if (busy) stop.current = true
      else onClose()
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [busy, exporting, onClose, present])

  const options = [
    ...TARGETS.map(([value, key]) => ({ value, label: t(key) })),
    ...customTypes.map(type => ({ value: type.typeKey, label: type.displayNamePlural }))
  ]
  const labelFor = (target: string): string => options.find(option => option.value === target)?.label ?? target

  const exportSchema = async (): Promise<void> => {
    setExporting(true)
    setSchemaSaved(false)
    setError(null)
    try {
      const output = await window.novalist.saveFile('novalist-import.schema.json')
      if (!output) return
      await flushPendingWrites()
      await useProjectStore.getState().flushPendingSave()
      await rpc.request('folderImport/exportSchema', [output])
      setSchemaSaved(true)
    } catch (e) {
      setError(String(e))
    } finally {
      setExporting(false)
    }
  }

  const browse = async (): Promise<void> => {
    setBusy(true)
    setError(null)
    try {
      const picked = await window.novalist.pickFolder(t('folderImport.choose'))
      if (!picked) return
      if (session.current) await rpc.request('folderImport/release', [session.current])
      session.current = null
      setFolder(picked)
      setScan(null)
      setPage(null)
      setProgress(null)
      setRules({})
      setQuery('')
      setOffset(0)
      setStopped(false)
      const result = await rpc.request<Scan>('folderImport/scan', [picked])
      session.current = result.sessionId
      setScan(result)
    } catch (e) {
      setError(String(e))
    } finally {
      setBusy(false)
    }
  }

  const run = async (): Promise<void> => {
    if (!scan || progress?.done) return
    stop.current = false
    setBusy(true)
    setStopped(false)
    setError(null)
    try {
      await flushPendingWrites()
      await useProjectStore.getState().flushPendingSave()
      let status = progress ?? await rpc.request<Progress>('folderImport/start', [
        scan.sessionId, defaultTarget, rules, t('folderImport.sectionTitle'), tag
      ])
      setProgress(status)
      while (!status.done && !stop.current) {
        status = await rpc.request<Progress>('folderImport/batch', [scan.sessionId])
        setProgress(status)
      }
      setStopped(!status.done)
    } catch (e) {
      setError(String(e))
      setStopped(true)
    } finally {
      try {
        useProjectStore.getState().applyState(await rpc.request<ProjectStateDto>('project/getState'))
        await useCodexStore.getState().refresh()
      } catch (e) {
        setError(String(e))
      }
      setBusy(false)
    }
  }

  const hasTarget = defaultTarget !== 'skip' || Object.values(rules).some(target => target !== 'skip')
  return (
    <div className="dialog-overlay" onPointerDown={event => {
      if (event.target === event.currentTarget && !busy && !exporting) onClose()
    }}>
      <div className="dialog-card folder-import-card" role="dialog" aria-modal="true" aria-labelledby={titleId}>
        <h2 className="dialog-title" id={titleId}>{t('folderImport.title')}</h2>
        <div className="folder-import-body">
          <section className="folder-import-section" aria-labelledby={filesTitleId}>
            <div className="folder-import-section-header">
              <h3 id={filesTitleId}>{t('folderImport.filesTitle')}</h3>
              <p className="folder-import-help">{t('folderImport.intro')}</p>
            </div>
            <div className="export-field">
              <label className="export-field-label" htmlFor={sourceId}>{t('folderImport.source')}</label>
              <div className="folder-import-control-row">
                <input id={sourceId} className="dialog-input" readOnly value={folder} title={folder} placeholder={t('vaultImport.noFolder')} />
                <button type="button" className="dialog-button" disabled={busy || exporting} onClick={() => void browse()}>
                  <FolderOpen size="1em" aria-hidden="true" /> {t('folderImport.choose')}
                </button>
              </div>
            </div>
            {busy && !progress && <p role="status" className="folder-import-help">{t('folderImport.scanning')}</p>}
            {scan && <>
              <p className="folder-import-help">{t('folderImport.found', { count: scan.total, folders: scan.folders })}</p>
              {scan.unsupported > 0 && <p className="folder-import-help">{t('folderImport.unsupported', { count: scan.unsupported })}</p>}
              {scan.total === 0 ? <p className="folder-import-help">{t('folderImport.empty')}</p> : <fieldset className="folder-import-targets">
                <legend>{t('folderImport.targetsTitle')}</legend>
                <label className="export-field">
                  <span className="export-field-label">{t('folderImport.defaultTarget')}</span>
                  <select className="dialog-input" value={defaultTarget} disabled={locked} onChange={event => setDefaultTarget(event.target.value)}>
                    {options.map(option => <option key={option.value} value={option.value}>{option.label}</option>)}
                  </select>
                </label>
                <p className="folder-import-help">{t('folderImport.inheritHint')}</p>
                {scan.folders > 0 && <>
                  <input className="dialog-input" value={query} aria-label={t('folderImport.search')} placeholder={t('folderImport.search')}
                    disabled={busy} onChange={event => { setQuery(event.target.value); setOffset(0) }} />
                  <div className="folder-import-folders" aria-busy={loadingFolders}>
                    {page?.items.map(item => <label className="folder-import-row" key={item.path}>
                      <span className="folder-import-path">
                        <span title={item.path}>{item.path}</span>
                        <span className="folder-import-help">{t('folderImport.fileCount', { count: item.files, total: item.total })}</span>
                      </span>
                      <select className="dialog-input" aria-label={item.path} value={rules[item.path] ?? ''} disabled={locked || loadingFolders}
                        onChange={event => setRules(previous => {
                          const next = { ...previous }
                          if (event.target.value) next[item.path] = event.target.value
                          else delete next[item.path]
                          return next
                        })}>
                        <option value="">{t('folderImport.inherit', { target: labelFor(inheritedTarget(item.path, rules, defaultTarget)) })}</option>
                        {options.map(option => <option key={option.value} value={option.value}>{option.label}</option>)}
                      </select>
                    </label>)}
                    {!loadingFolders && page?.total === 0 && <p className="folder-import-help">{t('folderImport.noMatches')}</p>}
                  </div>
                  {page && page.total > 0 && <div className="folder-import-pagination">
                    <button type="button" className="dialog-button" disabled={busy || loadingFolders || offset === 0} onClick={() => setOffset(value => Math.max(0, value - PAGE_SIZE))}>{t('folderImport.previous')}</button>
                    <span className="folder-import-help">{t('folderImport.page', { from: offset + 1, to: Math.min(offset + PAGE_SIZE, page.total), total: page.total })}</span>
                    <button type="button" className="dialog-button" disabled={busy || loadingFolders || offset + PAGE_SIZE >= page.total} onClick={() => setOffset(value => value + PAGE_SIZE)}>{t('folderImport.next')}</button>
                  </div>}
                </>}
                <label className="export-field">
                  <span className="export-field-label">{t('vaultImport.extraTag')}</span>
                  <input className="dialog-input" value={tag} disabled={locked} placeholder={t('vaultImport.extraTagPlaceholder')} onChange={event => setTag(event.target.value)} />
                </label>
                <details className="folder-import-details">
                  <summary>{t('folderImport.detailsTitle')}</summary>
                  <p className="folder-import-help">{t('folderImport.sceneHint')}</p>
                  <p className="folder-import-help">{t('folderImport.fieldHint')}</p>
                </details>
              </fieldset>}
            </>}
            {progress && <div className="folder-import-progress" role="status" aria-live="polite">
              <progress max={Math.max(1, progress.total)} value={progress.processed} aria-label={t('folderImport.progress')} />
              <p className="folder-import-help">{t('folderImport.progressCount', { count: progress.processed, total: progress.total })}</p>
              <p className="folder-import-help">{t('folderImport.result', { imported: progress.imported, skipped: progress.skipped, failed: progress.failed })}</p>
              {progress.done && <p>{t('folderImport.done')}</p>}
              {stopped && !progress.done && <p>{t('folderImport.stopped')}</p>}
              {progress.issues.map(issue => <p className="folder-import-help" key={issue.path}>{t(`folderImport.${issue.kind}Error`, { path: issue.path })}</p>)}
            </div>}
            {!progress?.done && <div className="folder-import-run">
              {busy && progress ? <button type="button" className="dialog-button" onClick={() => { stop.current = true }}>{t('folderImport.stop')}</button> :
                <button type="button" className="dialog-button primary" disabled={busy || exporting || !scan?.total || !hasTarget} onClick={() => void run()}>
                  {t(progress ? 'folderImport.resume' : 'folderImport.run')}
                </button>}
            </div>}
          </section>
          <section className="folder-import-section" aria-labelledby={schemaTitleId}>
            <div className="folder-import-section-header">
              <h3 id={schemaTitleId}>{t('folderImport.schemaTitle')}</h3>
              <p className="folder-import-help">{t('folderImport.schemaHint')}</p>
            </div>
            <div className="folder-import-section-actions">
              <button type="button" className="dialog-button" disabled={busy || exporting} onClick={() => void exportSchema()}>
                <FileDown size="1em" aria-hidden="true" /> {t('folderImport.exportSchema')}
              </button>
            </div>
            {schemaSaved && <p className="folder-import-help" role="status">{t('folderImport.schemaSaved')}</p>}
          </section>
          {!window.novalist.isMobile && <ImportApiPanel folder={folder} disabled={busy || exporting} />}
          {error && <p className="folder-import-help" role="alert">{error}</p>}
        </div>
        <div className="dialog-actions folder-import-footer">
          <button type="button" className={`dialog-button${progress?.done ? ' primary' : ''}`} disabled={busy || exporting} onClick={onClose}>{t('dialog.close')}</button>
        </div>
      </div>
    </div>
  )
}
