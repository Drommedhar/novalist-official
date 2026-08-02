import { useEffect, useRef } from 'react'
import { rpc } from '../../rpc/client'
import { postThemeToFrame, watchTheme } from '../../shell/extensionTheme'
import type { ExtensionWebView as WebViewInfo } from '../../stores/extensionsStore'

/**
 * Hosts one extension-contributed webview in a sandboxed same-origin-scheme
 * iframe. Frame messages route to the extension's controller over RPC;
 * replies and controller pushes are posted back into the frame.
 *
 * The interface theme is posted in as well. A panel is a separate document, so
 * it inherits none of the shell's design tokens - which is why every extension
 * used to ship its own palette and stay that colour whichever theme the writer
 * picked. A panel that includes the theme shim announces itself and is sent the
 * whole `--nl-*` set, then again whenever the theme changes.
 */
export function ExtensionWebView({ view }: { view: WebViewInfo }): React.JSX.Element {
  const iframeRef = useRef<HTMLIFrameElement>(null)

  useEffect(() => {
    const iframe = iframeRef.current
    if (!iframe) return

    const postToFrame = (json: string): void => {
      iframe.contentWindow?.postMessage({ novalistHost: json }, '*')
    }

    const sendTheme = (): void => postThemeToFrame(iframe.contentWindow)

    const onMessage = (event: MessageEvent): void => {
      if (event.source !== iframe.contentWindow) return
      const data = event.data as { novalistExt?: string; novalistThemeReady?: boolean }
      // The shim has loaded and is listening. Posting before this point would
      // leave the panel unstyled until the writer next changed theme.
      if (data?.novalistThemeReady === true) {
        sendTheme()
        return
      }

      const raw = data?.novalistExt
      if (typeof raw !== 'string') return
      void rpc
        .request<string | null>('extensions/webviewMessage', [view.extensionId, view.key, raw])
        .then((reply) => {
          if (reply != null) postToFrame(reply)
        })
    }

    rpc.onNotification('extensions/webviewPosted', (params) => {
      const [extensionId, viewKey, json] = params as [string, string, string]
      if (extensionId === view.extensionId && viewKey === view.key) postToFrame(json)
    })

    const stopWatchingTheme = watchTheme(sendTheme)
    window.addEventListener('message', onMessage)
    return () => {
      window.removeEventListener('message', onMessage)
      stopWatchingTheme()
    }
  }, [view.extensionId, view.key])

  return (
    <iframe
      ref={iframeRef}
      className="editor-frame"
      src={`novalist-ext://${view.extensionId}/${view.entry.replace(`${view.extensionId}/`, '')}`}
      title={view.title}
      sandbox="allow-scripts"
    />
  )
}
