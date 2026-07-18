import { useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { listenToEditor, editorWindow, pushEditorTheme, type EditorWindow } from './editorBridge'
import { EditorToolbar, type FormattingState } from './EditorToolbar'
import { useProjectStore } from '../../stores/projectStore'

const DEFAULT_FORMATTING: FormattingState = {
  bold: false,
  italic: false,
  underline: false,
  alignment: 'left'
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

  // Push content whenever the open scene changes and the editor is live.
  useEffect(() => {
    const editor = editorRef.current
    if (!editor || sceneHtml === null) return
    loadingRef.current = true
    editor.setContent(sceneHtml)
    loadingRef.current = false
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
          const state = useProjectStore.getState()
          if (state.openSceneHtml !== null) {
            loadingRef.current = true
            live.setContent(state.openSceneHtml)
            loadingRef.current = false
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

    return () => {
      dispose()
      observer.disconnect()
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
