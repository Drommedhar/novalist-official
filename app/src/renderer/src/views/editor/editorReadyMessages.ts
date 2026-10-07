import { editorWindow, pushEditorTheme } from './editorBridge'
import { useEditorBridge } from '../../stores/editorBridgeStore'
import { editorPane, useProjectStore } from '../../stores/projectStore'
import { useSettingsStore } from '../../stores/settingsStore'
import { rpc } from '../../rpc/client'
import { pushEditorSettings, pushEditorConfig } from './editorConfiguration'
import type { EditorHandlerContext, EditorMessageHandlers } from './editorMessageContext'

export function createEditorReadyMessages(context: EditorHandlerContext): EditorMessageHandlers {
  const { editorRef, iframe, pane, i18n, loadingRef, lastReportedHtmlRef, loadAnnotations, applyFootnoteOrder, pushEntityNames, t } = context
  return {
    ready: () => {
      // Where a stored image path hangs off, so the frame can show one.
      void rpc
        .request<string>('gallery/base')
        .then((base) => editorRef.current?.setImageBase(`novalist-project://nl/${base}`))
        .catch(() => undefined)
      const live = editorWindow(iframe)
      if (!live) return
      editorRef.current = live
      if (useProjectStore.getState().activeEditorPaneId === pane) {
        useEditorBridge
          .getState()
          .register(live, editorPane(useProjectStore.getState(), pane).sceneId)
      }
      pushEditorTheme(live)
      live.setLanguage(i18n.language.startsWith('de') ? 'de' : 'en')
      // Mobile: full-width text (no 18em comment gutter) + touch-sized toolbar.
      live.setMobile(window.novalist.isMobile === true)
      // Content loads synchronously so typing can never race a deferred
      // setContent; the settings push is made non-destructive instead.
      const initialHtml = editorPane(useProjectStore.getState(), pane).html
      if (initialHtml !== null) {
        loadingRef.current = true
        live.setSceneContext(editorPane(useProjectStore.getState(), pane).sceneId ?? '')
        live.setContent(initialHtml)
        loadingRef.current = false
        lastReportedHtmlRef.current = initialHtml
      }
      void loadAnnotations(live).then((loaded) => {
        if (!loaded) return
        const ids = live.footnoteOrder()
        if (ids.length > 0) applyFootnoteOrder(ids)
      })
      void pushEntityNames(live)
      const settings = useSettingsStore.getState()
      if (settings.view) {
        pushEditorSettings(live, true)
        pushEditorConfig(live, t)
      } else {
        void settings.load().then(() => {
          if (editorRef.current) {
            pushEditorSettings(editorRef.current, true)
            pushEditorConfig(editorRef.current, t)
          }
        })
      }
    }
  }
}
