import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { FileInput, FolderInput, Import } from 'lucide-react'
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
}

interface ImportResult {
  chapters: number
  scenes: number
  words: number
  characters: number
  locations: number
  research: number
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
  // The OS picker cannot be filtered here, so the reader tells us what it can
  // actually open and we say so up front instead of failing after the choice.
  const [formats, setFormats] = useState<string[]>([])

  useEffect(() => {
    void rpc.request<string[]>('manuscriptImport/formats').then(setFormats)
  }, [])

  const preview = async (chosen: string): Promise<void> => {
    setBusy(true)
    setResult(null)
    try {
      setPath(chosen)
      setPlan(await rpc.request<ImportPlan>('manuscriptImport/preview', [chosen]))
    } finally {
      setBusy(false)
    }
  }

  /** A Scrivener project is a folder, not a file, so it needs the folder
   *  picker - the file picker cannot select one. */
  const pickProject = async (): Promise<void> => {
    const chosen = await window.novalist.pickFolder(t('manuscriptImport.chooseScrivener'))
    if (chosen) await preview(chosen)
  }

  const pick = async (): Promise<void> => {
    const chosen = await window.novalist.pickFile(t('manuscriptImport.choose'), 'all')
    if (!chosen) return

    setBusy(true)
    setResult(null)
    try {
      setPath(chosen)
      setPlan(await rpc.request<ImportPlan>('manuscriptImport/preview', [chosen]))
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
      setResult(await rpc.request<ImportResult>('manuscriptImport/run', [path]))
      useProjectStore
        .getState()
        .applyState(await rpc.request<ProjectStateDto>('project/getState'))
      setPlan(null)
    } finally {
      setBusy(false)
    }
  }

  const fileName = path.split(/[\\/]/).pop() ?? ''

  return (
    <div className="dialog-overlay" onClick={props.onClose}>
      <div className="dialog-card import-manuscript-dialog" onClick={(e) => e.stopPropagation()}>
        <div className="dialog-header">
          <h3>{t('manuscriptImport.title')}</h3>
          <button className="dialog-close" onClick={props.onClose} aria-label={t('dialog.close')}>
            ×
          </button>
        </div>

        <p className="settings-hint">{t('manuscriptImport.description')}</p>
        {formats.length > 0 && (
          <p className="settings-hint">
            {t('manuscriptImport.formats', { formats: formats.join(', ') })}
          </p>
        )}

        <div className="settings-button-row">
          <button className="dialog-button" disabled={busy} onClick={() => void pick()}>
            <FileInput size={14} /> {t('manuscriptImport.choose')}
          </button>
          <button className="dialog-button" disabled={busy} onClick={() => void pickProject()}>
            <FolderInput size={14} /> {t('manuscriptImport.chooseScrivener')}
          </button>
          {fileName && <span className="settings-hint">{fileName}</span>}
        </div>

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
          </>
        )}
      </div>
    </div>
  )
}
