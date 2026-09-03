import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { FileInput, Import } from 'lucide-react'
import { rpc } from '../rpc/client'
import { useProjectStore, type ProjectStateDto } from '../stores/projectStore'

interface ImportScene {
  title: string
  wordCount: number
}

interface ImportChapter {
  title: string
  /** The act this chapter lands in. Empty when the source had no part above
   *  it, which is every format except a Scrivener project with parts. */
  partTitle: string
  scenes: ImportScene[]
}

/** One binder row the writer can redirect, and where it is currently headed. */
interface ImportMappingRow {
  key: string
  title: string
  /** 0 for a top-level binder entry, 1 for a direct child of one. */
  depth: number
  destination: Destination
  documents: number
  hasChildren: boolean
}

/** One book or draft this import would fill. */
interface ImportTarget {
  kind: 'manuscript' | 'draft' | 'book'
  title: string
  chapterCount: number
  sceneCount: number
  wordCount: number
}

interface ImportPlan {
  format: string
  chapterCount: number
  sceneCount: number
  wordCount: number
  chapters: ImportChapter[]
  /** What this import will not bring across. Empty for the single-file
   *  formats; populated for a Scrivener project. */
  losses: string[]
  partCount: number
  characterCount: number
  locationCount: number
  researchCount: number
  /** The binder rows the writer can redirect. Empty for the single-file
   *  formats, which have no binder to arrange. */
  mapping: ImportMappingRow[]
  targets: ImportTarget[]
}

interface ImportResult {
  chapters: number
  scenes: number
  words: number
  characters: number
  locations: number
  research: number
  drafts: number
  books: number
}

type Destination =
  | 'manuscript'
  | 'draft'
  | 'book'
  | 'characters'
  | 'places'
  | 'research'
  | 'skip'

/** Offered in the order a writer works down a binder: the book first, then the
 *  things beside it, then what is not coming. */
const DESTINATIONS: Destination[] = [
  'manuscript',
  'draft',
  'book',
  'characters',
  'places',
  'research',
  'skip'
]

function stagingFailure(error: unknown): { stage: string; reason: string } | null {
  const message = error instanceof Error ? error.message : String(error)
  const match = message.match(
    /manuscript-staging-failed:(project|source):(access-denied|disk-full|source-missing|unsafe-link|manifest-not-found|manifest-ambiguous|invalid-manifest|invalid-project|io|other)/
  )
  return match ? { stage: match[1], reason: match[2] } : null
}

/**
 * Brings an existing manuscript into the open project.
 *
 * Deliberately two steps: the preview reads and splits the file without writing
 * anything, so a writer sees exactly what would be created before their book
 * lands in the project. Dropping someone's whole manuscript in unannounced is
 * not a thing to do on one click.
 */
export function ImportManuscriptDialog(props: { onClose: () => void }): React.JSX.Element {
  const { t } = useTranslation()
  const [path, setPath] = useState('')
  const [plan, setPlan] = useState<ImportPlan | null>(null)
  const [result, setResult] = useState<ImportResult | null>(null)
  const [busy, setBusy] = useState(false)
  // The reader owns this list; the same values are shown here and passed to the
  // native picker so neither UI can promise a format the backend cannot open.
  const [formats, setFormats] = useState<string[]>([])
  const [formatsFailed, setFormatsFailed] = useState(false)
  const [openFailed, setOpenFailed] = useState(false)
  // The binder as the rules read it, kept from the first look at the project so
  // the rows and their detected destinations stay put while choices are made.
  const [rows, setRows] = useState<ImportMappingRow[]>([])
  // Only what the writer changed. A project the rules already read correctly
  // sends nothing, and imports exactly as it did before there was a mapping.
  const [overrides, setOverrides] = useState<Record<string, Destination>>({})

  useEffect(() => {
    let current = true
    void rpc
      .request<string[]>('manuscriptImport/formats')
      .then((supported) => {
        if (current) setFormats(supported)
      })
      .catch(() => {
        if (current) setFormatsFailed(true)
      })
    return () => {
      current = false
    }
  }, [])

  // MAS selections are copied into the app container; iOS keeps a native file
  // or folder scope open. Retain either through preview/import, then release it
  // on replacement or close.
  useEffect(() => {
    if (!path) return undefined
    return () => {
      void window.novalist.releasePickedFile(path).catch(() => {
        // Desktop startup or mobile process exit cleans up a missed release.
      })
    }
  }, [path])

  const asArgument = (chosen: Record<string, Destination>): { key: string; destination: string }[] =>
    Object.entries(chosen).map(([key, destination]) => ({ key, destination }))

  const preview = async (chosen: string): Promise<void> => {
    setBusy(true)
    setResult(null)
    setOverrides({})
    try {
      setPath(chosen)
      const next = await rpc.request<ImportPlan>('manuscriptImport/preview', [chosen])
      setPlan(next)
      setRows(next.mapping)
    } finally {
      setBusy(false)
    }
  }

  /** The rows nested under a top-level one: everything after it, up to the next
   *  top-level row. The list is flat and in binder order, so this is the run
   *  immediately following it. */
  const childrenOf = (key: string): ImportMappingRow[] => {
    const start = rows.findIndex((r) => r.key === key)
    if (start < 0 || rows[start].depth > 0) return []
    const rest = rows.slice(start + 1)
    const end = rest.findIndex((r) => r.depth === 0)
    return end < 0 ? rest : rest.slice(0, end)
  }

  /**
   * Sends a row where the writer asked, and everything inside it with it.
   *
   * Setting a folder used to leave its own contents on whatever had been
   * detected, so pointing nine drafts at nine drafts of their own meant nine
   * separate menus. A folder now sets what is inside it, and each of those rows
   * can still be changed afterwards - which is what makes one folder of nine
   * drafts one action rather than nine.
   *
   * Nothing is ever removed from the choices once made, even when it matches
   * what was detected. A row put back to its detected value is still a row the
   * writer decided about, and dropping it would leave it inheriting from the
   * folder above - which after a cascade is the very thing they just changed.
   */
  const reroute = async (key: string, destination: Destination): Promise<void> => {
    const next = { ...overrides, [key]: destination }
    for (const child of childrenOf(key)) next[child.key] = destination
    setOverrides(next)

    setBusy(true)
    try {
      setPlan(
        await rpc.request<ImportPlan>('manuscriptImport/preview', [path, asArgument(next)])
      )
    } finally {
      setBusy(false)
    }
  }

  const destinationOf = (row: ImportMappingRow): Destination =>
    overrides[row.key] ?? row.destination

  const pick = async (): Promise<void> => {
    if (formats.length === 0) return
    setBusy(true)
    setOpenFailed(false)
    try {
      const chosen = await window.novalist.pickFile(
        t('manuscriptImport.choose'),
        'manuscript',
        {
          extensions: formats,
          filterName: t('manuscriptImport.filterName'),
          scrivenerAccessTitle: t('manuscriptImport.scrivenerAccess')
        }
      )
      if (chosen) await preview(chosen)
    } catch (error) {
      const failure = stagingFailure(error)
      if (failure) {
        void rpc
          .request<void>('manuscriptImport/pickerFailure', [failure.stage, failure.reason])
          .catch(() => {
            // The backend may itself be unavailable; the in-dialog error remains.
          })
      }
      setOpenFailed(true)
      setPlan(null)
      setRows([])
    } finally {
      setBusy(false)
    }
  }

  /** A Scrivener project can be worth importing for its Codex sketches and
   *  research alone, so an empty draft is not an empty import. */
  const hasSomething = (p: ImportPlan): boolean =>
    p.chapterCount > 0 || p.characterCount > 0 || p.locationCount > 0 || p.researchCount > 0

  const run = async (): Promise<void> => {
    if (!plan || !hasSomething(plan)) return
    setBusy(true)
    try {
      setResult(
        await rpc.request<ImportResult>('manuscriptImport/run', [path, asArgument(overrides)])
      )
      useProjectStore
        .getState()
        .applyState(await rpc.request<ProjectStateDto>('project/getState'))
      setPlan(null)
      setRows([])
      setOverrides({})
      setPath('')
    } finally {
      setBusy(false)
    }
  }

  const fileName = path.split(/[\\/]/).pop() ?? ''
  const close = (): void => {
    if (!busy) props.onClose()
  }

  return (
    <div className="dialog-overlay" onClick={close}>
      <div
        className="dialog-card dialog-card-wide import-manuscript-dialog"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="dialog-header">
          <h3>{t('manuscriptImport.title')}</h3>
          <button
            className="dialog-close"
            disabled={busy}
            onClick={close}
            aria-label={t('dialog.close')}
          >
            ×
          </button>
        </div>

        <p className="settings-hint">{t('manuscriptImport.description')}</p>
        {formats.length > 0 && (
          <p className="settings-hint">
            {t('manuscriptImport.formats', { formats: formats.join(', ') })}
          </p>
        )}
        {formatsFailed && (
          <p className="settings-hint" role="alert">
            {t('manuscriptImport.formatsUnavailable')}
          </p>
        )}
        {openFailed && (
          <p className="settings-hint" role="alert">
            {t('manuscriptImport.openFailed')}
          </p>
        )}

        <div className="settings-button-row">
          <button
            className="dialog-button"
            disabled={busy || formats.length === 0}
            onClick={() => void pick()}
          >
            <FileInput size={14} /> {t('manuscriptImport.choose')}
          </button>
          {fileName && <span className="settings-hint">{fileName}</span>}
        </div>

        {/* The binder, and where each part of it is headed. Shown above the
            plan because it is what the plan is made of: a draft folder left
            empty for the next draft used to send an entire project to research
            with no way to say otherwise. */}
        {rows.length > 0 && (
          <>
            <h4 className="import-mapping-heading">{t('manuscriptImport.mappingTitle')}</h4>
            <p className="settings-hint">{t('manuscriptImport.mappingHint')}</p>
            <ul className="import-mapping">
              {rows.map((row) => (
                <li
                  key={row.key}
                  className={row.depth > 0 ? 'import-mapping-row nested' : 'import-mapping-row'}
                >
                  {/* Titled as well as shown: a binder name can outrun the row,
                      and these differ only in their tails. */}
                  <span
                    className="import-mapping-title"
                    title={row.title || t('manuscriptImport.untitledRow')}
                  >
                    {row.title || t('manuscriptImport.untitledRow')}
                  </span>
                  <span className="import-mapping-count">
                    {t('manuscriptImport.mappingDocuments', { count: row.documents })}
                  </span>
                  <select
                    className="toolbar-select"
                    aria-label={row.title || t('manuscriptImport.untitledRow')}
                    disabled={busy}
                    value={destinationOf(row)}
                    onChange={(e) => void reroute(row.key, e.target.value as Destination)}
                  >
                    {DESTINATIONS.map((d) => (
                      <option key={d} value={d}>
                        {t(`manuscriptImport.destination.${d}`)}
                      </option>
                    ))}
                  </select>
                </li>
              ))}
            </ul>
          </>
        )}

        {/* Named before the import runs, not discovered afterwards. */}
        {plan && plan.losses.length > 0 && (
          <p className="settings-hint">
            {t('manuscriptImport.losses', { items: plan.losses.join(', ') })}
          </p>
        )}

        {plan && !hasSomething(plan) && (
          <p className="settings-hint">{t('manuscriptImport.nothingFound')}</p>
        )}

        {plan && hasSomething(plan) && (
          <>
            <p className="settings-hint">
              {t('manuscriptImport.summary', {
                chapters: plan.chapterCount,
                scenes: plan.sceneCount,
                words: plan.wordCount.toLocaleString(),
                format: plan.format
              })}
            </p>

            {plan.partCount > 0 && (
              <p className="settings-hint">
                {t('manuscriptImport.partSummary', { count: plan.partCount })}
              </p>
            )}

            {/* Named before the import runs, so nothing about a writer's Codex
                arrives as a surprise. */}
            {(plan.characterCount > 0 ||
              plan.locationCount > 0 ||
              plan.researchCount > 0) && (
              <p className="settings-hint">
                {t('manuscriptImport.extrasSummary', {
                  characters: plan.characterCount,
                  locations: plan.locationCount,
                  research: plan.researchCount
                })}
              </p>
            )}

            {/* What each book and draft would end up holding, so nine drafts
                are nine lines rather than a single total to divide by nine.
                Shown for a single new draft too - "a draft called this" is the
                thing worth confirming before it is created. */}
            {(plan.targets.length > 1 ||
              plan.targets.some((target) => target.kind !== 'manuscript')) && (
              <ul className="import-preview">
                {plan.targets.map((target, i) => (
                  <li key={`${target.kind}-${target.title}-${i}`}>
                    <div className="import-preview-chapter">
                      {target.kind === 'manuscript'
                        ? t('manuscriptImport.targetManuscript')
                        : t(`manuscriptImport.target.${target.kind}`, { name: target.title })}
                    </div>
                    <div className="import-preview-scenes">
                      {t('manuscriptImport.targetSummary', {
                        chapters: target.chapterCount,
                        scenes: target.sceneCount,
                        words: target.wordCount.toLocaleString()
                      })}
                    </div>
                  </li>
                ))}
              </ul>
            )}

            <ul className="import-preview">
              {plan.chapters.map((c, i) => (
                <li key={`${c.title}-${i}`}>
                  <div className="import-preview-chapter">
                    {c.partTitle ? `${c.partTitle} - ${c.title}` : c.title}
                  </div>
                  <div className="import-preview-scenes">
                    {t('manuscriptImport.sceneSummary', {
                      count: c.scenes.length,
                      words: c.scenes.reduce((sum, s) => sum + s.wordCount, 0).toLocaleString()
                    })}
                  </div>
                </li>
              ))}
            </ul>

            <div className="settings-button-row">
              <button className="dialog-button" disabled={busy} onClick={() => void run()}>
                <Import size={14} /> {t('manuscriptImport.run')}
              </button>
              <span className="settings-hint">{t('manuscriptImport.appendHint')}</span>
            </div>
          </>
        )}

        {result && (
          <>
            <p className="settings-hint">
              {t('manuscriptImport.done', {
                chapters: result.chapters,
                scenes: result.scenes,
                words: result.words.toLocaleString()
              })}
            </p>
            {(result.characters > 0 || result.locations > 0 || result.research > 0) && (
              <p className="settings-hint">
                {t('manuscriptImport.doneExtras', {
                  characters: result.characters,
                  locations: result.locations,
                  research: result.research
                })}
              </p>
            )}
            {(result.drafts > 0 || result.books > 0) && (
              <p className="settings-hint">
                {t('manuscriptImport.doneTargets', {
                  drafts: result.drafts,
                  books: result.books
                })}
              </p>
            )}
          </>
        )}
      </div>
    </div>
  )
}
