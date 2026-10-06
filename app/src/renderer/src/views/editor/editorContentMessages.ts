import { type FormattingState } from './EditorToolbar'
import { useEditorBridge } from '../../stores/editorBridgeStore'
import { useProjectStore } from '../../stores/projectStore'
import { useShellStore } from '../../stores/shellStore'
import type { EditorHandlerContext, EditorMessageHandlers } from './editorMessageContext'

export function createEditorContentMessages(context: EditorHandlerContext): EditorMessageHandlers {
  const { iframe, loadingRef, lastReportedHtmlRef, pane, editorRef, activatePane, peekRef, setSpeaking, setFormatting } = context
  return {
    contentChanged: (message) => {
      const editor = editorRef.current
      // A debounced edit can arrive after the writer has opened the tools.
      // Hide them only while keyboard focus is still in this editor.
      if (useShellStore.getState().focusMode && document.activeElement === iframe)
        useShellStore.getState().setFocusToolsVisible(false)
      if (loadingRef.current || !editor) return
      // Record what the editor authored so the push effect treats the
      // store's echo of this same HTML as a no-op (keeps caret + undo).
      lastReportedHtmlRef.current = String(message.html ?? '')
      useProjectStore
        .getState()
        .onEditorContentChanged(
          pane,
          String(message.html ?? ''),
          String(message.plainText ?? '')
        )
    },
    focused: () => {
      activatePane()
    },
    pointerPressed: () => {
      activatePane()
      // A click in the editor dismisses the card unless it is pinned.
      peekRef.current.hide()
    },
    readAloudStateChanged: (message) => {
      setSpeaking(Boolean(message.speaking))
    },
    formattingChanged: (message) => {
      const nextFormatting: FormattingState = {
        bold: Boolean(message.bold),
        italic: Boolean(message.italic),
        underline: Boolean(message.underline),
        strikethrough: Boolean(message.strikethrough),
        highlight: Boolean(message.highlight),
        alignment: (message.alignment as FormattingState['alignment']) ?? 'left',
        paragraphStyle: String(message.paragraphStyle ?? ''),
        bulletList: Boolean(message.bulletList),
        numberList: Boolean(message.numberList),
        hasSelection: Boolean(message.hasSelection),
        linkActive: Boolean(message.linkActive),
        entityAtCaret: Boolean(message.entityAtCaret)
      }
      setFormatting(nextFormatting)
      if (useEditorBridge.getState().editor === editorRef.current) {
        useEditorBridge.getState().setContext({
          hasSelection: nextFormatting.hasSelection,
          entityAtCaret: nextFormatting.entityAtCaret
        })
      }
    }
  }
}
