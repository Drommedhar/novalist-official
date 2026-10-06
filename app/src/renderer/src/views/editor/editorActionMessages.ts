import { runInlineAction } from './editorBridge'
import { useProjectStore, type ProjectStateDto } from '../../stores/projectStore'
import { useSettingsStore } from '../../stores/settingsStore'
import { dispatchForwardedHotkey } from '../../shell/hotkeys'
import { rpc } from '../../rpc/client'
import type { EditorHandlerContext, EditorMessageHandlers } from './editorMessageContext'

export function createEditorActionMessages(context: EditorHandlerContext): EditorMessageHandlers {
  const { paneIds, pane, editorRef } = context
  return {
    splitSceneRequested: (message) => {
      // Everything after the caret becomes a new scene right below this
      // one, carrying the date, stage, plotlines and POV that still
      // describe it.
      const { chapterGuid, sceneId } = paneIds()
      if (!chapterGuid || !sceneId) return
      void rpc
        .request<{ sceneId: string | null; state: ProjectStateDto }>('sceneSplit/split', [
          chapterGuid,
          sceneId,
          String(message.before ?? ''),
          String(message.after ?? ''),
          null
        ])
        .then((result) => {
          useProjectStore.getState().applyState(result.state)
          // The scene shrank under the editor, so it has to be re-read
          // rather than left showing both halves.
          void useProjectStore.getState().openSceneIn(pane, chapterGuid, sceneId)
        })
    },
    inlineActionRequested: (message) => {
      const actionId = String(message.actionId ?? '')
      const selected = String(message.selectedText ?? '')
      // Carried so an action invoked at a bare caret has something to work
      // from: the prose before it, and whatever the writer typed after the
      // slash as a directive.
      void runInlineAction(actionId, selected, {
        precedingText: String(message.precedingText ?? ''),
        directive: String(message.directive ?? '')
      }).then((result) => {
        editorRef.current?.applyInlineActionResult(JSON.stringify({ actionId, ...result }))
      })
    },
    extensionContextMenuRequested: (message) => {
      const itemId = String(message.itemId ?? '')
      const proj = useProjectStore.getState()
      void rpc.request('extensions/contextMenuItem/execute', [
        itemId,
        proj.openChapterGuid ?? '',
        proj.openSceneId ?? ''
      ])
    },
    hotkey: (message) => {
      const key = String(message.key ?? '')
      const ran = dispatchForwardedHotkey({
        key,
        code: String(message.code ?? ''),
        ctrlKey: Boolean(message.ctrlKey),
        metaKey: Boolean(message.metaKey),
        shiftKey: Boolean(message.shiftKey),
        altKey: Boolean(message.altKey)
      })
      // Escape is not a registered hotkey - it is what dismisses whatever
      // overlay is up (mobile sheets, dialogs). Those listen on the window,
      // which an iframe keydown never reaches, so replay it there.
      if (!ran && key === 'Escape') {
        window.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }))
      }
    },
    zoom: (message) => {
      const settings = useSettingsStore.getState()
      const view = settings.view
      if (!view) return
      const current = view.effective.editorFontSize
      const next = Math.min(36, Math.max(8, current + Number(message.delta ?? 0)))
      if (next === current) return
      const scope =
        view.overrides && view.overrides.editorFontSize != null ? 'project' : 'global'
      void settings.update(scope, { editorFontSize: next })
    }
  }
}
