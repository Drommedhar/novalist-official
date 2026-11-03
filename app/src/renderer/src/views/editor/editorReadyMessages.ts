import { editorWindow, pushEditorTheme } from './editorBridge'
import { useEditorBridge } from '../../stores/editorBridgeStore'
import { editorPane, useProjectStore } from '../../stores/projectStore'
import { useSettingsStore } from '../../stores/settingsStore'
import { useWritingZoomStore } from '../../stores/writingZoomStore'
import { useShellStore } from '../../stores/shellStore'
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
      const shell = useShellStore.getState()
      if (useProjectStore.getState().activeEditorPaneId === pane && shell.mainView === 'write' && !shell.extView) {
        useEditorBridge
          .getState()
          .register(live, editorPane(useProjectStore.getState(), pane).sceneId)
      }
      pushEditorTheme(live)
      live.setLanguage(i18n.language.startsWith('de') ? 'de' : 'en')
      // Mobile: full-width text (no 18em comment gutter) + touch-sized toolbar.
      live.setMobile(window.novalist.isMobile === true)
      live.setProofingStatusHosted(window.novalist.isMobile !== true)
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
      const annotationsReady = loadAnnotations(live).then((loaded) => {
        if (!loaded) return
        const ids = live.footnoteOrder()
        if (ids.length > 0) applyFootnoteOrder(ids)
      })
      void pushEntityNames(live)
      const settings = useSettingsStore.getState()
      const applySettings = (): void => {
        if (editorRef.current !== live) return
        pushEditorSettings(live, true)
        pushEditorConfig(live, t)
      }
      let settingsReady: Promise<void>
      if (settings.view) {
        applySettings()
        settingsReady = Promise.resolve()
      } else {
        settingsReady = settings.load().then(applySettings)
      }
      // Fit the first scene once the chosen font, paper and comment gutter
      // have arrived. Later navigation/resizes keep the writer's chosen zoom.
      if (window.novalist.isMobile !== true && !useWritingZoomStore.getState().initialized) {
        void Promise.all([annotationsReady, settingsReady]).then(async () => {
          await live.document.fonts.ready
          await new Promise<void>(resolve => live.requestAnimationFrame(() => live.requestAnimationFrame(() => resolve())))
          if (!iframe.isConnected || editorRef.current !== live || !editorPane(useProjectStore.getState(), pane).sceneId) return
          if (!useWritingZoomStore.getState().initialized) {
            useWritingZoomStore.getState().fitOnce(live.getWritingZoomToFit() * 100)
          }
        })
      }
    }
  }
}
