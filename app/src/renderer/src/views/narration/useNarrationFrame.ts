import { useEffect, useRef } from 'react'
import { useTranslation } from 'react-i18next'
import { useNarrationStore } from '../../stores/narrationStore'
import { useSettingsStore } from '../../stores/settingsStore'
import { type NarrationState } from './useNarrationView'

/** The frame's side of the bridge. Same shape as the manuscript frame's. */
export interface NarrationWindow extends Window {
  setBook(json: string): void
  setSpeaking(sceneId: string | null, key: string | null): void
  setProgress(ready: string, rendering: string): void
  setSelected(sceneId: string | null, key: string | null, reveal: boolean): void
  revealScene(sceneId: string): void
  setTheme(
    bg: string,
    fg: string,
    accent: string,
    subtle: string,
    divider: string,
    scrollbarThumb?: string,
    scrollbarThumbHover?: string,
    scrollbarThumbActive?: string
  ): void
  setFont(family: string, size: number): void
  setReadingComfort(lineHeight: number, letterSpacing: number): void
  setLanguage(lang: string): void
}

interface NarrationFrameProps extends Pick<NarrationState, 'book' | 'sceneNavigation' | 'speaking' | 'ready' | 'rendering' | 'selected'> {
  colours: Record<string, string>
  voiceInfersDelivery(voiceId: string | null): boolean
  t: ReturnType<typeof useTranslation>['t']
}

interface NarrationFrameContext extends NarrationFrameProps {
  frameRef: React.RefObject<HTMLIFrameElement | null>
  readyRef: React.RefObject<boolean>
}

function pushNarrationBook({frameRef, readyRef, book, colours, voiceInfersDelivery, t}: NarrationFrameContext): void {
    const frame = frameRef.current?.contentWindow as NarrationWindow | null
    if (!frame || !readyRef.current) return

    const style = getComputedStyle(document.documentElement)
    const token = (name: string): string => style.getPropertyValue(name).trim()
    frame.setTheme(
      token('--nl-surface-editor'),
      token('--nl-text'),
      token('--nl-accent'),
      token('--nl-text-subtle'),
      token('--nl-border'),
      // Separate document: browser-painted scrollbars need the colours pushed.
      token('--nl-scrollbar-thumb'),
      token('--nl-scrollbar-thumb-hover'),
      token('--nl-scrollbar-thumb-active')
    )
    const effective = useSettingsStore.getState().view?.effective
    if (effective) {
      frame.setFont(effective.editorFontFamily, effective.editorFontSize)
      frame.setReadingComfort(effective.editorLineHeight, effective.editorLetterSpacing)
    }

    frame.setBook(
      JSON.stringify({
        chapters:
          book?.chapters.map((chapter) => ({
            guid: chapter.guid,
            title: chapter.title,
            act: chapter.act,
            scenes: chapter.scenes.map((scene) => ({
              chapterGuid: scene.chapterGuid,
              sceneId: scene.sceneId,
              sceneTitle: scene.sceneTitle,
              html: scene.html,
              segments: scene.segments.map((segment) => ({
                key: segment.key,
                kind: segment.kind,
                speakerId: segment.speakerId,
                // Hovering a line says who reads it and how, without the writer
                // having to select it to find out.
                label:
                  (segment.speakerName ?? t('narration.narrator')) +
                  ' · ' +
                  (voiceInfersDelivery(segment.voiceId)
                    ? t('narration.automaticDelivery')
                    : t(`emotion.${segment.directionKey}`, segment.directionKey))
              }))
            }))
          })) ?? [],
        colours,
        narratorColour: getComputedStyle(document.documentElement)
          .getPropertyValue('--nl-text-subtle')
          .trim(),
        emptyLabel: t('narration.emptyBook')
      })
    )
  }

export function useNarrationFrame(props: NarrationFrameProps) {
  const {book, colours, sceneNavigation, speaking, ready, rendering, selected} = props
  const frameRef = useRef<HTMLIFrameElement>(null)
  const readyRef = useRef(false)

  const pushBook = (): void => pushNarrationBook({...props, frameRef, readyRef})
  // The listener is installed once, but the book and cast can finish loading
  // before the frame. Its ready message must push the latest render's data.
  const pushBookRef = useRef(pushBook)
  pushBookRef.current = pushBook

  // eslint-disable-next-line react-hooks/exhaustive-deps
  useEffect(pushBook, [book, colours])

  const revealRequestedScene = (): void => {
    const frame = frameRef.current?.contentWindow as NarrationWindow | null
    if (readyRef.current && frame && sceneNavigation) {
      frame.revealScene(sceneNavigation.sceneId)
    }
  }
  const revealSceneRef = useRef(revealRequestedScene)
  revealSceneRef.current = revealRequestedScene
  useNarrationMessages(frameRef, readyRef, pushBookRef, revealSceneRef)
  // Book arrival must replay a request made while the reading was loading.
  // eslint-disable-next-line react-hooks/exhaustive-deps
  useEffect(revealRequestedScene, [sceneNavigation, book])

  useEffect(() => {
    const frame = frameRef.current?.contentWindow as NarrationWindow | null
    if (!frame || !readyRef.current) return
    frame.setSpeaking(speaking?.sceneId ?? null, speaking?.key ?? null)
  }, [speaking])

  // What has been made and what is being made, on the page. A reading is built
  // ahead of where it is being played, and a writer who cannot see that has no
  // way to tell a model thinking from a feature that has stopped.
  useEffect(() => {
    const frame = frameRef.current?.contentWindow as NarrationWindow | null
    if (!frame || !readyRef.current) return
    frame.setProgress(JSON.stringify(Object.keys(ready)), JSON.stringify(rendering))
  }, [ready, rendering, book])

  useEffect(() => {
    const frame = frameRef.current?.contentWindow as NarrationWindow | null
    if (!frame || !readyRef.current) return
    frame.setSelected(selected?.sceneId ?? null, selected?.key ?? null, false)
  }, [selected])

  return frameRef
}

function useNarrationMessages(frameRef: React.RefObject<HTMLIFrameElement | null>, readyRef: React.RefObject<boolean>, pushBookRef: React.RefObject<() => void>, revealSceneRef: React.RefObject<() => void>): void {
  // Not gated on the frame being mounted yet. It was, and the listener was
  // therefore never attached: the frame was only in the tree once the book had
  // loaded, so on the first render there was nothing to read a ready message
  // from and the prose never arrived.
  useEffect(() => {
    const onMessage = (event: MessageEvent): void => {
      if (!frameRef.current || event.source !== frameRef.current.contentWindow) return
      const raw = (event.data as { novalistNarration?: string })?.novalistNarration
      if (typeof raw !== 'string') return
      let message: { type: string; [key: string]: unknown }
      try {
        message = JSON.parse(raw)
      } catch {
        return
      }
      if (message.type === 'ready') {
        readyRef.current = true
        pushBookRef.current()
        revealSceneRef.current()
      } else if (message.type === 'segmentClicked') {
        // The frame marks up utterances and knows them by their own key. The
        // line an utterance belongs to is the store's business, because that is
        // what a speaker or a direction gets written against - a speech cut
        // into three sentences is still one line to correct.
        const chapterGuid = String(message.chapterGuid)
        const sceneId = String(message.sceneId)
        const key = String(message.key)
        const step = useNarrationStore
          .getState()
          .reading.find(
            (s) => s.chapterGuid === chapterGuid && s.sceneId === sceneId && s.segment.key === key
          )
        useNarrationStore.getState().select({
          chapterGuid,
          sceneId,
          key,
          lineKey: step?.segment.lineKey ?? key
        })
      }
    }

    window.addEventListener('message', onMessage)
    return () => {
      window.removeEventListener('message', onMessage)
      readyRef.current = false
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

}
