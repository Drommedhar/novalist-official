import { useEffect } from 'react'
import { listenToEditor, editorWindow, pushEditorTheme } from './editorBridge'
import { useShellStore } from '../../stores/shellStore'
import { useSettingsStore } from '../../stores/settingsStore'
import { pushEditorSettings, pushEditorConfig } from './editorConfiguration'
import type { EditorMessageContext, EditorHandlerContext } from './editorMessageContext'
import { createEditorMessageHandlers } from './editorMessageHandlers'

export function useEditorMessages(context: EditorMessageContext): void {
  const { iframeRef, editorRef, i18n, t, peekRef } = context
  useEffect(() => {
    const iframe = iframeRef.current
    if (!iframe) return

    const handlers = createEditorMessageHandlers(createHandlerContext(context, iframe))
    const dispose = listenToEditor(iframe, (message) => handlers.get(message.type)?.(message))

    // Re-push theme when the OS/light-dark theme flips under the editor.
    const observer = new MutationObserver(() => {
      if (editorRef.current) pushEditorTheme(editorRef.current)
    })
    observer.observe(document.documentElement, {
      attributes: true,
      attributeFilter: ['data-theme']
    })

    // Live-apply settings changes (font, typewriter, page view, config) to the editor.
    const unsubscribeSettings = useSettingsStore.subscribe(() => {
      if (editorRef.current) {
        pushEditorSettings(editorRef.current)
        pushEditorConfig(editorRef.current, t)
      }
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
      peekRef.current.clearHide()
      editorRef.current = null
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [i18n.language])
}

function createHandlerContext(context: EditorMessageContext, iframe: HTMLIFrameElement): EditorHandlerContext {
  const { pane, iframeRef, peekRef } = context
  const activatePane = (): void => {
    // Only the focused child document may claim its pane.
    const shell = useShellStore.getState()
    if (iframe.contentDocument?.hasFocus() && shell.activePaneId !== pane) {
      shell.setActivePane(pane)
    }
  }
  const showHoverCard: EditorHandlerContext['showHoverCard'] = (hit, x, y, wordRect): void => {
    if (!iframeRef.current) return
    // Keep the anchor on the word while translating into parent coordinates.
    const frame = iframeRef.current.getBoundingClientRect()
    const anchor = wordRect
      ? {
          left: frame.left + wordRect.left,
          top: frame.top + wordRect.top,
          right: frame.left + wordRect.right,
          bottom: frame.top + wordRect.bottom
        }
      : { left: frame.left + x, top: frame.top + y, right: frame.left + x, bottom: frame.top + y }
    peekRef.current.showAt({ entityType: hit.type, entityId: hit.id }, anchor)
  }
  return { ...context, iframe, activatePane, showHoverCard }
}
