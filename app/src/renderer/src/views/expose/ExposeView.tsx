import { useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { FileDown } from 'lucide-react'
import {
  listenToEditor,
  editorWindow,
  pushEditorTheme,
  type EditorWindow
} from '../editor/editorBridge'
import { rpc } from '../../rpc/client'
import { useProjectStore } from '../../stores/projectStore'
import { useSettingsStore } from '../../stores/settingsStore'
import '../editor/editor.css'
import './expose.css'

/** Counts refreshed while typing; save runs on the slower autosave beat. */
const MEASURE_DELAY_MS = 300
const SAVE_DELAY_MS = 2000

interface ExposeState {
  html: string
  charLimit: number
  pageLimit: number
  characters: number
  lines: number
  pages: number
}

const EMPTY: ExposeState = {
  html: '',
  charLimit: 0,
  pageLimit: 0,
  characters: 0,
  lines: 0,
  pages: 0
}

/**
 * The paragraph styles the export understands. The empty id is plain body
 * text; the other two map onto the document's two heading levels.
 */
const PARAGRAPH_STYLES: { id: string; labelKey: string }[] = [
  { id: 'heading', labelKey: 'expose.styleTitle' },
  { id: 'subheading', labelKey: 'expose.styleSection' },
  { id: '', labelKey: 'expose.styleBody' }
]

/** ok / close to the budget / past it. Drives the counter colour only. */
function budgetClass(value: number, limit: number): string {
  if (limit <= 0) return ''
  if (value > limit) return ' is-over'
  if (value >= limit * 0.9) return ' is-near'
  return ''
}

/**
 * The book's exposé: a full editing surface with the Normseiten counts the
 * writer is working against. Limits warn, they never block typing.
 */
export function ExposeView(): React.JSX.Element {
  const { t, i18n } = useTranslation()
  const iframeRef = useRef<HTMLIFrameElement>(null)
  const editorRef = useRef<EditorWindow | null>(null)
  const projectName = useProjectStore((s) => s.projectName)
  const books = useProjectStore((s) => s.books)
  const activeBookId = useProjectStore((s) => s.activeBookId)
  // The exposé belongs to a book, so it is the book's title that heads it.
  const bookTitle = books.find((b) => b.id === activeBookId)?.name ?? projectName ?? ''
  const [state, setState] = useState<ExposeState>(EMPTY)
  const [charLimitText, setCharLimitText] = useState('')
  const [pageLimitText, setPageLimitText] = useState('')
  const [busy, setBusy] = useState(false)
  const [result, setResult] = useState<string | null>(null)
  // Style of the paragraph the caret sits in, reported by the editor.
  const [paragraphStyle, setParagraphStyle] = useState('')
  // The HTML last pushed into (or reported by) the iframe. Guards against
  // re-pushing our own echo, which would reset the caret and kill undo.
  const currentHtmlRef = useRef('')
  const measureTimer = useRef<ReturnType<typeof setTimeout> | null>(null)
  const saveTimer = useRef<ReturnType<typeof setTimeout> | null>(null)

  const applyLimits = (next: ExposeState): void => {
    setCharLimitText(next.charLimit > 0 ? String(next.charLimit) : '')
    setPageLimitText(next.pageLimit > 0 ? String(next.pageLimit) : '')
  }

  // Load once; the iframe picks the content up on its ready handshake, or here
  // when it booted first.
  useEffect(() => {
    void rpc.request<ExposeState>('expose/get').then((loaded) => {
      setState(loaded)
      applyLimits(loaded)
      currentHtmlRef.current = loaded.html
      editorRef.current?.setContent(loaded.html)
    })
  }, [])

  // Flush a pending save when the view is left mid-edit.
  useEffect(() => {
    return () => {
      if (measureTimer.current) clearTimeout(measureTimer.current)
      if (saveTimer.current) {
        clearTimeout(saveTimer.current)
        void rpc.request('expose/save', [currentHtmlRef.current])
      }
    }
  }, [])

  useEffect(() => {
    const iframe = iframeRef.current
    if (!iframe) return

    const pushConfig = (editor: EditorWindow): void => {
      pushEditorTheme(editor)
      editor.setLanguage(i18n.language.startsWith('de') ? 'de' : 'en')
      editor.setMobile(window.novalist.isMobile === true)
      const view = useSettingsStore.getState().view
      if (view) editor.setFont(view.effective.editorFontFamily, view.effective.editorFontSize)
      editor.setContextMenuLabels(
        JSON.stringify({
          cut: t('editor.contextMenu.cut'),
          copy: t('editor.contextMenu.copy'),
          paste: t('editor.contextMenu.paste'),
          selectAll: t('editor.contextMenu.selectAll')
        })
      )
    }

    const dispose = listenToEditor(iframe, (message) => {
      switch (message.type) {
        case 'ready': {
          const live = editorWindow(iframe)
          if (!live) return
          editorRef.current = live
          pushConfig(live)
          live.setContent(currentHtmlRef.current)
          break
        }
        case 'contentChanged': {
          const html = String(message.html ?? '')
          currentHtmlRef.current = html
          if (measureTimer.current) clearTimeout(measureTimer.current)
          measureTimer.current = setTimeout(() => {
            measureTimer.current = null
            void rpc.request<ExposeState>('expose/measure', [html]).then((measured) => {
              setState((prev) => ({ ...measured, charLimit: prev.charLimit, pageLimit: prev.pageLimit }))
            })
          }, MEASURE_DELAY_MS)
          if (saveTimer.current) clearTimeout(saveTimer.current)
          saveTimer.current = setTimeout(() => {
            saveTimer.current = null
            void rpc.request('expose/save', [html])
          }, SAVE_DELAY_MS)
          break
        }
        case 'formattingChanged': {
          setParagraphStyle(String(message.paragraphStyle ?? ''))
          break
        }
        default:
          break
      }
    })

    const observer = new MutationObserver(() => {
      if (editorRef.current) pushEditorTheme(editorRef.current)
    })
    observer.observe(document.documentElement, { attributes: true, attributeFilter: ['data-theme'] })

    const existing = editorWindow(iframe)
    if (existing && typeof existing.setContent === 'function') {
      editorRef.current = existing
      pushConfig(existing)
    }

    return () => {
      dispose()
      observer.disconnect()
      editorRef.current = null
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [i18n.language])

  const commitLimits = (): void => {
    const chars = Math.max(0, Number.parseInt(charLimitText, 10) || 0)
    const pages = Math.max(0, Number.parseInt(pageLimitText, 10) || 0)
    if (chars === state.charLimit && pages === state.pageLimit) return
    void rpc.request<ExposeState>('expose/setLimits', [chars, pages]).then((next) => {
      setState((prev) => ({ ...prev, charLimit: next.charLimit, pageLimit: next.pageLimit }))
      applyLimits(next)
    })
  }

  const runExport = async (): Promise<void> => {
    const name = bookTitle ? `${bookTitle} - Expose` : 'Expose'
    const output = await window.novalist.saveFile(`${name}.docx`)
    if (!output) return
    setBusy(true)
    setResult(null)
    try {
      // Export reads the saved file, so land any pending edit first.
      if (saveTimer.current) {
        clearTimeout(saveTimer.current)
        saveTimer.current = null
      }
      await rpc.request('expose/save', [currentHtmlRef.current])
      const exported = await rpc.request<{ success: boolean }>('expose/export', [output, bookTitle])
      setResult(exported.success ? t('export.exportSuccess') : t('expose.exportEmpty'))
    } catch {
      setResult(t('export.exportFailed'))
    } finally {
      setBusy(false)
    }
  }

  const charText =
    state.charLimit > 0
      ? t('expose.charsOfLimit', { value: state.characters, limit: state.charLimit })
      : t('expose.chars', { value: state.characters })
  const pageText =
    state.pageLimit > 0
      ? t('expose.pagesOfLimit', { value: state.pages, limit: state.pageLimit })
      : t('expose.pages', { value: state.pages })

  return (
    <div className="expose-view">
      <div className="expose-bar">
        <h1 className="expose-heading">{t('shell.view.expose')}</h1>
        <div className="expose-styles" role="group" aria-label={t('expose.styleGroup')}>
          {PARAGRAPH_STYLES.map((style) => (
            <button
              key={style.id || 'body'}
              type="button"
              className={`expose-style-btn${paragraphStyle === style.id ? ' active' : ''}`}
              data-style={style.id || 'body'}
              aria-pressed={paragraphStyle === style.id}
              onClick={() => editorRef.current?.setParagraphStyle(style.id)}
            >
              {t(style.labelKey)}
            </button>
          ))}
        </div>
        <div className="expose-counters">
          <span className={`expose-counter${budgetClass(state.characters, state.charLimit)}`}>
            {charText}
          </span>
          <span className={`expose-counter${budgetClass(state.pages, state.pageLimit)}`}>
            {pageText}
          </span>
        </div>
        <div className="expose-limits">
          <label className="expose-limit" htmlFor="expose-char-limit">
            {t('expose.charLimit')}
            <input
              id="expose-char-limit"
              className="dialog-input expose-limit-input"
              type="number"
              min={0}
              value={charLimitText}
              placeholder={t('expose.noLimit')}
              onChange={(e) => setCharLimitText(e.target.value)}
              onBlur={commitLimits}
            />
          </label>
          <label className="expose-limit" htmlFor="expose-page-limit">
            {t('expose.pageLimit')}
            <input
              id="expose-page-limit"
              className="dialog-input expose-limit-input"
              type="number"
              min={0}
              value={pageLimitText}
              placeholder={t('expose.noLimit')}
              onChange={(e) => setPageLimitText(e.target.value)}
              onBlur={commitLimits}
            />
          </label>
        </div>
        <button className="start-open expose-export" disabled={busy} onClick={() => void runExport()}>
          <FileDown size={15} strokeWidth={2} />
          {busy ? t('export.exporting') : t('expose.exportAction')}
        </button>
      </div>
      {result && <p className="inspector-meta expose-result">{result}</p>}
      <div className="editor-pane expose-editor">
        <iframe
          ref={iframeRef}
          className="editor-frame"
          src="./editor/editor.html"
          title="expose-editor"
          sandbox="allow-scripts allow-same-origin"
        />
      </div>
    </div>
  )
}
