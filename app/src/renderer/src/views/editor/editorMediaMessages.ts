import { rpc } from '../../rpc/client'
import type { EditorHandlerContext, EditorMessageHandlers } from './editorMessageContext'

export function createEditorMediaMessages(context: EditorHandlerContext): EditorMessageHandlers {
  const { t, setPendingImage, sceneTitleRef, setLinkPrompt, editorRef, paneIds } = context
  return {
    insertImageRequested: () => {
      const owner = paneIds().sceneId
      void (async () => {
        const path = await window.novalist.pickFile(t('editorImage.pick'), 'images')
        if (!path || paneIds().sceneId !== owner) return
        const image = await rpc.request<{ path: string; url: string }>('gallery/import', [path])
        if (paneIds().sceneId === owner) setPendingImage(image.path)
      })()
    },
    keepDarling: (message) => {
      // Nothing is logged on this path at any level: the payload is the
      // writer's prose.
      void rpc.request('darlings/keep', [
        String(message.text ?? ''),
        sceneTitleRef.current ?? ''
      ])
    },
    requestLink: () => {
      // The frame owns no dialogs, so it asks and the host answers. An
      // empty address unlinks, which is how a link is removed.
      setLinkPrompt(true)
    },
    speakSentence: (message) => {
      // One sentence at a time so the editor keeps highlighting the one
      // being read; it waits for this reply before moving on.
      const frame = editorRef.current
      void rpc
        .request<boolean>('voices/speak', [
          String(message.text ?? ''),
          (message.voiceId as string) || null,
          Number(message.rate) || 1
        ])
        .then((ok) => frame?.onSentenceSpoken(ok))
        .catch(() => frame?.onSentenceSpoken(false))
    },
    auditionLine: (message) => {
      // One line, cast and directed, played where it was written. The
      // clip goes to the same cache the reading uses and is fetched over
      // the same protocol, so nothing about the audio path is new.
      const { chapterGuid, sceneId } = paneIds()
      if (!chapterGuid || !sceneId) return
      void rpc
        .request<{ clip: string | null }>('narration/auditionLine', [
          chapterGuid,
          sceneId,
          String(message.text ?? '')
        ])
        .then((result) => {
          if (result.clip === null) return
          const audio = new Audio(`novalist-audio://clip/${result.clip}`)
          void audio.play().catch(() => {})
        })
        .catch(() => {})
    },
    stopSystemSpeech: () => {
      void rpc.request('voices/stop').catch(() => {})
    }
  }
}
