import { useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { listenToEditor, editorWindow, pushEditorTheme, type EditorWindow } from './editorBridge'
import { EditorToolbar, type FormattingState } from './EditorToolbar'
import { useProjectStore } from '../../stores/projectStore'
import { rpc } from '../../rpc/client'
import { useSettingsStore } from '../../stores/settingsStore'

function pushEditorSettings(editor: EditorWindow, initial = false): void {
  const view = useSettingsStore.getState().view
  if (!view) return
  const eff = view.effective
  editor.setFont(eff.editorFontFamily, eff.editorFontSize)
  // On the initial push, disabled toggles match editor.html's startup state;
  // skipping them avoids DOM rebuilds that would drop the caret mid-typing.
  if (!initial || eff.typewriterScrollEnabled) {
    editor.setTypewriterScroll(eff.typewriterScrollEnabled, eff.typewriterScrollAnchor)
  }
  if (!initial || eff.pageViewEnabled) {
    editor.setPageView(eff.pageViewEnabled)
  }
  if (!initial || eff.enableBookParagraphSpacing) {
    editor.setBookParagraphSpacing(eff.enableBookParagraphSpacing)
  }
  if (!initial || eff.grammarCheckEnabled) {
    editor.setGrammarCheckEnabled(eff.grammarCheckEnabled)
  }
}

const DEFAULT_FORMATTING: FormattingState = {
  bold: false,
  italic: false,
  underline: false,
  alignment: 'left'
}

interface SceneComment {
  id: string
  anchorText: string
  text: string
  resolved: boolean
}

interface SceneFootnote {
  id: string
  number: number
  text: string
}

/**
 * Hosts editor.html (carried over from the Avalonia app unchanged apart from
 * the parent-frame transport branch) and wires the ready handshake, theme,
 * content push, and autosave round-trip.
 */
export function EditorFrame(): React.JSX.Element {
  const { i18n } = useTranslation()
  const iframeRef = useRef<HTMLIFrameElement>(null)
  const editorRef = useRef<EditorWindow | null>(null)
  const openSceneId = useProjectStore((s) => s.openSceneId)
  const sceneHtml = useProjectStore((s) => s.openSceneHtml)
  const loadingRef = useRef(false)
  const [formatting, setFormatting] = useState<FormattingState>(DEFAULT_FORMATTING)
  const annotationsRef = useRef<{ comments: SceneComment[]; footnotes: SceneFootnote[] }>({
    comments: [],
    footnotes: []
  })

  const pushAnnotations = (editor: EditorWindow): void => {
    editor.setCommentsData(
      annotationsRef.current.comments.map((c) => ({
        id: c.id,
        anchorText: c.anchorText,
        text: c.text
      }))
    )
    editor.setFootnotesData(
      annotationsRef.current.footnotes.map((f) => ({ id: f.id, text: f.text }))
    )
  }

  const loadAnnotations = async (editor: EditorWindow | null): Promise<void> => {
    const state = useProjectStore.getState()
    if (!state.openChapterGuid || !state.openSceneId) return
    const annotations = await rpc.request<{ comments: SceneComment[]; footnotes: SceneFootnote[] }>(
      'scenes/getAnnotations',
      [state.openChapterGuid, state.openSceneId]
    )
    annotationsRef.current = annotations
    if (editor) pushAnnotations(editor)
  }

  const persistAnnotations = (): void => {
    const state = useProjectStore.getState()
    if (!state.openChapterGuid || !state.openSceneId) return
    void rpc.request('scenes/setAnnotations', [
      state.openChapterGuid,
      state.openSceneId,
      annotationsRef.current.comments,
      annotationsRef.current.footnotes
    ])
  }

  // Push content whenever the open scene changes and the editor is live.
  useEffect(() => {
    const editor = editorRef.current
    if (!editor || sceneHtml === null) return
    loadingRef.current = true
    editor.setContent(sceneHtml)
    loadingRef.current = false
    void loadAnnotations(editor)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [openSceneId, sceneHtml])

  useEffect(() => {
    const iframe = iframeRef.current
    if (!iframe) return

    const dispose = listenToEditor(iframe, (message) => {
      const editor = editorRef.current
      switch (message.type) {
        case 'ready': {
          const live = editorWindow(iframe)
          if (!live) return
          editorRef.current = live
          pushEditorTheme(live)
          live.setLanguage(i18n.language.startsWith('de') ? 'de' : 'en')
          // Content loads synchronously so typing can never race a deferred
          // setContent; the settings push is made non-destructive instead.
          const state = useProjectStore.getState()
          if (state.openSceneHtml !== null) {
            loadingRef.current = true
            live.setContent(state.openSceneHtml)
            loadingRef.current = false
          }
          void loadAnnotations(live)
          const settings = useSettingsStore.getState()
          if (settings.view) {
            pushEditorSettings(live, true)
          } else {
            void settings.load().then(() => {
              if (editorRef.current) pushEditorSettings(editorRef.current, true)
            })
          }
          break
        }
        case 'contentChanged': {
          if (loadingRef.current || !editor) return
          useProjectStore
            .getState()
            .onEditorContentChanged(String(message.html ?? ''), String(message.plainText ?? ''))
          break
        }
        case 'grammarCheckRequest': {
          if (!editor) return
          void rpc
            .request<unknown[]>('grammar/check', [String(message.plainText ?? '')])
            .then((issues) => {
              editorRef.current?.setGrammarIssues(JSON.stringify(issues))
            })
            .catch(() => {
              // Offline or endpoint unavailable: clear underlines quietly.
              editorRef.current?.setGrammarIssues('[]')
            })
          break
        }
        case 'requestAddComment': {
          editorRef.current?.addCommentToSelection(crypto.randomUUID())
          break
        }
        case 'commentAdded': {
          annotationsRef.current.comments.push({
            id: String(message.commentId),
            anchorText: String(message.anchorText ?? ''),
            text: '',
            resolved: false
          })
          persistAnnotations()
          if (editorRef.current) pushAnnotations(editorRef.current)
          break
        }
        case 'commentTextChanged': {
          const comment = annotationsRef.current.comments.find(
            (c) => c.id === String(message.commentId)
          )
          if (comment) {
            comment.text = String(message.text ?? '')
            persistAnnotations()
          }
          break
        }
        case 'commentDeleted': {
          annotationsRef.current.comments = annotationsRef.current.comments.filter(
            (c) => c.id !== String(message.commentId)
          )
          persistAnnotations()
          break
        }
        case 'requestAddFootnote': {
          editorRef.current?.insertFootnoteAtSelection(crypto.randomUUID())
          break
        }
        case 'footnoteInserted': {
          annotationsRef.current.footnotes.push({
            id: String(message.footnoteId),
            number: Number(message.number ?? annotationsRef.current.footnotes.length + 1),
            text: ''
          })
          persistAnnotations()
          break
        }
        case 'addToDictionary': {
          void rpc.request<boolean>('grammar/addToDictionary', [String(message.word ?? '')])
          break
        }
        case 'formattingChanged': {
          setFormatting({
            bold: Boolean(message.bold),
            italic: Boolean(message.italic),
            underline: Boolean(message.underline),
            alignment: (message.alignment as FormattingState['alignment']) ?? 'left'
          })
          break
        }
        default:
          break
      }
    })

    // Re-push theme when the OS/light-dark theme flips under the editor.
    const observer = new MutationObserver(() => {
      if (editorRef.current) pushEditorTheme(editorRef.current)
    })
    observer.observe(document.documentElement, { attributes: true, attributeFilter: ['data-theme'] })

    // Live-apply settings changes (font, typewriter, page view) to the editor.
    const unsubscribeSettings = useSettingsStore.subscribe(() => {
      if (editorRef.current) pushEditorSettings(editorRef.current)
    })

    // If the effect re-runs after the iframe already booted (e.g. a language
    // change re-created this closure), re-acquire the live editor window —
    // 'ready' only fires once per page load.
    const existing = editorWindow(iframe)
    if (existing && typeof existing.setContent === 'function') {
      editorRef.current = existing
      existing.setLanguage(i18n.language.startsWith('de') ? 'de' : 'en')
    }

    return () => {
      dispose()
      observer.disconnect()
      unsubscribeSettings()
      editorRef.current = null
    }
  }, [i18n.language])

  return (
    <div className="editor-pane">
      <EditorToolbar formatting={formatting} editor={() => editorRef.current} />
      <iframe
        ref={iframeRef}
        className="editor-frame"
        src="./editor/editor.html"
        title="editor"
        sandbox="allow-scripts allow-same-origin"
      />
    </div>
  )
}
