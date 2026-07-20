import { useEffect, useRef } from 'react'
import { rpc } from '../../rpc/client'
import type { ExtensionWebView as WebViewInfo } from '../../stores/extensionsStore'

/**
 * Hosts one extension-contributed webview in a sandboxed same-origin-scheme
 * iframe. Frame messages route to the extension's controller over RPC;
 * replies and controller pushes are posted back into the frame.
 */
export function ExtensionWebView({ view }: { view: WebViewInfo }): React.JSX.Element {
  const iframeRef = useRef<HTMLIFrameElement>(null)

  useEffect(() => {
    const iframe = iframeRef.current
    if (!iframe) return

    const postToFrame = (json: string): void => {
      iframe.contentWindow?.postMessage({ novalistHost: json }, '*')
    }

    const onMessage = (event: MessageEvent): void => {
      if (event.source !== iframe.contentWindow) return
      const raw = (event.data as { novalistExt?: string })?.novalistExt
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

    window.addEventListener('message', onMessage)
    return () => window.removeEventListener('message', onMessage)
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
